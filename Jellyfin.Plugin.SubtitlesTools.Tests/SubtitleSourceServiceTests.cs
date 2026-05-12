using System.Net;
using System.Text;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Jellyfin.Plugin.SubtitlesTools.Services;
using Jellyfin.Plugin.SubtitlesTools.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验内置字幕源的迅雷查询、缓存、下载和错误处理。
/// </summary>
public sealed class SubtitleSourceServiceTests
{
    /// <summary>
    /// 迅雷 provider 应能按 GCID 查询并映射上游字段。
    /// </summary>
    [Fact]
    public async Task ThunderProvider_SearchByGcidAsync_ShouldParsePayload()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api-shoulei-ssl.xunlei.com/oracle/subtitle?gcid=GCID-ONE", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(BuildSearchPayload("https://subtitle.example/1.srt")));
        });

        using var httpClient = new HttpClient(handler);
        var provider = BuildProvider(httpClient);

        var items = await provider.SearchByGcidAsync("GCID-ONE", CancellationToken.None, "trace-test");

        Assert.Single(items);
        Assert.Equal("thunder", items[0].Provider);
        Assert.Equal("srt", items[0].Ext);
        Assert.Equal(88.8, items[0].FingerprintScore);
        Assert.Equal(["chi", "eng"], items[0].Languages);
    }

    /// <summary>
    /// 字幕搜索应在 GCID 无结果时按文件名回退。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldFallbackToNameWhenGcidHasNoResults()
    {
        var gcidCalls = 0;
        var nameCalls = 0;
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            if (HasQueryValue(request.RequestUri, "gcid", "GCID-EMPTY"))
            {
                gcidCalls += 1;
                return Task.FromResult(JsonResponse("{\"code\":0,\"result\":\"ok\",\"data\":[]}"));
            }

            if (HasQueryValue(request.RequestUri, "name", "demo.mkv"))
            {
                nameCalls += 1;
                return Task.FromResult(JsonResponse(BuildSearchPayload("https://subtitle.example/name.srt")));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var httpClient = new HttpClient(handler);
        var service = BuildService(httpClient, CreateTempCachePath());

        var result = await service.SearchAsync(
            new SubtitleSearchRequestDto
            {
                Gcid = "GCID-EMPTY",
                Name = "demo.mkv"
            },
            CancellationToken.None,
            "trace-test");

        Assert.Equal("name", result.MatchedBy);
        Assert.Equal("fallback", result.Confidence);
        Assert.Single(result.Items);
        Assert.Equal(1, gcidCalls);
        Assert.Equal(1, nameCalls);
    }

    /// <summary>
    /// 相同上游地址的字幕项应只保留排序最高的一条。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldDeduplicateSameUrlByRank()
    {
        var payload = """
            {
              "code": 0,
              "result": "ok",
              "data": [
                {
                  "gcid": "GCID-ONE",
                  "cid": "CID-ONE",
                  "url": "https://subtitle.example/dup.srt",
                  "ext": "srt",
                  "name": "低分字幕.srt",
                  "duration": 100,
                  "languages": ["chi"],
                  "source": 1,
                  "score": 1,
                  "fingerprintf_score": 1.0,
                  "extra_name": "低分"
                },
                {
                  "gcid": "GCID-ONE",
                  "cid": "CID-ONE",
                  "url": "https://subtitle.example/dup.srt",
                  "ext": "srt",
                  "name": "高分字幕.srt",
                  "duration": 200,
                  "languages": ["chi", "eng"],
                  "source": 1,
                  "score": 200,
                  "fingerprintf_score": 99.0,
                  "extra_name": "高分"
                }
              ]
            }
            """;
        var handler = new TestHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(payload)));
        using var httpClient = new HttpClient(handler);
        var service = BuildService(httpClient, CreateTempCachePath());

        var result = await service.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-ONE" }, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("高分字幕.srt", result.Items[0].Name);
        Assert.Equal(200, result.Items[0].Score);
    }

    /// <summary>
    /// 相同搜索命中缓存时不应重复访问迅雷。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldUseMemoryCacheForRepeatedSearch()
    {
        var upstreamCalls = 0;
        var handler = new TestHttpMessageHandler((_, _) =>
        {
            upstreamCalls += 1;
            return Task.FromResult(JsonResponse(BuildSearchPayload("https://subtitle.example/cache.srt")));
        });
        using var httpClient = new HttpClient(handler);
        var service = BuildService(httpClient, CreateTempCachePath());

        var first = await service.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-CACHE" }, CancellationToken.None);
        var second = await service.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-CACHE" }, CancellationToken.None);

        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal(1, upstreamCalls);
    }

    /// <summary>
    /// 搜索磁盘缓存应能在服务重建后继续复用。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldReuseDiskCacheAfterRestart()
    {
        var cachePath = CreateTempCachePath();
        var upstreamCalls = 0;
        var handler = new TestHttpMessageHandler((_, _) =>
        {
            upstreamCalls += 1;
            return Task.FromResult(JsonResponse(BuildSearchPayload("https://subtitle.example/restart.srt")));
        });

        using var firstHttpClient = new HttpClient(handler);
        var firstService = BuildService(firstHttpClient, cachePath);
        var first = await firstService.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-RESTART" }, CancellationToken.None);

        using var secondHttpClient = new HttpClient(new TestHttpMessageHandler((_, _) => throw new InvalidOperationException("不应再次访问上游。")));
        var secondService = BuildService(secondHttpClient, cachePath);
        var second = await secondService.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-RESTART" }, CancellationToken.None);

        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal(1, upstreamCalls);
    }

    /// <summary>
    /// 字幕下载应只使用搜索阶段缓存的元数据，并缓存下载内容。
    /// </summary>
    [Fact]
    public async Task DownloadSubtitleAsync_ShouldUseMetadataAndContentCache()
    {
        var downloadCalls = 0;
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.Host == "subtitle.example")
            {
                downloadCalls += 1;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "1\n00:00:01,000 --> 00:00:02,000\n测试字幕\n",
                        Encoding.UTF8,
                        "application/x-subrip")
                });
            }

            return Task.FromResult(JsonResponse(BuildSearchPayload("https://subtitle.example/download.srt")));
        });
        using var httpClient = new HttpClient(handler);
        var service = BuildService(httpClient, CreateTempCachePath());
        var search = await service.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-DOWNLOAD" }, CancellationToken.None);
        var subtitleId = search.Items[0].Id;

        var first = await service.DownloadSubtitleAsync(subtitleId, CancellationToken.None);
        var second = await service.DownloadSubtitleAsync(subtitleId, CancellationToken.None);

        Assert.Equal(first.Content, second.Content);
        Assert.Equal("测试字幕.srt", first.FileName);
        Assert.Equal(1, downloadCalls);
    }

    /// <summary>
    /// 未知字幕标识应显式返回业务异常。
    /// </summary>
    [Fact]
    public async Task DownloadSubtitleAsync_ShouldRejectUnknownSubtitleId()
    {
        using var httpClient = new HttpClient(new TestHttpMessageHandler((_, _) => throw new InvalidOperationException("不应访问上游。")));
        var service = BuildService(httpClient, CreateTempCachePath());

        await Assert.ThrowsAsync<SubtitleNotFoundException>(() => service.DownloadSubtitleAsync("unknown", CancellationToken.None));
    }

    /// <summary>
    /// 上游失败状态应转换为字幕源异常。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldMapUpstreamFailureToProviderException()
    {
        var handler = new TestHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("{\"code\":1,\"result\":\"fail\",\"data\":[]}")));
        using var httpClient = new HttpClient(handler);
        var service = BuildService(httpClient, CreateTempCachePath());

        await Assert.ThrowsAsync<SubtitleProviderException>(() => service.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-FAIL" }, CancellationToken.None));
    }

    /// <summary>
    /// 上游超时应转换为字幕源超时异常。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldMapTimeoutToProviderTimeoutException()
    {
        var handler = new TestHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return JsonResponse(BuildSearchPayload("https://subtitle.example/timeout.srt"));
        });
        using var httpClient = new HttpClient(handler);
        var service = BuildService(httpClient, CreateTempCachePath(), timeoutSeconds: 1);

        await Assert.ThrowsAsync<SubtitleProviderTimeoutException>(() => service.SearchAsync(new SubtitleSearchRequestDto { Gcid = "GCID-TIMEOUT" }, CancellationToken.None));
    }

    private static SubtitleSourceService BuildService(HttpClient httpClient, string cachePath, int timeoutSeconds = 10)
    {
        var configuration = new PluginConfiguration
        {
            ThunderBaseUrl = "https://api-shoulei-ssl.xunlei.com",
            RequestTimeoutSeconds = timeoutSeconds,
            SearchCacheTtlSeconds = 86400,
            SubtitleCacheTtlSeconds = 604800
        };
        var provider = new ThunderSubtitleProvider(
            httpClient,
            NullLogger<ThunderSubtitleProvider>.Instance,
            () => configuration);
        return new SubtitleSourceService(
            new SubtitleSourceCacheStore(cachePath),
            provider,
            NullLogger<SubtitleSourceService>.Instance,
            () => configuration);
    }

    private static ThunderSubtitleProvider BuildProvider(HttpClient httpClient)
    {
        return new ThunderSubtitleProvider(
            httpClient,
            NullLogger<ThunderSubtitleProvider>.Instance,
            () => new PluginConfiguration
            {
                ThunderBaseUrl = "https://api-shoulei-ssl.xunlei.com",
                RequestTimeoutSeconds = 10
            });
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string BuildSearchPayload(string url)
    {
        return $$"""
            {
              "code": 0,
              "result": "ok",
              "data": [
                {
                  "gcid": "GCID-ONE",
                  "cid": "CID-ONE",
                  "url": "{{url}}",
                  "ext": "srt",
                  "name": "测试字幕.srt",
                  "duration": 123456,
                  "languages": ["chi", "eng"],
                  "source": 1,
                  "score": 100,
                  "fingerprintf_score": 88.8,
                  "extra_name": "网友上传"
                }
              ]
            }
            """;
    }

    private static bool HasQueryValue(Uri? uri, string key, string value)
    {
        if (uri is null)
        {
            return false;
        }

        var expected = $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
        return uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Contains(expected, StringComparer.Ordinal);
    }

    private static string CreateTempCachePath()
    {
        return Path.Combine(Path.GetTempPath(), "jellyfin-plugin-subtitles-tools-tests", Guid.NewGuid().ToString("N"));
    }
}
