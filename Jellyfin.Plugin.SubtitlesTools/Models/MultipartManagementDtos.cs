using System.Collections.Generic;

namespace Jellyfin.Plugin.SubtitlesTools.Models;

/// <summary>
/// 表示媒体项的分段字幕管理首页响应。
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
    /// 获取或设置分段文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段类型。
    /// </summary>
    public string PartKind { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段编号；单文件时为空。
    /// </summary>
    public int? PartNumber { get; set; }

    /// <summary>
    /// 获取或设置该分段是否为当前 Jellyfin 条目主文件。
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// 获取或设置当前分段已存在的外部字幕文件。
    /// </summary>
    public List<ExistingSubtitleDto> ExistingSubtitles { get; set; } = [];

    /// <summary>
    /// 获取或设置当前用户在该分段上记住的字幕信息。
    /// </summary>
    public ManagedRememberedSubtitleDto RememberedSubtitle { get; set; } = new();
}

/// <summary>
/// 表示存在于媒体目录中的 sidecar 字幕文件。
/// </summary>
public sealed class ExistingSubtitleDto
{
    /// <summary>
    /// 获取或设置稳定标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置三字母语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置字幕格式。
    /// </summary>
    public string Format { get; set; } = "srt";

    /// <summary>
    /// 获取或设置当前用户是否已将该字幕记住为默认选项。
    /// </summary>
    public bool IsRemembered { get; set; }
}

/// <summary>
/// 表示当前用户在某个分段上记住的字幕摘要。
/// </summary>
public sealed class ManagedRememberedSubtitleDto
{
    /// <summary>
    /// 获取或设置状态。可用值为 none、active、missing。
    /// </summary>
    public string Status { get; set; } = "none";

    /// <summary>
    /// 获取或设置稳定标识。
    /// </summary>
    public string SubtitleId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字幕文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置三字母语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置字幕格式。
    /// </summary>
    public string Format { get; set; } = "srt";
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
    /// 获取或设置实际命中的搜索方式。
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
    /// 获取或设置将要写入的目标字幕文件名。
    /// </summary>
    public string TargetFileName { get; set; } = string.Empty;
}

/// <summary>
/// 表示手动下载某条候选字幕时的请求体。
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

    /// <summary>
    /// 获取或设置是否允许覆盖同语言现有字幕。
    /// </summary>
    public bool OverwriteExisting { get; set; }
}

/// <summary>
/// 表示单分段下载动作的结果。
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
    /// 获取或设置覆盖确认所需的冲突信息。
    /// </summary>
    public ManagedDownloadConflictDto? Conflict { get; set; }

    /// <summary>
    /// 获取或设置成功写入的字幕信息。
    /// </summary>
    public ManagedWrittenSubtitleDto? WrittenSubtitle { get; set; }
}

/// <summary>
/// 表示设置“记住字幕”时的请求体。
/// </summary>
public sealed class ManagedRememberedSubtitleRequestDto
{
    /// <summary>
    /// 获取或设置目标 sidecar 字幕文件名。
    /// </summary>
    public string SubtitleFileName { get; set; } = string.Empty;
}

/// <summary>
/// 表示设置或清除“记住字幕”后的结果。
/// </summary>
public sealed class ManagedRememberedSubtitleResponseDto
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
    /// 获取或设置当前分段更新后的记住字幕摘要。
    /// </summary>
    public ManagedRememberedSubtitleDto RememberedSubtitle { get; set; } = new();
}

/// <summary>
/// 表示播放时自动应用已记住字幕的查询结果。
/// </summary>
public sealed class RememberedSubtitleAutoApplyResponseDto
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
    /// 获取或设置当前播放键，用于前端避免同一次播放重复自动切换。
    /// </summary>
    public string PlaybackKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前会话标识。
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前播放的媒体项标识。
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前分段标识。
    /// </summary>
    public string PartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置已记住的字幕文件名。
    /// </summary>
    public string SubtitleFileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置目标字幕流索引。
    /// </summary>
    public int? TargetSubtitleStreamIndex { get; set; }

    /// <summary>
    /// 获取或设置当前会话已选择的字幕流索引。
    /// </summary>
    public int? CurrentSubtitleStreamIndex { get; set; }
}

/// <summary>
/// 表示一键最佳匹配下载的请求体。
/// </summary>
public sealed class ManagedDownloadBestRequestDto
{
    /// <summary>
    /// 获取或设置是否允许覆盖同语言现有字幕。
    /// </summary>
    public bool OverwriteExisting { get; set; }
}

/// <summary>
/// 表示一键最佳匹配下载的整体结果。
/// </summary>
public sealed class ManagedDownloadBestResponseDto
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

    /// <summary>
    /// 获取或设置需要用户确认的覆盖冲突集合。
    /// </summary>
    public List<ManagedDownloadConflictDto> Conflicts { get; set; } = [];
}

/// <summary>
/// 表示批量最佳匹配流程中单个分段的结果。
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
    /// 获取或设置成功写入的字幕信息。
    /// </summary>
    public ManagedWrittenSubtitleDto? WrittenSubtitle { get; set; }

    /// <summary>
    /// 获取或设置冲突信息。
    /// </summary>
    public ManagedDownloadConflictDto? Conflict { get; set; }
}

/// <summary>
/// 表示同语言 sidecar 字幕覆盖前的冲突描述。
/// </summary>
public sealed class ManagedDownloadConflictDto
{
    /// <summary>
    /// 获取或设置分段标识。
    /// </summary>
    public string PartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段标签。
    /// </summary>
    public string PartLabel { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置冲突语言。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置计划写入的目标文件名。
    /// </summary>
    public string TargetFileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前已存在、将被覆盖或替换的字幕文件列表。
    /// </summary>
    public List<string> ExistingFiles { get; set; } = [];
}

/// <summary>
/// 表示成功写入媒体目录的字幕文件信息。
/// </summary>
public sealed class ManagedWrittenSubtitleDto
{
    /// <summary>
    /// 获取或设置写入后的字幕文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置写入后的语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置写入后的字幕格式。
    /// </summary>
    public string Format { get; set; } = "srt";
}
