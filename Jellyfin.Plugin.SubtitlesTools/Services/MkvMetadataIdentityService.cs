using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 统一负责读取、判断和写入 MKV 自定义元数据中的原始文件指纹。
/// 当前版本不仅要确保”已处理”，还要确保当前视频已经修复到适合安卓硬解的兼容形态。
/// </summary>
public sealed class MkvMetadataIdentityService
{
    public const string OriginalCidTag = "SUBTITLESTOOLS_ORIGINAL_CID";
    public const string OriginalGcidTag = "SUBTITLESTOOLS_ORIGINAL_GCID";
    public const string ProcessedTag = "SUBTITLESTOOLS_PROCESSED";
    public const string PipelineTag = "SUBTITLESTOOLS_PIPELINE";
    public const string MetadataPipelineValue = "mkv_metadata_v1";
    public const string RemuxPipelineValue = "mkv_remux_v1";
    public const string QsvRepairPipelineValue = "mkv_h264_aac_qsv_v1";

    private readonly VideoHashCalculator _videoHashCalculator;
    private readonly FfmpegProcessService _ffmpegProcessService;
    private readonly VideoContainerConversionService _videoContainerConversionService;
    private readonly AndroidHwdecodeRiskService _androidHwdecodeRiskService;
    private readonly ILogger<MkvMetadataIdentityService> _logger;

    /// <summary>
    /// 初始化 MKV 元数据身份服务。
    /// </summary>
    public MkvMetadataIdentityService(
        VideoHashCalculator videoHashCalculator,
        FfmpegProcessService ffmpegProcessService,
        VideoContainerConversionService videoContainerConversionService,
        AndroidHwdecodeRiskService androidHwdecodeRiskService,
        ILogger<MkvMetadataIdentityService> logger)
    {
        _videoHashCalculator = videoHashCalculator;
        _ffmpegProcessService = ffmpegProcessService;
        _videoContainerConversionService = videoContainerConversionService;
        _androidHwdecodeRiskService = androidHwdecodeRiskService;
        _logger = logger;
    }

