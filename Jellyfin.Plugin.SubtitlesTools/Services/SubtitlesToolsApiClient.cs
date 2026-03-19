using System;
using System.Diagnostics;
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
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<SubtitlesToolsApiClient> _logger;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// 初始化服务端客户端。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端。</param>
    /// <param name="logger">日志器。</param>
    public SubtitlesToolsApiClient(
        HttpClient httpClient,
        ILogger<SubtitlesToolsApiClient> logger)
        : this(httpClient, logger, GetPluginConfiguration)
    {
    }

    /// <summary>
    /// 仅供测试或特殊场景替换配置读取方式。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="configurationAccessor">配置访问器。</param>
    internal SubtitlesToolsApiClient(
        HttpClient httpClient,
        ILogger<SubtitlesToolsApiClient> logger,
        Func<PluginConfiguration> configurationAccessor)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configurationAccessor = configurationAccessor;
    }

    /// <summary>
    /// 检查 Python 服务健康状态。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>健康检查结果。</returns>
    public async Task<ServiceHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        var configuration = GetNormalizedConfiguration();
        return await CheckHealthAsync(configuration, cancellationToken, traceId).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用指定配置检查 Python 服务健康状态。
    /// </summary>
    /// <param name="configuration">待测试的服务配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>健康检查结果。</returns>
    internal async Task<ServiceHealthResult> CheckHealthAsync(
        PluginConfiguration configuration,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(configuration.ServiceBaseUrl, "/health"));
        using var response = await SendAsync(
            configuration,
            request,
            operationName: "health",
            traceId: traceId,
            cancellationToken).ConfigureAwait(false);
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
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>字幕候选列表。</returns>
    public async Task<SubtitleSearchResponseDto> SearchAsync(
        SubtitleSearchRequestDto request,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = GetNormalizedConfiguration();
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(configuration.ServiceBaseUrl, "/api/v1/subtitles/search"))
        {
            Content = JsonContent.Create(request, options: JsonSerializerOptions)
        };

        using var response = await SendAsync(
            configuration,
            message,
            operationName: "search",
            traceId: traceId,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadJsonAsync<SubtitleSearchResponseDto>(response, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "trace={TraceId} client_search_response matched_by={MatchedBy} confidence={Confidence} items={ItemCount}",
            NormalizeTraceId(traceId),
            result.MatchedBy,
            result.Confidence,
            result.Items.Count);
        return result;
    }

    /// <summary>
    /// 下载服务端代理的字幕内容。
    /// </summary>
    /// <param name="subtitleId">服务端返回的字幕标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>字幕下载结果。</returns>
    public async Task<DownloadedSubtitle> DownloadSubtitleAsync(
        string subtitleId,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        if (string.IsNullOrWhiteSpace(subtitleId))
        {
            throw new ArgumentException("字幕标识不能为空。", nameof(subtitleId));
        }

        var configuration = GetNormalizedConfiguration();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(configuration.ServiceBaseUrl, $"/api/v1/subtitles/{Uri.EscapeDataString(subtitleId)}"));

        using var response = await SendAsync(
            configuration,
            request,
            operationName: "download",
            traceId: traceId,
            cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"subtitle-{subtitleId}.srt";
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        _logger.LogInformation(
            "trace={TraceId} client_download_response subtitle_id={SubtitleId} file_name={FileName} media_type={MediaType} bytes={ByteCount}",
            NormalizeTraceId(traceId),
            subtitleId,
            fileName,
            mediaType,
            content.Length);

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
        string operationName,
        string? traceId,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
        var normalizedTraceId = NormalizeTraceId(traceId);
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            request.Headers.TryAddWithoutValidation("X-Subtitles-Trace-Id", traceId);
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "trace={TraceId} client_http_start operation={Operation} method={Method} url={Url} timeout_s={TimeoutSeconds}",
            normalizedTraceId,
            operationName,
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            configuration.RequestTimeoutSeconds);

        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCancellationTokenSource.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "trace={TraceId} client_http_complete operation={Operation} method={Method} url={Url} status={StatusCode} total_ms={ElapsedMs:F2}",
                    normalizedTraceId,
                    operationName,
                    request.Method.Method,
                    request.RequestUri?.ToString() ?? string.Empty,
                    (int)response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds);
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "trace={TraceId} client_http_failed operation={Operation} method={Method} url={Url} status={StatusCode} total_ms={ElapsedMs:F2} body={Body}",
                normalizedTraceId,
                operationName,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                string.IsNullOrWhiteSpace(body) ? "[empty]" : body);
            response.Dispose();
            throw new SubtitlesToolsApiException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "服务端返回异常状态码 {0}：{1}",
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(body) ? "[empty]" : body));
        }
        catch (TaskCanceledException ex)
            when (timeoutCancellationTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "trace={TraceId} client_http_timeout operation={Operation} method={Method} url={Url} total_ms={ElapsedMs:F2}",
                normalizedTraceId,
                operationName,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                stopwatch.Elapsed.TotalMilliseconds);
            throw new SubtitlesToolsApiException("访问 Python 服务超时。", ex);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "trace={TraceId} client_http_request_failed operation={Operation} method={Method} url={Url} total_ms={ElapsedMs:F2}",
                normalizedTraceId,
                operationName,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                stopwatch.Elapsed.TotalMilliseconds);
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

    private static string NormalizeTraceId(string? traceId)
    {
        return string.IsNullOrWhiteSpace(traceId) ? "-" : traceId;
    }
}
