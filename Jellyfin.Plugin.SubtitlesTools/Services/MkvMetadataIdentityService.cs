using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 统一负责读取、判断和写入 MKV 自定义元数据中的原始 CID/GCID。
/// 插件后续不再依赖外部归档文件，而是把“是否受管”和“原始身份”全部收敛到 MKV 文件自身。
/// </summary>
public sealed class MkvMetadataIdentityService
{
    public const string OriginalCidTag = "SUBTITLESTOOLS_ORIGINAL_CID";
    public const string OriginalGcidTag = "SUBTITLESTOOLS_ORIGINAL_GCID";
    public const string ProcessedTag = "SUBTITLESTOOLS_PROCESSED";
    public const string PipelineTag = "SUBTITLESTOOLS_PIPELINE";
    public const string PipelineValue = "mkv_srt_embed_v2";

    private readonly VideoHashCalculator _videoHashCalculator;
    private readonly FfmpegProcessService _ffmpegProcessService;
    private readonly VideoContainerConversionService _videoContainerConversionService;
    private readonly ILogger<MkvMetadataIdentityService> _logger;

    /// <summary>
    /// 初始化 MKV 元数据身份服务。
    /// </summary>
    public MkvMetadataIdentityService(
        VideoHashCalculator videoHashCalculator,
        FfmpegProcessService ffmpegProcessService,
        VideoContainerConversionService videoContainerConversionService,
        ILogger<MkvMetadataIdentityService> logger)
    {
        _videoHashCalculator = videoHashCalculator;
        _ffmpegProcessService = ffmpegProcessService;
        _videoContainerConversionService = videoContainerConversionService;
        _logger = logger;
    }

    /// <summary>
    /// 尝试从当前媒体文件中读取插件自定义元数据。
    /// 只有 MKV 且元数据完整时才返回身份；否则返回空，调用方再决定是否纳管。
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

        return new ManagedMediaIdentity
        {
            MediaPath = mediaFile.FullName,
            Container = "mkv",
            OriginalCid = originalCid,
            OriginalGcid = originalGcid,
            ReadFromMetadata = true
        };
    }

    /// <summary>
    /// 确保当前文件已经被插件纳管。
    /// 非 MKV 会先计算 CID/GCID，再转成 MKV 并写入元数据；
    /// 已是 MKV 但无元数据时，会直接对该 MKV 本体计算 CID/GCID 并补写元数据。
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
            throw new FileNotFoundException("媒体文件不存在，无法纳管。", mediaPath);
        }

        var existingIdentity = await TryGetIdentityAsync(sourceFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        if (existingIdentity is not null)
        {
            return new ManagedMediaIdentityResult
            {
                Identity = existingIdentity,
                Message = "当前 MKV 已纳管，直接读取 MKV 元数据中的原始 CID/GCID。",
                ConvertedToMkv = false,
                WroteMetadata = false,
                UsedTranscodeFallback = false
            };
        }

        var hashResult = await _videoHashCalculator.ComputeAsync(sourceFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        var metadata = BuildMetadata(hashResult.Cid, hashResult.Gcid);

        if (!string.Equals(sourceFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            var conversionResult = await _videoContainerConversionService
                .EnsureMkvAsync(sourceFile.FullName, cancellationToken, traceId, metadata)
                .ConfigureAwait(false);
            var managedIdentity = await RequireIdentityAsync(conversionResult.OutputPath, traceId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "trace={TraceId} mkv_metadata_managed_converted media_path={MediaPath} output_path={OutputPath}",
                traceId ?? "-",
                sourceFile.FullName,
                conversionResult.OutputPath);

            return new ManagedMediaIdentityResult
            {
                Identity = managedIdentity,
                Message = conversionResult.Message,
                ConvertedToMkv = true,
                WroteMetadata = true,
                UsedTranscodeFallback = conversionResult.UsedTranscodeFallback
            };
        }

        await _videoContainerConversionService
            .WriteMetadataAsync(sourceFile.FullName, metadata, cancellationToken, traceId)
            .ConfigureAwait(false);
        var updatedIdentity = await RequireIdentityAsync(sourceFile.FullName, traceId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "trace={TraceId} mkv_metadata_managed_existing media_path={MediaPath}",
            traceId ?? "-",
            sourceFile.FullName);

        return new ManagedMediaIdentityResult
        {
            Identity = updatedIdentity,
            Message = "当前 MKV 原先未纳管，已为其计算 CID/GCID 并写入 MKV 元数据。",
            ConvertedToMkv = false,
            WroteMetadata = true,
            UsedTranscodeFallback = false
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
            throw new InvalidOperationException("已完成 MKV 纳管流程，但未能从输出文件中读取到插件元数据。");
        }

        return identity;
    }

    private static Dictionary<string, string> BuildMetadata(string cid, string gcid)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OriginalCidTag] = cid,
            [OriginalGcidTag] = gcid,
            [ProcessedTag] = "1",
            [PipelineTag] = PipelineValue
        };
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
    public bool ReadFromMetadata { get; set; }
}

/// <summary>
/// 表示“确保文件已纳管”操作的结果。
/// </summary>
public sealed class ManagedMediaIdentityResult
{
    public ManagedMediaIdentity Identity { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public bool ConvertedToMkv { get; set; }
    public bool WroteMetadata { get; set; }
    public bool UsedTranscodeFallback { get; set; }
}