    /// <summary>
    /// 尝试从当前媒体文件读取插件元数据。
    /// 只有 MKV 且原始文件指纹完整时，才视为已经处理。
    /// </summary>
    public async Task<ManagedMediaIdentity?> TryGetIdentityAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var mediaFile = new FileInfo(mediaPath);
        if (!mediaFile.Exists || !string.Equals(mediaFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tags = await _ffmpegProcessService
            .ProbeContainerTagsAsync(mediaFile.FullName, cancellationToken, traceId)
            .ConfigureAwait(false);

        if (!tags.TryGetValue(OriginalCidTag, out var originalCid)
            || !tags.TryGetValue(OriginalGcidTag, out var originalGcid)
            || string.IsNullOrWhiteSpace(originalCid)
            || string.IsNullOrWhiteSpace(originalGcid))
        {
            return null;
        }

        tags.TryGetValue(PipelineTag, out var pipeline);
        return new ManagedMediaIdentity
        {
            MediaPath = mediaFile.FullName,
            Container = "mkv",
            OriginalCid = originalCid,
            OriginalGcid = originalGcid,
            Pipeline = pipeline?.Trim() ?? string.Empty,
            ReadFromMetadata = true
        };
    }

    /// <summary>
    /// 读取当前媒体的受管状态、视频处理流水线和安卓硬解风险。
    /// 详情页展示当前分段时，会直接读取这个摘要，而不会主动修改文件。
    /// </summary>
    public async Task<ManagedMediaInspectionResult> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var mediaFile = new FileInfo(mediaPath);
        if (!mediaFile.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在，无法读取状态。", mediaPath);
        }

        var identity = await TryGetIdentityAsync(mediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        var assessment = await _androidHwdecodeRiskService.AssessAsync(mediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);

        return new ManagedMediaInspectionResult
        {
            MediaPath = mediaFile.FullName,
            Container = NormalizeContainer(mediaFile.Extension),
            Identity = identity,
            Pipeline = identity?.Pipeline ?? string.Empty,
            RiskVerdict = assessment.Verdict,
            NeedsCompatibilityRepair = assessment.NeedsCompatibilityRepair
        };
    }

    /// <summary>
    /// 确保当前文件已经完成”处理 + 安卓硬解兼容修复”。
    /// 命中高风险时会优先执行 Intel QSV 重编码；其余场景只做 remux 或补写元数据。
    /// </summary>
    public async Task<ManagedMediaIdentityResult> EnsureManagedAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var sourceFile = new FileInfo(mediaPath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在，无法处理。", mediaPath);
        }

        var existingIdentity = await TryGetIdentityAsync(sourceFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        var currentAssessment = await _androidHwdecodeRiskService.AssessAsync(sourceFile.FullName, cancellationToken, traceId).ConfigureAwait(false);

        if (existingIdentity is not null && !currentAssessment.NeedsCompatibilityRepair)
        {
            return new ManagedMediaIdentityResult
            {
                Identity = existingIdentity,
                Message = "当前文件已处理且兼容性正常，无需重复处理。",
                ConvertedToMkv = false,
                WroteMetadata = false,
                UsedCompatibilityRepairReencode = false,
                RiskVerdict = currentAssessment.Verdict,
                Pipeline = existingIdentity.Pipeline,
                NeedsCompatibilityRepair = currentAssessment.NeedsCompatibilityRepair
            };
        }

        if (existingIdentity is not null && currentAssessment.NeedsCompatibilityRepair)
        {
            var qsvMetadata = BuildMetadata(existingIdentity.OriginalCid, existingIdentity.OriginalGcid, QsvRepairPipelineValue);
            var qsvResult = await _videoContainerConversionService
                .EnsureCompatibilityAsync(
                    sourceFile.FullName,
                    GetNormalizedQsvRenderDevicePath(),
                    cancellationToken,
                    traceId,
                    qsvMetadata)
                .ConfigureAwait(false);

            var repairedResult = await BuildManagedResultAsync(
                    qsvResult.OutputPath,
                    "当前文件虽然已处理，但仍存在兼容性问题，已使用 Intel QSV 重编码修复。",
                    convertedToMkv: true,
                    wroteMetadata: true,
                    usedCompatibilityRepairReencode: qsvResult.UsedCompatibilityRepairReencode,
                    traceId,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} mkv_metadata_repair_complete media_path={MediaPath} output_path={OutputPath}",
                traceId ?? "-",
                sourceFile.FullName,
                repairedResult.Identity.MediaPath);
            return repairedResult;
        }

        var hashResult = await _videoHashCalculator.ComputeAsync(sourceFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        if (currentAssessment.NeedsCompatibilityRepair)
        {
            var qsvMetadata = BuildMetadata(hashResult.Cid, hashResult.Gcid, QsvRepairPipelineValue);
            var qsvResult = await _videoContainerConversionService
                .EnsureCompatibilityAsync(
                    sourceFile.FullName,
                    GetNormalizedQsvRenderDevicePath(),
                    cancellationToken,
                    traceId,
                    qsvMetadata)
                .ConfigureAwait(false);

            var repairedResult = await BuildManagedResultAsync(
                    qsvResult.OutputPath,
                    "当前文件存在兼容性问题，已先计算文件指纹并通过 Intel QSV 重编码为兼容 MKV。",
                    convertedToMkv: true,
                    wroteMetadata: true,
                    usedCompatibilityRepairReencode: qsvResult.UsedCompatibilityRepairReencode,
                    traceId,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} mkv_metadata_repair_new_complete media_path={MediaPath} output_path={OutputPath}",
                traceId ?? "-",
                sourceFile.FullName,
                repairedResult.Identity.MediaPath);
            return repairedResult;
        }

        if (!string.Equals(sourceFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            var metadata = BuildMetadata(hashResult.Cid, hashResult.Gcid, RemuxPipelineValue);
            var remuxResult = await _videoContainerConversionService
                .EnsureMkvAsync(sourceFile.FullName, cancellationToken, traceId, metadata)
                .ConfigureAwait(false);

            var managedResult = await BuildManagedResultAsync(
                    remuxResult.OutputPath,
                    remuxResult.Message,
                    convertedToMkv: true,
                    wroteMetadata: true,
                    usedCompatibilityRepairReencode: false,
                    traceId,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} mkv_metadata_managed_converted media_path={MediaPath} output_path={OutputPath}",
                traceId ?? "-",
                sourceFile.FullName,
                managedResult.Identity.MediaPath);
            return managedResult;
        }

        await _videoContainerConversionService
            .WriteMetadataAsync(
                sourceFile.FullName,
                BuildMetadata(hashResult.Cid, hashResult.Gcid, MetadataPipelineValue),
                cancellationToken,
                traceId)
            .ConfigureAwait(false);

        var metadataOnlyResult = await BuildManagedResultAsync(
                sourceFile.FullName,
                "当前 MKV 原先未处理，已为其计算文件指纹并写入 MKV 元数据。",
                convertedToMkv: false,
                wroteMetadata: true,
                usedCompatibilityRepairReencode: false,
                traceId,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "trace={TraceId} mkv_metadata_managed_existing media_path={MediaPath}",
            traceId ?? "-",
            sourceFile.FullName);
        return metadataOnlyResult;
    }

