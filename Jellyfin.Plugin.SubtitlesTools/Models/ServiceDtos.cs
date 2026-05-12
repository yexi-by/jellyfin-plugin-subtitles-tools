using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 内置字幕源健康检查响应。
/// </summary>
public sealed class HealthResponseDto
{
    /// <summary>
    /// 获取或设置服务状态。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置服务版本。
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置内置字幕源名称。
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕源上游地址。
    /// </summary>
    [JsonPropertyName("provider_base_url")]
    public string ProviderBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕源可用状态。
    /// </summary>
    [JsonPropertyName("provider_available")]
    public bool ProviderAvailable { get; set; }

    /// <summary>
    /// 获取或设置搜索缓存有效期秒数。
    /// </summary>
    [JsonPropertyName("search_cache_ttl_seconds")]
    public int SearchCacheTtlSeconds { get; set; }

    /// <summary>
    /// 获取或设置字幕文件缓存有效期秒数。
    /// </summary>
    [JsonPropertyName("subtitle_cache_ttl_seconds")]
    public int SubtitleCacheTtlSeconds { get; set; }
}

/// <summary>
/// 字幕搜索请求。
/// </summary>
public sealed class SubtitleSearchRequestDto
{
    /// <summary>
    /// 获取或设置 GCID。
    /// </summary>
    [JsonPropertyName("gcid")]
    public string? Gcid { get; set; }

    /// <summary>
    /// 获取或设置 CID。
    /// </summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; set; }

    /// <summary>
    /// 获取或设置文件名。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// 字幕搜索响应。
/// </summary>
public sealed class SubtitleSearchResponseDto
{
    /// <summary>
    /// 获取或设置命中的查询方式。
    /// </summary>
    [JsonPropertyName("matched_by")]
    public string MatchedBy { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果可信度。
    /// </summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕候选列表。
    /// </summary>
    [JsonPropertyName("items")]
    public List<SubtitleSearchItemDto> Items { get; set; } = [];
}

/// <summary>
/// 字幕候选项。
/// </summary>
public sealed class SubtitleSearchItemDto
{
    /// <summary>
    /// 获取或设置稳定字幕标识。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕扩展名。
    /// </summary>
    [JsonPropertyName("ext")]
    public string Ext { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置语言列表。
    /// </summary>
    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// 获取或设置字幕时长。
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public int DurationMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置字幕来源编号。
    /// </summary>
    [JsonPropertyName("source")]
    public int Source { get; set; }

    /// <summary>
    /// 获取或设置上游评分。
    /// </summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>
    /// 获取或设置上游指纹评分。
    /// </summary>
    [JsonPropertyName("fingerprint_score")]
    public double FingerprintScore { get; set; }

    /// <summary>
    /// 获取或设置额外说明。
    /// </summary>
    [JsonPropertyName("extra_name")]
    public string? ExtraName { get; set; }

}

/// <summary>
/// 字幕下载结果。
/// </summary>
public sealed class DownloadedSubtitle
{
    /// <summary>
    /// 获取或设置字幕文件名。
    /// </summary>
    public string FileName { get; set; } = "subtitle.srt";

    /// <summary>
    /// 获取或设置字幕媒体类型。
    /// </summary>
    public string MediaType { get; set; } = "application/octet-stream";

    /// <summary>
    /// 获取或设置字幕原始字节。
    /// </summary>
    public byte[] Content { get; set; } = [];
}

/// <summary>
/// 内置字幕源健康检查结果模型。
/// </summary>
public sealed class ServiceHealthResult
{
    /// <summary>
    /// 获取或设置访问上游字幕源的超时秒数。
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// 获取或设置健康检查明细。
    /// </summary>
    public HealthResponseDto Health { get; set; } = new();
}

/// <summary>
/// 字幕源返回的标准字幕条目。
/// </summary>
internal sealed class ProviderSubtitle
{
    /// <summary>
    /// 获取或设置字幕源名称。
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置上游字幕下载地址。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置上游返回的 GCID。
    /// </summary>
    public string? Gcid { get; set; }

    /// <summary>
    /// 获取或设置上游返回的 CID。
    /// </summary>
    public string? Cid { get; set; }

    /// <summary>
    /// 获取或设置字幕文件名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕扩展名。
    /// </summary>
    public string Ext { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕语言列表。
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// 获取或设置字幕时长，单位为毫秒。
    /// </summary>
    public int DurationMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置上游字幕来源编号。
    /// </summary>
    public int Source { get; set; }

    /// <summary>
    /// 获取或设置上游原始评分。
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 获取或设置上游指纹评分。
    /// </summary>
    public double FingerprintScore { get; set; }

    /// <summary>
    /// 获取或设置上游补充说明。
    /// </summary>
    public string? ExtraName { get; set; }
}

/// <summary>
/// 搜索缓存中的字幕项快照。
/// </summary>
internal sealed class SearchCacheItem
{
    /// <summary>
    /// 获取或设置稳定字幕标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕扩展名。
    /// </summary>
    public string Ext { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕语言列表。
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// 获取或设置字幕时长，单位为毫秒。
    /// </summary>
    public int DurationMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置字幕来源编号。
    /// </summary>
    public int Source { get; set; }

    /// <summary>
    /// 获取或设置上游评分。
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 获取或设置上游指纹评分。
    /// </summary>
    public double FingerprintScore { get; set; }

    /// <summary>
    /// 获取或设置上游补充说明。
    /// </summary>
    public string? ExtraName { get; set; }
}

/// <summary>
/// 搜索结果缓存条目。
/// </summary>
internal sealed class SearchCacheEntry
{
    /// <summary>
    /// 获取或设置本次命中的查询方式。
    /// </summary>
    public string MatchedBy { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置本次结果可信度。
    /// </summary>
    public string Confidence { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置搜索缓存过期时间。
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 获取或设置缓存的字幕项。
    /// </summary>
    public List<SearchCacheItem> Items { get; set; } = [];
}

/// <summary>
/// 字幕下载所需的缓存元数据。
/// </summary>
internal sealed class CachedSubtitleMetadata
{
    /// <summary>
    /// 获取或设置稳定字幕标识。
    /// </summary>
    public string SubtitleId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕源名称。
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置上游字幕下载地址。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕文件名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕扩展名。
    /// </summary>
    public string Ext { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕元数据和文件缓存过期时间。
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 字幕源业务异常基类。
/// </summary>
internal class SubtitleSourceException : Exception
{
    /// <summary>
    /// 初始化一个字幕源业务异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    public SubtitleSourceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化一个包含内部异常的字幕源业务异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    /// <param name="innerException">内部异常。</param>
    public SubtitleSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 上游字幕源访问超时异常。
/// </summary>
internal sealed class SubtitleProviderTimeoutException : SubtitleSourceException
{
    /// <summary>
    /// 初始化一个上游字幕源访问超时异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    /// <param name="innerException">内部异常。</param>
    public SubtitleProviderTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 上游字幕源返回异常。
/// </summary>
internal sealed class SubtitleProviderException : SubtitleSourceException
{
    /// <summary>
    /// 初始化一个上游字幕源异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    public SubtitleProviderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化一个包含内部异常的上游字幕源异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    /// <param name="innerException">内部异常。</param>
    public SubtitleProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 字幕标识不存在或缓存已经过期。
/// </summary>
internal sealed class SubtitleNotFoundException : SubtitleSourceException
{
    /// <summary>
    /// 初始化一个字幕未找到异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    public SubtitleNotFoundException(string message)
        : base(message)
    {
    }
}
