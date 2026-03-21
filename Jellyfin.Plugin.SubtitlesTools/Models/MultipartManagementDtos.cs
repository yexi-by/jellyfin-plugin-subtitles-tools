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
    public string Id { get; set; } = string.Empty;
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string PartKind { get; set; } = string.Empty;
    public int? PartNumber { get; set; }
    public bool IsCurrent { get; set; }
    public string Container { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public bool ReadIdentityFromMetadata { get; set; }
    public string RiskVerdict { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;
    public bool NeedsCompatibilityRepair { get; set; }
    public List<ManagedEmbeddedSubtitleDto> EmbeddedSubtitles { get; set; } = [];
}

/// <summary>
/// 表示媒体容器中的一条字幕流。
/// </summary>
public sealed class ManagedEmbeddedSubtitleDto
{
    public int StreamIndex { get; set; }
    public int SubtitleStreamIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Language { get; set; } = "und";
    public string Format { get; set; } = "srt";
    public bool IsPluginManaged { get; set; }
}

/// <summary>
/// 表示某个分段的字幕搜索结果。
/// </summary>
public sealed class ManagedPartSearchResponseDto
{
    public string PartId { get; set; } = string.Empty;
    public string MatchedBy { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public string RiskVerdict { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;
    public bool NeedsCompatibilityRepair { get; set; }
    public List<ManagedSubtitleCandidateDto> Items { get; set; } = [];
}

/// <summary>
/// 表示前端展示用的字幕候选项。
/// </summary>
public sealed class ManagedSubtitleCandidateDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Ext { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = [];
    public string Language { get; set; } = "und";
    public int DurationMilliseconds { get; set; }
    public int Source { get; set; }
    public int Score { get; set; }
    public double FingerprintScore { get; set; }
    public string? ExtraName { get; set; }
    public string TemporarySrtFileName { get; set; } = string.Empty;
}

public sealed class ManagedPartDownloadRequestDto
{
    public string SubtitleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ext { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = [];
    public string Language { get; set; } = string.Empty;
}

public sealed class ManagedPartDownloadResponseDto
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public string RiskVerdict { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;
    public bool NeedsCompatibilityRepair { get; set; }
    public bool UsedCompatibilityRepairReencode { get; set; }
    public ManagedEmbeddedSubtitleDto? EmbeddedSubtitle { get; set; }
}

public sealed class ManagedPartConvertResponseDto
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public string RiskVerdict { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;
    public bool NeedsCompatibilityRepair { get; set; }
    public bool UsedCompatibilityRepairReencode { get; set; }
}

public sealed class ManagedDeleteEmbeddedSubtitleRequestDto
{
    public int StreamIndex { get; set; }
}

public sealed class ManagedDeleteEmbeddedSubtitleResponseDto
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int DeletedStreamIndex { get; set; }
}

public sealed class ManagedDownloadBestRequestDto
{
}

public sealed class ManagedConvertGroupRequestDto
{
}

public sealed class ManagedBatchOperationResponseDto
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<ManagedBatchPartResultDto> Items { get; set; } = [];
}

public sealed class ManagedBatchPartResultDto
{
    public string PartId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public string RiskVerdict { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;
    public bool NeedsCompatibilityRepair { get; set; }
    public bool UsedCompatibilityRepairReencode { get; set; }
    public ManagedEmbeddedSubtitleDto? EmbeddedSubtitle { get; set; }
}
