using System.Collections.Generic;

namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 表示媒体项的分段转换与内封字幕管理首页响应。
/// </summary>
public sealed class ManagedItemPartsResponseDto
{
    /// <summary>
    /// 获取或设置当前媒体项标识。
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置媒体项名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置媒体项类型。
    /// </summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前媒体项是否被识别为多分段媒体。
    /// </summary>
    public bool IsMultipart { get; set; }

    /// <summary>
    /// 获取或设置当前媒体项默认高亮的分段标识。
    /// </summary>
    public string CurrentPartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置媒体项下的所有可管理分段。
    /// </summary>
    public List<ManagedMediaPartDto> Parts { get; set; } = [];
}

/// <summary>
/// 表示单个可管理的媒体分段。
/// </summary>
public sealed class ManagedMediaPartDto
{
    /// <summary>
    /// 获取或设置分段标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段顺序。
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 获取或设置前端展示用的分段标签。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前实际媒体文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前实际媒体完整路径。
    /// </summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段类型。
    /// </summary>
    public string PartKind { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段编号；单文件时为空。
    /// </summary>
    public int? PartNumber { get; set; }

    /// <summary>
    /// 获取或设置该分段是否为当前 Jellyfin 条目的主文件。
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// 获取或设置当前容器格式。
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置是否已经保留原始哈希档案。
    /// </summary>
    public bool HasOriginalHash { get; set; }

    /// <summary>
    /// 获取或设置当前分段内封的字幕流列表。
    /// </summary>
    public List<ManagedEmbeddedSubtitleDto> EmbeddedSubtitles { get; set; } = [];
}

/// <summary>
/// 表示媒体容器中的一条字幕流。
/// </summary>
public sealed class ManagedEmbeddedSubtitleDto
{
    /// <summary>
    /// 获取或设置 FFprobe 返回的绝对流索引。
    /// </summary>
    public int StreamIndex { get; set; }

    /// <summary>
    /// 获取或设置该字幕在字幕流集合中的相对序号。
    /// </summary>
    public int SubtitleStreamIndex { get; set; }

    /// <summary>
    /// 获取或设置字幕轨标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置三字母语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置字幕编码格式。
    /// </summary>
    public string Format { get; set; } = "srt";

    /// <summary>
    /// 获取或设置是否为插件写入的字幕轨。
    /// </summary>
    public bool IsPluginManaged { get; set; }
}

/// <summary>
/// 表示某个分段的字幕搜索结果。
/// </summary>
public sealed class ManagedPartSearchResponseDto
{
    /// <summary>
    /// 获取或设置分段标识。
    /// </summary>
    public string PartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置命中的查询方式。
    /// </summary>
    public string MatchedBy { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果可信度。
    /// </summary>
    public string Confidence { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前分段的字幕候选。
    /// </summary>
    public List<ManagedSubtitleCandidateDto> Items { get; set; } = [];
}

/// <summary>
/// 表示前端展示用的字幕候选项。
/// </summary>
public sealed class ManagedSubtitleCandidateDto
{
    /// <summary>
    /// 获取或设置服务端字幕标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置原始字幕名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置拼接后的展示名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置原始扩展名。
    /// </summary>
    public string Ext { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置规范化后的字幕格式。
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置上游返回的语言列表。
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// 获取或设置已解析的三字母语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置字幕时长（毫秒）。
    /// </summary>
    public int DurationMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置上游来源编号。
    /// </summary>
    public int Source { get; set; }

    /// <summary>
    /// 获取或设置上游综合分数。
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 获取或设置指纹匹配分数。
    /// </summary>
    public double FingerprintScore { get; set; }

    /// <summary>
    /// 获取或设置额外说明。
    /// </summary>
    public string? ExtraName { get; set; }

    /// <summary>
    /// 获取或设置临时 SRT 文件名。
    /// </summary>
    public string TemporarySrtFileName { get; set; } = string.Empty;
}

/// <summary>
/// 表示下载并内封字幕时的请求体。
/// </summary>
public sealed class ManagedPartDownloadRequestDto
{
    /// <summary>
    /// 获取或设置服务端字幕标识。
    /// </summary>
    public string SubtitleId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置候选字幕名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置候选扩展名。
    /// </summary>
    public string Ext { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置候选语言列表。
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// 获取或设置已解析的三字母语言码。
    /// </summary>
    public string Language { get; set; } = string.Empty;
}

/// <summary>
/// 表示单分段下载并内封动作的结果。
/// </summary>
public sealed class ManagedPartDownloadResponseDto
{
    /// <summary>
    /// 获取或设置结果状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前媒体路径。
    /// </summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前容器格式。
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置成功写入的字幕流信息。
    /// </summary>
    public ManagedEmbeddedSubtitleDto? EmbeddedSubtitle { get; set; }
}

/// <summary>
/// 表示手动转换单个分段的结果。
/// </summary>
public sealed class ManagedPartConvertResponseDto
{
    /// <summary>
    /// 获取或设置结果状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置转换后的媒体路径。
    /// </summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置转换后的容器格式。
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置是否使用了自动转码回退。
    /// </summary>
    public bool UsedTranscodeFallback { get; set; }
}

/// <summary>
/// 表示删除内封字幕流时的请求体。
/// </summary>
public sealed class ManagedDeleteEmbeddedSubtitleRequestDto
{
    /// <summary>
    /// 获取或设置待删除的绝对流索引。
    /// </summary>
    public int StreamIndex { get; set; }
}

/// <summary>
/// 表示删除内封字幕流后的结果。
/// </summary>
public sealed class ManagedDeleteEmbeddedSubtitleResponseDto
{
    /// <summary>
    /// 获取或设置结果状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置被删除的绝对流索引。
    /// </summary>
    public int DeletedStreamIndex { get; set; }
}

/// <summary>
/// 表示一键整组内封字幕的请求体。
/// </summary>
public sealed class ManagedDownloadBestRequestDto
{
}

/// <summary>
/// 表示一键整组转换的请求体。
/// </summary>
public sealed class ManagedConvertGroupRequestDto
{
}

/// <summary>
/// 表示批量操作的整体结果。
/// </summary>
public sealed class ManagedBatchOperationResponseDto
{
    /// <summary>
    /// 获取或设置整体执行状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置整体执行消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置各分段的执行结果。
    /// </summary>
    public List<ManagedBatchPartResultDto> Items { get; set; } = [];
}

/// <summary>
/// 表示批量流程中单个分段的结果。
/// </summary>
public sealed class ManagedBatchPartResultDto
{
    /// <summary>
    /// 获取或设置分段标识。
    /// </summary>
    public string PartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段标签。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结果消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前媒体路径。
    /// </summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前容器格式。
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置成功写入的字幕流信息。
    /// </summary>
    public ManagedEmbeddedSubtitleDto? EmbeddedSubtitle { get; set; }
}
