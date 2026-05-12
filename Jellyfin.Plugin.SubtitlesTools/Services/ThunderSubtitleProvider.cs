using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 访问迅雷在线字幕接口，负责上游查询和字幕文件下载。
/// </summary>
public sealed class ThunderSubtitleProvider
{
    /// <summary>
    /// 字幕源名称。
    /// </summary>
    public const string ProviderName = "thunder";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<ThunderSubtitleProvider> _logger;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// 初始化迅雷字幕源。
    /// </summary>
    /// <param name="httpClient">共享 HTTP 客户端。</param>
    /// <param name="logger">日志器。</param>
    public ThunderSubtitleProvider(HttpClient httpClient, ILogger<ThunderSubtitleProvider> logger)
        : this(httpClient, logger, GetPluginConfiguration)
    {
    }

    /// <summary>
    /// 使用指定配置读取器初始化迅雷字幕源；该构造器供测试注入配置。
    /// </summary>
    /// <param name="httpClient">共享 HTTP 客户端。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="configurationAccessor">配置读取器。</param>
    internal ThunderSubtitleProvider(
        HttpClient httpClient,
        ILogger<ThunderSubtitleProvider> logger,
        Func<PluginConfiguration> configurationAccessor)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configurationAccessor = configurationAccessor;
    }

    /// <summary>
    /// 按 GCID 查询迅雷字幕。
    /// </summary>
    /// <param name="gcid">媒体文件 GCID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>上游返回并规范化后的字幕候选。</returns>
    internal async Task<List<ProviderSubtitle>> SearchByGcidAsync(
        string gcid,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        return await SearchAsync(new Dictionary<string, string>(StringComparer.Ordinal) { ["gcid"] = gcid }, traceId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 按文件名查询迅雷字幕。
    /// </summary>
    /// <param name="name">媒体文件名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>上游返回并规范化后的字幕候选。</returns>
    internal async Task<List<ProviderSubtitle>> SearchByNameAsync(
        string name,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        return await SearchAsync(new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = name }, traceId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 从缓存元数据记录的上游地址下载字幕文件。
    /// </summary>
    /// <param name="url">上游字幕下载地址。</param>
    /// <param name="fileName">字幕文件名。</param>
    /// <param name="ext">字幕扩展名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>字幕下载结果。</returns>
    internal async Task<DownloadedSubtitle> DownloadSubtitleAsync(
        string url,
        string fileName,
        string ext,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new SubtitleProviderException("字幕下载地址不是合法的绝对 URL。");
        }

        var traceLabel = NormalizeTraceId(traceId);
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, "thunder_download", traceLabel, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var mediaType = response.Content.Headers.ContentType?.ToString();
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            mediaType = GuessMediaType(ext);
        }

        _logger.LogInformation(
            "trace={TraceId} thunder_download_complete url={Url} bytes={ByteCount} total_ms={ElapsedMs:F2}",
            traceLabel,
            url,
            content.Length,
            stopwatch.Elapsed.TotalMilliseconds);

        return new DownloadedSubtitle
        {
            FileName = fileName,
            MediaType = mediaType,
            Content = content
        };
    }

    private async Task<List<ProviderSubtitle>> SearchAsync(
        IReadOnlyDictionary<string, string> query,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var traceLabel = NormalizeTraceId(traceId);
        var queryMode = query.Keys.FirstOrDefault() ?? "unknown";
        var configuration = GetNormalizedConfiguration();
        var requestUri = BuildSearchUri(configuration.ThunderBaseUrl, query);
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await SendAsync(request, "thunder_search", traceLabel, cancellationToken).ConfigureAwait(false);

        ThunderSearchResponseDto? payload;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            payload = await JsonSerializer.DeserializeAsync<ThunderSearchResponseDto>(
                stream,
                JsonSerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new SubtitleProviderException("迅雷字幕接口返回了无法解析的数据。", ex);
        }

        if (payload is null)
        {
            throw new SubtitleProviderException("迅雷字幕接口返回了空响应。");
        }

        if (payload.Code != 0 || !string.Equals(payload.Result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new SubtitleProviderException("迅雷字幕接口返回了失败状态。");
        }

        var items = payload.Data
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .Select(NormalizeItem)
            .ToList();
        stopwatch.Stop();
        _logger.LogInformation(
            "trace={TraceId} thunder_search_complete mode={Mode} items={ItemCount} total_ms={ElapsedMs:F2}",
            traceLabel,
            queryMode,
            items.Count,
            stopwatch.Elapsed.TotalMilliseconds);
        return items;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string operationName,
        string traceId,
        CancellationToken cancellationToken)
    {
        var configuration = GetNormalizedConfiguration();
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "trace={TraceId} provider_http_start operation={Operation} method={Method} url={Url} timeout_s={TimeoutSeconds}",
            traceId,
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
                    "trace={TraceId} provider_http_complete operation={Operation} method={Method} url={Url} status={StatusCode} total_ms={ElapsedMs:F2}",
                    traceId,
                    operationName,
                    request.Method.Method,
                    request.RequestUri?.ToString() ?? string.Empty,
                    (int)response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds);
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "trace={TraceId} provider_http_failed operation={Operation} method={Method} url={Url} status={StatusCode} total_ms={ElapsedMs:F2} body={Body}",
                traceId,
                operationName,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                string.IsNullOrWhiteSpace(body) ? "[empty]" : body);
            response.Dispose();
            throw new SubtitleProviderException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "迅雷字幕接口返回异常状态码 {0}。",
                    (int)response.StatusCode));
        }
        catch (TaskCanceledException ex)
            when (timeoutCancellationTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "trace={TraceId} provider_http_timeout operation={Operation} method={Method} url={Url} total_ms={ElapsedMs:F2}",
                traceId,
                operationName,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                stopwatch.Elapsed.TotalMilliseconds);
            throw new SubtitleProviderTimeoutException("访问迅雷字幕接口超时。", ex);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "trace={TraceId} provider_http_request_failed operation={Operation} method={Method} url={Url} total_ms={ElapsedMs:F2}",
                traceId,
                operationName,
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                stopwatch.Elapsed.TotalMilliseconds);
            throw new SubtitleProviderException("访问迅雷字幕接口失败。", ex);
        }
    }

    private static ProviderSubtitle NormalizeItem(ThunderSubtitlePayloadDto item)
    {
        return new ProviderSubtitle
        {
            Provider = ProviderName,
            Url = item.Url,
            Gcid = item.Gcid,
            Cid = item.Cid,
            Name = item.Name,
            Ext = item.Ext.Trim().TrimStart('.').ToLowerInvariant(),
            Languages = item.Languages.Where(language => !string.IsNullOrWhiteSpace(language)).ToList(),
            DurationMilliseconds = item.Duration,
            Source = item.Source,
            Score = item.Score,
            FingerprintScore = item.FingerprintScore,
            ExtraName = item.ExtraName
        };
    }

    private static Uri BuildSearchUri(string baseUrl, IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder($"{baseUrl.TrimEnd('/')}/oracle/subtitle")
        {
            Query = string.Join(
                "&",
                query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"))
        };
        return builder.Uri;
    }

    private static PluginConfiguration GetPluginConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private PluginConfiguration GetNormalizedConfiguration()
    {
        var configuration = _configurationAccessor();
        return new PluginConfiguration
        {
            ThunderBaseUrl = PluginConfiguration.NormalizeThunderBaseUrl(configuration.ThunderBaseUrl),
            RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(configuration.RequestTimeoutSeconds)
        };
    }

    private static string GuessMediaType(string ext)
    {
        return ext.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "ass" => "text/x-ssa; charset=utf-8",
            "srt" => "application/x-subrip; charset=utf-8",
            "ssa" => "text/x-ssa; charset=utf-8",
            "vtt" => "text/vtt; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static string NormalizeTraceId(string? traceId)
    {
        return string.IsNullOrWhiteSpace(traceId) ? "-" : traceId;
    }

    /// <summary>
    /// 迅雷字幕接口响应体。
    /// </summary>
    private sealed class ThunderSearchResponseDto
    {
        /// <summary>
        /// 获取或设置上游状态码。
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 获取或设置上游字幕条目。
        /// </summary>
        [JsonPropertyName("data")]
        public List<ThunderSubtitlePayloadDto> Data { get; set; } = [];

        /// <summary>
        /// 获取或设置上游状态文本。
        /// </summary>
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;
    }

    /// <summary>
    /// 迅雷接口返回的单个字幕项。
    /// </summary>
    private sealed class ThunderSubtitlePayloadDto
    {
        /// <summary>
        /// 获取或设置上游返回的 GCID。
        /// </summary>
        [JsonPropertyName("gcid")]
        public string? Gcid { get; set; }

        /// <summary>
        /// 获取或设置上游返回的 CID。
        /// </summary>
        [JsonPropertyName("cid")]
        public string? Cid { get; set; }

        /// <summary>
        /// 获取或设置上游字幕下载地址。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置字幕扩展名。
        /// </summary>
        [JsonPropertyName("ext")]
        public string Ext { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置字幕文件名。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置字幕时长，单位为毫秒。
        /// </summary>
        [JsonPropertyName("duration")]
        public int Duration { get; set; }

        /// <summary>
        /// 获取或设置字幕语言列表。
        /// </summary>
        [JsonPropertyName("languages")]
        public List<string> Languages { get; set; } = [];

        /// <summary>
        /// 获取或设置上游字幕来源编号。
        /// </summary>
        [JsonPropertyName("source")]
        public int Source { get; set; }

        /// <summary>
        /// 获取或设置上游原始评分。
        /// </summary>
        [JsonPropertyName("score")]
        public int Score { get; set; }

        /// <summary>
        /// 获取或设置上游指纹评分；字段名兼容迅雷接口现有拼写。
        /// </summary>
        [JsonPropertyName("fingerprintf_score")]
        public double FingerprintScore { get; set; }

        /// <summary>
        /// 获取或设置上游补充说明。
        /// </summary>
        [JsonPropertyName("extra_name")]
        public string? ExtraName { get; set; }
    }
}
