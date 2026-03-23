namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 统一定义字幕写入模式。
/// </summary>
public static class SubtitleWriteMode
{
    /// <summary>
    /// 表示把字幕写入 MKV 容器。
    /// </summary>
    public const string Embedded = "embedded";

    /// <summary>
    /// 表示把字幕写成同目录外挂文件。
    /// </summary>
    public const string Sidecar = "sidecar";

    /// <summary>
    /// 规范化字幕写入模式；未知值回落到内封。
    /// </summary>
    /// <param name="value">原始写入模式。</param>
    /// <returns>规范化后的写入模式。</returns>
    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            Sidecar => Sidecar,
            _ => Embedded
        };
    }
}
