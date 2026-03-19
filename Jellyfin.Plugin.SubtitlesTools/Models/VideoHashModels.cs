using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 视频哈希计算结果。
/// </summary>
public sealed class VideoHashResult
{
    /// <summary>
    /// 获取或设置文件完整路径。
    /// </summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置文件大小。
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 获取或设置最后写入时间 UTC Tick。
    /// </summary>
    public long LastWriteTimeUtcTicks { get; set; }

    /// <summary>
    /// 获取或设置 CID。
    /// </summary>
    public string Cid { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 GCID。
    /// </summary>
    public string Gcid { get; set; } = string.Empty;
}

/// <summary>
/// 落盘缓存结构，单独拆出模型便于序列化和测试。
/// </summary>
public sealed class VideoHashCacheEntry
{
    /// <summary>
    /// 获取或设置文件完整路径。
    /// </summary>
    [JsonPropertyName("media_path")]
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置文件大小。
    /// </summary>
    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    /// <summary>
    /// 获取或设置最后写入时间 UTC Tick。
    /// </summary>
    [JsonPropertyName("last_write_time_utc_ticks")]
    public long LastWriteTimeUtcTicks { get; set; }

    /// <summary>
    /// 获取或设置 CID。
    /// </summary>
    [JsonPropertyName("cid")]
    public string Cid { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 GCID。
    /// </summary>
    [JsonPropertyName("gcid")]
    public string Gcid { get; set; } = string.Empty;
}