    private async Task<ManagedMediaIdentityResult> BuildManagedResultAsync(
        string mediaPath,
        string message,
        bool convertedToMkv,
        bool wroteMetadata,
        bool usedCompatibilityRepairReencode,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var identity = await RequireIdentityAsync(mediaPath, traceId, cancellationToken).ConfigureAwait(false);
        var assessment = await _androidHwdecodeRiskService.AssessAsync(mediaPath, cancellationToken, traceId).ConfigureAwait(false);

        return new ManagedMediaIdentityResult
        {
            Identity = identity,
            Message = message,
            ConvertedToMkv = convertedToMkv,
            WroteMetadata = wroteMetadata,
            UsedCompatibilityRepairReencode = usedCompatibilityRepairReencode,
            RiskVerdict = assessment.Verdict,
            Pipeline = identity.Pipeline,
            NeedsCompatibilityRepair = assessment.NeedsCompatibilityRepair
        };
    }

    private async Task<ManagedMediaIdentity> RequireIdentityAsync(
        string mediaPath,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var identity = await TryGetIdentityAsync(mediaPath, cancellationToken, traceId).ConfigureAwait(false);
        if (identity is null)
        {
            throw new InvalidOperationException("已完成 MKV 处理流程，但未能从输出文件中读取到插件元数据。");
        }

        return identity;
    }

    private static Dictionary<string, string> BuildMetadata(string cid, string gcid, string pipeline)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OriginalCidTag] = cid,
            [OriginalGcidTag] = gcid,
            [ProcessedTag] = "1",
            [PipelineTag] = pipeline
        };
    }

    private static string GetNormalizedQsvRenderDevicePath()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return PluginConfiguration.NormalizeQsvRenderDevicePath(configuration.QsvRenderDevicePath);
    }

    private static string NormalizeContainer(string extension)
    {
        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }
}

/// <summary>
/// 表示当前媒体文件中可直接使用的原始身份信息。
/// </summary>
public sealed class ManagedMediaIdentity
{
    public string MediaPath { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string OriginalCid { get; set; } = string.Empty;
    public string OriginalGcid { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;
    public bool ReadFromMetadata { get; set; }
}

/// <summary>
/// 表示“仅读取状态，不修改文件”的检查结果。
/// </summary>
public sealed class ManagedMediaInspectionResult
{
    public string MediaPath { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public ManagedMediaIdentity? Identity { get; set; }
    public string Pipeline { get; set; } = string.Empty;
    public string RiskVerdict { get; set; } = AndroidHwdecodeRiskService.LowRiskVerdict;
    public bool NeedsCompatibilityRepair { get; set; }
}

/// <summary>
/// 表示”确保文件已处理且已兼容”操作的结果。
/// </summary>
public sealed class ManagedMediaIdentityResult
{
    public ManagedMediaIdentity Identity { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public bool ConvertedToMkv { get; set; }
    public bool WroteMetadata { get; set; }
    public bool UsedCompatibilityRepairReencode { get; set; }
    public string RiskVerdict { get; set; } = AndroidHwdecodeRiskService.LowRiskVerdict;
    public string Pipeline { get; set; } = string.Empty;
    public bool NeedsCompatibilityRepair { get; set; }
}
