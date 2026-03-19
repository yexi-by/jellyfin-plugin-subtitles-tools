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

/// <summary>
/// 描述一次视频哈希解析过程的缓存命中情况与各阶段耗时。
/// </summary>
public sealed class VideoHashResolutionMetrics
{
    /// <summary>
    /// 获取或设置最终得到的视频哈希结果。
    /// </summary>
    public VideoHashResult HashResult { get; set; } = new();

    /// <summary>
    /// 获取或设置是否直接命中了现有缓存。
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// 获取或设置缓存读取耗时，单位为毫秒。
    /// </summary>
    public double CacheLookupMs { get; set; }

    /// <summary>
    /// 获取或设置实际计算耗时，单位为毫秒。
    /// </summary>
    public double ComputeMs { get; set; }

    /// <summary>
    /// 获取或设置缓存写回耗时，单位为毫秒。
    /// </summary>
    public double CacheSaveMs { get; set; }
}
