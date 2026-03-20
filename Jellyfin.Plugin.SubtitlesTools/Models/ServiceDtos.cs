using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 健康检查响应。
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
    /// 获取或设置字幕源名称。
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕源可用状态。
    /// </summary>
    [JsonPropertyName("provider_available")]
    public bool ProviderAvailable { get; set; }
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

    /// <summary>
    /// 获取或设置服务端代理下载地址。
    /// </summary>
    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;
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
/// 字幕快照，用于在下载阶段恢复 Jellyfin 需要的元数据。
/// </summary>
/// <summary>
/// 健康检查结果模型。
/// </summary>
public sealed class ServiceHealthResult
{
    /// <summary>
    /// 获取或设置规范化后的基地址。
    /// </summary>
    public string ServiceBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置请求超时秒数。
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// 获取或设置健康检查明细。
    /// </summary>
    public HealthResponseDto Health { get; set; } = new();
}

/// <summary>
/// 服务端异常。
/// </summary>
internal sealed class SubtitlesToolsApiException : Exception
{
    /// <summary>
    /// 初始化一个服务端异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    public SubtitlesToolsApiException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化一个包含内部异常的服务端异常实例。
    /// </summary>
    /// <param name="message">异常信息。</param>
    /// <param name="innerException">内部异常。</param>
    public SubtitlesToolsApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
