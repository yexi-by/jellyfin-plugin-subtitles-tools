using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// Python 服务端客户端，负责健康检查、字幕搜索和字幕下载。
/// </summary>
public sealed class SubtitlesToolsApiClient
{
    /// <summary>
    /// 复用的 HTTP 客户端名称。
    /// </summary>
    public const string HttpClientName = "SubtitlesTools";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SubtitlesToolsApiClient> _logger;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// 初始化服务端客户端。
    /// </summary>
    /// <param name="httpClientFactory">HTTP 客户端工厂。</param>
    /// <param name="logger">日志器。</param>
    public SubtitlesToolsApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<SubtitlesToolsApiClient> logger)
        : this(httpClientFactory, logger, GetPluginConfiguration)
    {
    }

    /// <summary>
    /// 仅供测试或特殊场景替换配置读取方式。
    /// </summary>
    /// <param name="httpClientFactory">HTTP 客户端工厂。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="configurationAccessor">配置访问器。</param>
    internal SubtitlesToolsApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<SubtitlesToolsApiClient> logger,
        Func<PluginConfiguration> configurationAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configurationAccessor = configurationAccessor;
    }

    /// <summary>
    /// 检查 Python 服务健康状态。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>健康检查结果。</returns>
    public async Task<ServiceHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var configuration = GetNormalizedConfiguration();
        return await CheckHealthAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用指定配置检查 Python 服务健康状态。
    /// </summary>
    /// <param name="configuration">待测试的服务配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>健康检查结果。</returns>
    internal async Task<ServiceHealthResult> CheckHealthAsync(
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(configuration.ServiceBaseUrl, "/health"));
        using var response = await SendAsync(configuration, request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadJsonAsync<HealthResponseDto>(response, cancellationToken).ConfigureAwait(false);

        return new ServiceHealthResult
        {
            ServiceBaseUrl = configuration.ServiceBaseUrl,
            TimeoutSeconds = configuration.RequestTimeoutSeconds,
            Health = payload
        };
    }

    /// <summary>
    /// 调用 Python 服务搜索字幕。
    /// </summary>
    /// <param name="request">字幕搜索请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>字幕候选列表。</returns>
    public async Task<SubtitleSearchResponseDto> SearchAsync(
        SubtitleSearchRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = GetNormalizedConfiguration();
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(configuration.ServiceBaseUrl, "/api/v1/subtitles/search"))
        {
            Content = JsonContent.Create(request, options: JsonSerializerOptions)
        };

        using var response = await SendAsync(configuration, message, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<SubtitleSearchResponseDto>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 下载服务端代理的字幕内容。
    /// </summary>
    /// <param name="subtitleId">服务端返回的字幕标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>字幕下载结果。</returns>
    public async Task<DownloadedSubtitle> DownloadSubtitleAsync(
        string subtitleId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subtitleId))
        {
            throw new ArgumentException("字幕标识不能为空。", nameof(subtitleId));
        }

        var configuration = GetNormalizedConfiguration();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(configuration.ServiceBaseUrl, $"/api/v1/subtitles/{Uri.EscapeDataString(subtitleId)}"));

        using var response = await SendAsync(configuration, request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"subtitle-{subtitleId}.srt";
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        return new DownloadedSubtitle
        {
            FileName = fileName,
            MediaType = mediaType,
            Content = content
        };
    }

    private static PluginConfiguration GetPluginConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
    {
        return new($"{baseUrl.TrimEnd('/')}{relativePath}", UriKind.Absolute);
    }

    private PluginConfiguration GetNormalizedConfiguration()
    {
        var configuration = _configurationAccessor();
        return new PluginConfiguration
        {
            ServiceBaseUrl = PluginConfiguration.NormalizeServiceBaseUrl(configuration.ServiceBaseUrl),
            RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(configuration.RequestTimeoutSeconds)
        };
    }

    private async Task<HttpResponseMessage> SendAsync(
        PluginConfiguration configuration,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds);

        try
        {
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();
            throw new SubtitlesToolsApiException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "服务端返回异常状态码 {0}：{1}",
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(body) ? "[empty]" : body));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "访问 Python 服务超时。");
            throw new SubtitlesToolsApiException("访问 Python 服务超时。", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "访问 Python 服务失败。");
            throw new SubtitlesToolsApiException("访问 Python 服务失败。", ex);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonSerializerOptions,
            cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            throw new SubtitlesToolsApiException("服务端返回了空 JSON 响应。");
        }

        return payload;
    }
}
