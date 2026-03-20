using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 表示由当前媒体文件推导出的分段媒体组。
/// </summary>
public sealed class MultipartMediaGroup
{
    /// <summary>
    /// 获取或设置分段组的规范化基名。
    /// </summary>
    public string CanonicalBaseName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前 Jellyfin 条目实际对应的分段标识。
    /// </summary>
    public string CurrentPartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前分段组中的所有媒体分段。
    /// </summary>
    public List<MultipartMediaPart> Parts { get; set; } = [];
}

/// <summary>
/// 表示一个可独立搜索和落盘字幕的媒体分段。
/// </summary>
public sealed class MultipartMediaPart
{
    /// <summary>
    /// 获取或设置稳定的分段标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段在当前组内的顺序。
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 获取或设置面向前端显示的分段标签。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段类型，例如 cd、part、disc 或 single。
    /// </summary>
    public string PartKind { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段编号；单文件时为空。
    /// </summary>
    public int? PartNumber { get; set; }

    /// <summary>
    /// 获取或设置当前分段对应的媒体文件。
    /// </summary>
    public FileInfo MediaFile { get; set; } = null!;
}
