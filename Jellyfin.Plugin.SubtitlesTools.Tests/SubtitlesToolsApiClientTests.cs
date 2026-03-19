using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Services;
using Jellyfin.Plugin.SubtitlesTools.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验 Python 服务端客户端封装。
/// </summary>
public sealed class SubtitlesToolsApiClientTests
{
    /// <summary>
    /// 健康检查应正确解析响应字段。
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_ShouldParsePayload()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"status\":\"ok\",\"version\":\"0.1.0\",\"provider_name\":\"thunder\",\"provider_available\":true}",
                    Encoding.UTF8,
                    "application/json")
            };

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://127.0.0.1:8055/health", request.RequestUri?.ToString());
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = new SubtitlesToolsApiClient(
            httpClient,
            NullLogger<SubtitlesToolsApiClient>.Instance,
            () => new PluginConfiguration
            {
                ServiceBaseUrl = "http://127.0.0.1:8055",
                RequestTimeoutSeconds = 10
            });

        var result = await apiClient.CheckHealthAsync(CancellationToken.None);

        Assert.Equal("http://127.0.0.1:8055", result.ServiceBaseUrl);
        Assert.Equal(10, result.TimeoutSeconds);
        Assert.Equal("ok", result.Health.Status);
        Assert.Equal("0.1.0", result.Health.Version);
        Assert.True(result.Health.ProviderAvailable);
    }

    /// <summary>
    /// 下载字幕时应正确解析文件名和内容类型。
    /// </summary>
    [Fact]
    public async Task DownloadSubtitleAsync_ShouldParseFilenameAndContentType()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\nhello\n"))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-subrip");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = "demo.srt"
            };

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://127.0.0.1:8055/api/v1/subtitles/sub-1", request.RequestUri?.ToString());
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = new SubtitlesToolsApiClient(
            httpClient,
            NullLogger<SubtitlesToolsApiClient>.Instance,
            () => new PluginConfiguration());

        var result = await apiClient.DownloadSubtitleAsync("sub-1", CancellationToken.None);

        Assert.Equal("demo.srt", result.FileName);
        Assert.Equal("application/x-subrip", result.MediaType);
        Assert.NotEmpty(result.Content);
    }

    /// <summary>
    /// 搜索字幕时应透传链路追踪标识，便于与服务端日志关联。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldSendTraceHeader()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"matched_by\":\"gcid\",\"confidence\":\"high\",\"items\":[]}",
                    Encoding.UTF8,
                    "application/json")
            };

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://127.0.0.1:8055/api/v1/subtitles/search", request.RequestUri?.ToString());
            Assert.True(request.Headers.TryGetValues("X-Subtitles-Trace-Id", out var values));
            Assert.Contains("trace-123", values);
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var apiClient = new SubtitlesToolsApiClient(
            httpClient,
            NullLogger<SubtitlesToolsApiClient>.Instance,
            () => new PluginConfiguration());

        var result = await apiClient.SearchAsync(
            new SubtitleSearchRequestDto
            {
                Gcid = "GCID-ONE",
                Name = "demo.mkv"
            },
            CancellationToken.None,
            "trace-123");

        Assert.Equal("gcid", result.MatchedBy);
        Assert.Empty(result.Items);
    }
}
