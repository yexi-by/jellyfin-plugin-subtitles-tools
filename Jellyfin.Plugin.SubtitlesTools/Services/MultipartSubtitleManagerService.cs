using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责”分段字幕管理页”所需的媒体识别、处理、兼容修复、字幕搜索、SRT 归一化以及内封/外挂写入流程。
/// 当前版本只认 MKV 元数据，不再依赖任何插件侧哈希归档。
/// </summary>
public sealed class MultipartSubtitleManagerService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly MkvMetadataIdentityService _mkvMetadataIdentityService;
    private readonly SubtitleSourceService _subtitleSourceService;
    private readonly MultipartMediaParserService _multipartMediaParserService;
    private readonly SubtitleMetadataService _subtitleMetadataService;
    private readonly SubtitleSrtConversionService _subtitleSrtConversionService;
    private readonly EmbeddedSubtitleService _embeddedSubtitleService;
    private readonly ExternalSubtitleService _externalSubtitleService;
    private readonly ILogger<MultipartSubtitleManagerService> _logger;

    /// <summary>
    /// 初始化分段字幕管理服务。
    /// </summary>
    public MultipartSubtitleManagerService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        MkvMetadataIdentityService mkvMetadataIdentityService,
        SubtitleSourceService subtitleSourceService,
        MultipartMediaParserService multipartMediaParserService,
        SubtitleMetadataService subtitleMetadataService,
        SubtitleSrtConversionService subtitleSrtConversionService,
        EmbeddedSubtitleService embeddedSubtitleService,
        ExternalSubtitleService externalSubtitleService,
        ILogger<MultipartSubtitleManagerService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _mkvMetadataIdentityService = mkvMetadataIdentityService;
        _subtitleSourceService = subtitleSourceService;
        _multipartMediaParserService = multipartMediaParserService;
        _subtitleMetadataService = subtitleMetadataService;
        _subtitleSrtConversionService = subtitleSrtConversionService;
        _embeddedSubtitleService = embeddedSubtitleService;
        _externalSubtitleService = externalSubtitleService;
        _logger = logger;
    }

    /// <summary>
    /// 获取媒体项的分段结构、当前容器状态、兼容风险与已内封字幕流。
    /// </summary>
    public async Task<ManagedItemPartsResponseDto> GetItemPartsAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var parts = new List<ManagedMediaPartDto>(context.Group.Parts.Count);
        foreach (var part in context.Group.Parts)
        {
            parts.Add(await BuildManagedPartAsync(part, context.Group.CurrentPartId, cancellationToken).ConfigureAwait(false));
        }

        return new ManagedItemPartsResponseDto
        {
            ItemId = context.Item.Id.ToString("D"),
            Name = context.Item.Name,
            ItemType = context.Item.GetType().Name,
            IsMultipart = context.Group.Parts.Count > 1,
            CurrentPartId = context.Group.CurrentPartId,
            Parts = parts
        };
    }

    /// <summary>
    /// 为指定分段先确保”已处理且已兼容”，再调用内置字幕源搜索字幕。
    /// </summary>
    public async Task<ManagedPartSearchResponseDto> SearchPartAsync(Guid itemId, string partId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("分段标识不能为空。", nameof(partId));
        }

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var part = GetPart(context, partId);
        var traceId = CreateTraceId();
        var totalStopwatch = Stopwatch.StartNew();

        var management = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        if (management.ConvertedToMkv || management.WroteMetadata)
        {
            QueueItemRefresh(context.Item);
        }

        var managedFile = new FileInfo(management.Identity.MediaPath);
        var serviceResponse = await _subtitleSourceService.SearchAsync(
                new SubtitleSearchRequestDto
                {
                    Gcid = management.Identity.OriginalGcid,
                    Cid = management.Identity.OriginalCid,
                    Name = Path.GetFileNameWithoutExtension(managedFile.Name)
                },
                cancellationToken,
                traceId)
            .ConfigureAwait(false);

        var items = serviceResponse.Items.Select(item => BuildManagedCandidate(managedFile, item)).ToList();
        totalStopwatch.Stop();
        _logger.LogInformation(
            "trace={TraceId} multipart_search_complete item_id={ItemId} part_id={PartId} matched_by={MatchedBy} confidence={Confidence} items={ItemCount} total_ms={ElapsedMs:F2}",
            traceId,
            itemId,
            part.Id,
            serviceResponse.MatchedBy,
            serviceResponse.Confidence,
            items.Count,
            totalStopwatch.Elapsed.TotalMilliseconds);

        return new ManagedPartSearchResponseDto
        {
            PartId = part.Id,
            MatchedBy = serviceResponse.MatchedBy,
            Confidence = serviceResponse.Confidence,
            MediaPath = managedFile.FullName,
            Container = NormalizeContainer(managedFile.Extension),
            IsManaged = true,
            RiskVerdict = management.RiskVerdict,
            Pipeline = management.Pipeline,
            NeedsCompatibilityRepair = management.NeedsCompatibilityRepair,
            Items = items
        };
    }

    /// <summary>
    /// 手动把当前分段处理并修复到兼容安卓硬解的 MKV。
    /// </summary>
    public async Task<ManagedPartConvertResponseDto> ConvertPartAsync(Guid itemId, string partId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("分段标识不能为空。", nameof(partId));
        }

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var part = GetPart(context, partId);
        var traceId = CreateTraceId();
        var result = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        if (result.ConvertedToMkv || result.WroteMetadata)
        {
            QueueItemRefresh(context.Item);
        }

        return new ManagedPartConvertResponseDto
        {
            Status = "converted",
            Message = result.Message,
            MediaPath = result.Identity.MediaPath,
            Container = result.Identity.Container,
            IsManaged = true,
            RiskVerdict = result.RiskVerdict,
            Pipeline = result.Pipeline,
            NeedsCompatibilityRepair = result.NeedsCompatibilityRepair,
            UsedCompatibilityRepairReencode = result.UsedCompatibilityRepairReencode
        };
    }

    /// <summary>
    /// 一键把整组分段处理并修复到兼容安卓硬解的 MKV。
    /// </summary>
    public async Task<ManagedBatchOperationResponseDto> ConvertGroupAsync(
        Guid itemId,
        ManagedConvertGroupRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var traceId = CreateTraceId();
        var items = new List<ManagedBatchPartResultDto>(context.Group.Parts.Count);
        var queuedRefresh = false;

        foreach (var part in context.Group.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
                if (result.ConvertedToMkv || result.WroteMetadata)
                {
                    queuedRefresh = true;
                }

                items.Add(new ManagedBatchPartResultDto
                {
                    PartId = part.Id,
                    Label = part.Label,
                    Status = "converted",
                    Message = result.Message,
                    MediaPath = result.Identity.MediaPath,
                    Container = result.Identity.Container,
                    IsManaged = true,
                    RiskVerdict = result.RiskVerdict,
                    Pipeline = result.Pipeline,
                    NeedsCompatibilityRepair = result.NeedsCompatibilityRepair,
                    UsedCompatibilityRepairReencode = result.UsedCompatibilityRepairReencode
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FfmpegExecutionException)
            {
                _logger.LogWarning(ex, "trace={TraceId} multipart_convert_group_part_failed item_id={ItemId} part_id={PartId}", traceId, itemId, part.Id);
                items.Add(new ManagedBatchPartResultDto
                {
                    PartId = part.Id,
                    Label = part.Label,
                    Status = "failed",
                    Message = ex.Message,
                    MediaPath = part.MediaFile.FullName,
                    Container = NormalizeContainer(part.MediaFile.Extension),
                    IsManaged = false
                });
            }
        }

        if (queuedRefresh)
        {
            QueueItemRefresh(context.Item);
        }

        return BuildBatchResponse("整组转换完成。", items, "converted");
    }

    /// <summary>
    /// 下载指定候选字幕，转成临时 SRT 后按模式写入当前分段。
    /// 执行写入前会先确保当前分段已经完成处理与兼容修复。
    /// </summary>
    public async Task<ManagedPartDownloadResponseDto> DownloadPartAsync(
        Guid itemId,
        string partId,
        ManagedPartDownloadRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("分段标识不能为空。", nameof(partId));
        }

        if (string.IsNullOrWhiteSpace(request.SubtitleId))
        {
            throw new ArgumentException("字幕标识不能为空。", nameof(request));
        }

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var part = GetPart(context, partId);
        var traceId = CreateTraceId();
        var writeMode = ResolveRequestedWriteMode(request.WriteMode);
        var writeResult = await WriteCandidateAsync(part, request, writeMode, traceId, cancellationToken).ConfigureAwait(false);
        QueueItemRefresh(context.Item);

        return new ManagedPartDownloadResponseDto
        {
            Status = writeResult.Status,
            Message = writeResult.Message,
            WriteMode = writeMode,
            MediaPath = writeResult.MediaPath,
            Container = writeResult.Container,
            IsManaged = true,
            RiskVerdict = writeResult.RiskVerdict,
            Pipeline = writeResult.Pipeline,
            NeedsCompatibilityRepair = writeResult.NeedsCompatibilityRepair,
            UsedCompatibilityRepairReencode = writeResult.UsedCompatibilityRepairReencode,
            EmbeddedSubtitle = writeResult.EmbeddedSubtitle,
            ExternalSubtitle = writeResult.ExternalSubtitle
        };
    }

    /// <summary>
    /// 删除当前分段中的一条插件内封字幕流。
    /// 删除前会先确保当前分段已经处理并可被当前插件识别。
    /// </summary>
    public async Task<ManagedDeleteEmbeddedSubtitleResponseDto> DeleteEmbeddedSubtitleAsync(
        Guid itemId,
        string partId,
        ManagedDeleteEmbeddedSubtitleRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("分段标识不能为空。", nameof(partId));
        }

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var part = GetPart(context, partId);
        var management = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, CreateTraceId()).ConfigureAwait(false);
        await _embeddedSubtitleService
            .DeletePluginManagedSubtitleAsync(new FileInfo(management.Identity.MediaPath), request.StreamIndex, cancellationToken, CreateTraceId())
            .ConfigureAwait(false);
        QueueItemRefresh(context.Item);

        return new ManagedDeleteEmbeddedSubtitleResponseDto
        {
            Status = "deleted",
            Message = "内封字幕流已删除。",
            DeletedStreamIndex = request.StreamIndex
        };
    }

    /// <summary>
    /// 删除当前分段目录中的一条外挂字幕文件。
    /// 这里只删除当前媒体同目录、且当前列表中已识别到的 sidecar 字幕，避免误删其它文件。
    /// </summary>
    public async Task<ManagedDeleteExternalSubtitleResponseDto> DeleteExternalSubtitleAsync(
        Guid itemId,
        string partId,
        ManagedDeleteExternalSubtitleRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("分段标识不能为空。", nameof(partId));
        }

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new ArgumentException("外挂字幕路径不能为空。", nameof(request));
        }

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var part = GetPart(context, partId);
        await _externalSubtitleService
            .DeleteExternalSubtitleAsync(part.MediaFile, request.FilePath, cancellationToken)
            .ConfigureAwait(false);
        QueueItemRefresh(context.Item);

        return new ManagedDeleteExternalSubtitleResponseDto
        {
            Status = "deleted",
            Message = "外挂字幕已删除。",
            DeletedExternalSubtitlePath = request.FilePath
        };
    }

    /// <summary>
    /// 为所有分段分别搜索并写入第一名字幕候选。
    /// 每个分段都会先完成处理和兼容修复，再进入字幕搜索与字幕写入。
    /// </summary>
    public async Task<ManagedBatchOperationResponseDto> DownloadBestAsync(
        Guid itemId,
        ManagedDownloadBestRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await ResolveContextAsync(itemId, cancellationToken).ConfigureAwait(false);
        var traceId = CreateTraceId();
        var writeMode = ResolveRequestedWriteMode(request.WriteMode);
        var items = new List<ManagedBatchPartResultDto>(context.Group.Parts.Count);
        var queuedRefresh = false;

        foreach (var part in context.Group.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedMediaIdentityResult? management = null;
            try
            {
                management = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
                if (management.ConvertedToMkv || management.WroteMetadata)
                {
                    queuedRefresh = true;
                }

                var managedFile = new FileInfo(management.Identity.MediaPath);
                var searchResponse = await _subtitleSourceService.SearchAsync(
                        new SubtitleSearchRequestDto
                        {
                            Gcid = management.Identity.OriginalGcid,
                            Cid = management.Identity.OriginalCid,
                            Name = Path.GetFileNameWithoutExtension(managedFile.Name)
                        },
                        cancellationToken,
                        traceId)
                    .ConfigureAwait(false);

                var bestCandidate = searchResponse.Items.FirstOrDefault();
                if (bestCandidate is null)
                {
                    items.Add(new ManagedBatchPartResultDto
                    {
                        PartId = part.Id,
                        Label = part.Label,
                        Status = "no_candidates",
                        Message = "未找到可下载的字幕候选。",
                        MediaPath = managedFile.FullName,
                        Container = NormalizeContainer(managedFile.Extension),
                        IsManaged = true,
                        RiskVerdict = management.RiskVerdict,
                        Pipeline = management.Pipeline,
                        NeedsCompatibilityRepair = management.NeedsCompatibilityRepair,
                        UsedCompatibilityRepairReencode = management.UsedCompatibilityRepairReencode
                    });
                    continue;
                }

                var writeResult = await WriteCandidateAsync(
                    part,
                    new ManagedPartDownloadRequestDto
                    {
                        SubtitleId = bestCandidate.Id,
                        Name = bestCandidate.Name,
                        Ext = bestCandidate.Ext,
                        Languages = bestCandidate.Languages,
                        Language = _subtitleMetadataService.ResolveThreeLetterLanguage(bestCandidate.Languages),
                        WriteMode = writeMode
                    },
                    writeMode,
                    traceId,
                    cancellationToken).ConfigureAwait(false);
                queuedRefresh = true;

                items.Add(new ManagedBatchPartResultDto
                {
                    PartId = part.Id,
                    Label = part.Label,
                    Status = writeResult.Status,
                    Message = writeResult.Message,
                    WriteMode = writeMode,
                    MediaPath = writeResult.MediaPath,
                    Container = writeResult.Container,
                    IsManaged = true,
                    RiskVerdict = writeResult.RiskVerdict,
                    Pipeline = writeResult.Pipeline,
                    NeedsCompatibilityRepair = writeResult.NeedsCompatibilityRepair,
                    UsedCompatibilityRepairReencode = writeResult.UsedCompatibilityRepairReencode,
                    EmbeddedSubtitle = writeResult.EmbeddedSubtitle,
                    ExternalSubtitle = writeResult.ExternalSubtitle
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SubtitleSourceException or FfmpegExecutionException)
            {
                _logger.LogWarning(ex, "trace={TraceId} multipart_download_best_part_failed item_id={ItemId} part_id={PartId}", traceId, itemId, part.Id);
                items.Add(new ManagedBatchPartResultDto
                {
                    PartId = part.Id,
                    Label = part.Label,
                    Status = "failed",
                    Message = ex.Message,
                    MediaPath = management?.Identity.MediaPath ?? part.MediaFile.FullName,
                    Container = management?.Identity.Container ?? NormalizeContainer(part.MediaFile.Extension),
                    IsManaged = management is not null,
                    RiskVerdict = management?.RiskVerdict ?? string.Empty,
                    Pipeline = management?.Pipeline ?? string.Empty,
                    NeedsCompatibilityRepair = management?.NeedsCompatibilityRepair ?? false,
                    UsedCompatibilityRepairReencode = management?.UsedCompatibilityRepairReencode ?? false
                });
            }
        }

        if (queuedRefresh)
        {
            QueueItemRefresh(context.Item);
        }

        return BuildBatchResponse(
            string.Equals(writeMode, SubtitleWriteMode.Sidecar, StringComparison.Ordinal)
                ? "一键全段最佳匹配外挂写入完成。"
                : "一键全段最佳匹配内封完成。",
            items,
            writeMode);
    }

    private ManagedSubtitleCandidateDto BuildManagedCandidate(FileInfo mediaFile, SubtitleSearchItemDto item)
    {
        var language = _subtitleMetadataService.ResolveThreeLetterLanguage(item.Languages);
        return new ManagedSubtitleCandidateDto
        {
            Id = item.Id,
            Name = item.Name,
            DisplayName = _subtitleMetadataService.BuildDisplayName(item),
            Ext = item.Ext,
            Format = _subtitleMetadataService.NormalizeFormat(item.Ext),
            Languages = item.Languages,
            Language = language,
            DurationMilliseconds = item.DurationMilliseconds,
            Source = item.Source,
            Score = item.Score,
            FingerprintScore = item.FingerprintScore,
            ExtraName = item.ExtraName,
            TemporarySrtFileName = $"{Path.GetFileNameWithoutExtension(mediaFile.Name)}.srt",
            SidecarFileName = _externalSubtitleService.BuildSuggestedSidecarFileName(mediaFile, language, "srt")
        };
    }

    private async Task<ManagedMediaPartDto> BuildManagedPartAsync(MultipartMediaPart part, string currentPartId, CancellationToken cancellationToken)
    {
        var inspection = await _mkvMetadataIdentityService.InspectAsync(part.MediaFile.FullName, cancellationToken).ConfigureAwait(false);
        var embeddedSubtitles = await _embeddedSubtitleService.GetEmbeddedSubtitlesAsync(part.MediaFile, cancellationToken).ConfigureAwait(false);
        var externalSubtitles = _externalSubtitleService.GetExternalSubtitles(part.MediaFile);
        return new ManagedMediaPartDto
        {
            Id = part.Id,
            Index = part.Index,
            Label = part.Label,
            FileName = part.MediaFile.Name,
            MediaPath = part.MediaFile.FullName,
            PartKind = part.PartKind,
            PartNumber = part.PartNumber,
            IsCurrent = string.Equals(part.Id, currentPartId, StringComparison.Ordinal),
            Container = inspection.Container,
            IsManaged = inspection.Identity is not null,
            ReadIdentityFromMetadata = inspection.Identity?.ReadFromMetadata ?? false,
            RiskVerdict = inspection.RiskVerdict,
            Pipeline = inspection.Pipeline,
            NeedsCompatibilityRepair = inspection.NeedsCompatibilityRepair,
            EmbeddedSubtitles = embeddedSubtitles,
            ExternalSubtitles = externalSubtitles
        };
    }

    private async Task<WriteCandidateResult> WriteCandidateAsync(
        MultipartMediaPart part,
        ManagedPartDownloadRequestDto request,
        string writeMode,
        string traceId,
        CancellationToken cancellationToken)
    {
        return string.Equals(writeMode, SubtitleWriteMode.Sidecar, StringComparison.Ordinal)
            ? await WriteSidecarCandidateAsync(part, request, traceId, cancellationToken).ConfigureAwait(false)
            : await EmbedCandidateAsync(part, request, traceId, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "这里使用的媒体路径来自 EnsureManagedAsync 返回的 Jellyfin 本地媒体文件，已经完成存在性与可管理性校验，不直接信任请求体中的路径。")]
    private async Task<WriteCandidateResult> EmbedCandidateAsync(
        MultipartMediaPart part,
        ManagedPartDownloadRequestDto request,
        string traceId,
        CancellationToken cancellationToken)
    {
        var management = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        var managedFile = new FileInfo(management.Identity.MediaPath);
        var downloadedSubtitle = await _subtitleSourceService.DownloadSubtitleAsync(request.SubtitleId, cancellationToken, traceId).ConfigureAwait(false);

        FileInfo? temporarySrtFile = null;
        try
        {
            temporarySrtFile = await _subtitleSrtConversionService
                .ConvertToTemporarySrtAsync(managedFile, downloadedSubtitle, request.Ext, cancellationToken, traceId)
                .ConfigureAwait(false);

            var embeddedSubtitle = await _embeddedSubtitleService
                .ReplacePluginManagedSubtitleAsync(
                    managedFile,
                    temporarySrtFile,
                    request.Name,
                    ResolveRequestLanguage(request),
                    cancellationToken,
                    traceId)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} multipart_embed_complete part_id={PartId} subtitle_id={SubtitleId} media_path={MediaPath} stream_index={StreamIndex}",
                traceId,
                part.Id,
                request.SubtitleId,
                managedFile.FullName,
                embeddedSubtitle.StreamIndex);

            return new WriteCandidateResult
            {
                Status = SubtitleWriteMode.Embedded,
                Message = "字幕已转为 UTF-8 SRT 并内封到视频。",
                MediaPath = managedFile.FullName,
                Container = "mkv",
                RiskVerdict = management.RiskVerdict,
                Pipeline = management.Pipeline,
                NeedsCompatibilityRepair = management.NeedsCompatibilityRepair,
                UsedCompatibilityRepairReencode = management.UsedCompatibilityRepairReencode,
                EmbeddedSubtitle = embeddedSubtitle
            };
        }
        finally
        {
            if (temporarySrtFile is not null && temporarySrtFile.Exists)
            {
                temporarySrtFile.Delete();
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "这里使用的媒体路径来自 EnsureManagedAsync 返回的 Jellyfin 本地媒体文件，已经完成存在性与可管理性校验，不直接信任请求体中的路径。")]
    private async Task<WriteCandidateResult> WriteSidecarCandidateAsync(
        MultipartMediaPart part,
        ManagedPartDownloadRequestDto request,
        string traceId,
        CancellationToken cancellationToken)
    {
        var management = await _mkvMetadataIdentityService.EnsureManagedAsync(part.MediaFile.FullName, cancellationToken, traceId).ConfigureAwait(false);
        var managedFile = new FileInfo(management.Identity.MediaPath);
        var downloadedSubtitle = await _subtitleSourceService.DownloadSubtitleAsync(request.SubtitleId, cancellationToken, traceId).ConfigureAwait(false);

        FileInfo? temporarySrtFile = null;
        try
        {
            temporarySrtFile = await _subtitleSrtConversionService
                .ConvertToTemporarySrtAsync(managedFile, downloadedSubtitle, request.Ext, cancellationToken, traceId)
                .ConfigureAwait(false);

            var externalSubtitle = await _externalSubtitleService
                .ReplaceExternalSubtitleAsync(
                    managedFile,
                    temporarySrtFile,
                    ResolveRequestLanguage(request),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} multipart_sidecar_complete part_id={PartId} subtitle_id={SubtitleId} media_path={MediaPath} subtitle_path={SubtitlePath}",
                traceId,
                part.Id,
                request.SubtitleId,
                managedFile.FullName,
                externalSubtitle.FilePath);

            temporarySrtFile = null;

            return new WriteCandidateResult
            {
                Status = SubtitleWriteMode.Sidecar,
                Message = "字幕已转为 UTF-8 SRT 并写成外挂字幕。",
                MediaPath = managedFile.FullName,
                Container = NormalizeContainer(managedFile.Extension),
                RiskVerdict = management.RiskVerdict,
                Pipeline = management.Pipeline,
                NeedsCompatibilityRepair = management.NeedsCompatibilityRepair,
                UsedCompatibilityRepairReencode = management.UsedCompatibilityRepairReencode,
                ExternalSubtitle = externalSubtitle
            };
        }
        finally
        {
            if (temporarySrtFile is not null && temporarySrtFile.Exists)
            {
                temporarySrtFile.Delete();
            }
        }
    }

    private static ManagedBatchOperationResponseDto BuildBatchResponse(string successMessage, List<ManagedBatchPartResultDto> items, string successStatus)
    {
        var successCount = items.Count(item => string.Equals(item.Status, successStatus, StringComparison.OrdinalIgnoreCase));
        var failedCount = items.Count - successCount;
        return new ManagedBatchOperationResponseDto
        {
            Status = failedCount > 0 ? "partial" : "completed",
            Message = failedCount > 0
                ? $"{successMessage} 成功 {successCount} 个分段，失败或跳过 {failedCount} 个分段。"
                : $"{successMessage} 共处理 {successCount} 个分段。",
            Items = items
        };
    }

    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "媒体路径不直接来自外部输入，而是来自 Jellyfin 已入库项；这里只允许 Movie/Episode、本地路径且文件必须存在。")]
    private async Task<ManagedItemContext> ResolveContextAsync(Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = _libraryManager.GetItemById(itemId) as BaseItem;
        if (item is null)
        {
            throw new FileNotFoundException("未找到对应的 Jellyfin 媒体项。");
        }

        if (item is not Movie && item is not Episode)
        {
            throw new InvalidOperationException("当前仅支持 Movie 和 Episode。");
        }

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            throw new InvalidOperationException("当前媒体项没有可直接读取的本地媒体路径。");
        }

        if (string.Equals(Path.GetExtension(item.Path), ".strm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前版本不支持 .strm 媒体。");
        }

        var effectiveMediaPath = await ResolveEffectiveMediaPathAsync(item.Path, cancellationToken).ConfigureAwait(false);
        var mediaFile = new FileInfo(effectiveMediaPath);
        if (!mediaFile.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在或 Jellyfin 无法读取该路径。", effectiveMediaPath);
        }

        var group = _multipartMediaParserService.Parse(mediaFile.FullName);
        return new ManagedItemContext
        {
            Item = item,
            Group = group
        };
    }

    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "这里读取的路径不是用户任意输入，而是 Jellyfin 已入库项路径；若原路径已被插件转成 MKV，则只尝试同目录同名 .mkv 兜底。")]
    private async Task<string> ResolveEffectiveMediaPathAsync(string itemPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = Path.GetFullPath(itemPath);
        if (File.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        var fallbackMkvPath = Path.Combine(
            Path.GetDirectoryName(normalizedPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(normalizedPath)}.mkv");
        if (File.Exists(fallbackMkvPath))
        {
            return fallbackMkvPath;
        }

        throw new FileNotFoundException("媒体文件不存在或已被移动，且未能推断出转换后的 MKV 路径。", normalizedPath);
    }

    private static MultipartMediaPart GetPart(ManagedItemContext context, string partId)
    {
        var part = context.Group.Parts.FirstOrDefault(item => string.Equals(item.Id, partId, StringComparison.Ordinal));
        if (part is null)
        {
            throw new FileNotFoundException("未找到对应的分段。");
        }

        return part;
    }

    private void QueueItemRefresh(BaseItem item)
    {
        var refreshOptions = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.None,
            ImageRefreshMode = MetadataRefreshMode.None,
            ReplaceAllImages = false,
            ReplaceAllMetadata = false,
            ForceSave = false,
            IsAutomated = false,
            RemoveOldMetadata = false,
            RegenerateTrickplay = false
        };

        _providerManager.QueueRefresh(item.Id, refreshOptions, RefreshPriority.High);
    }

    private static string ResolveRequestLanguage(ManagedPartDownloadRequestDto request)
    {
        return string.IsNullOrWhiteSpace(request.Language)
            ? "und"
            : request.Language.Trim().ToLowerInvariant();
    }

    private static string NormalizeContainer(string extension)
    {
        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private static string ResolveRequestedWriteMode(string? requestedWriteMode)
    {
        return SubtitleWriteMode.Normalize(requestedWriteMode ?? Plugin.Instance?.Configuration?.DefaultSubtitleWriteMode);
    }

    /// <summary>
    /// 表示当前管理流程中的媒体上下文。
    /// </summary>
    private sealed class ManagedItemContext
    {
        public BaseItem Item { get; set; } = null!;
        public MultipartMediaGroup Group { get; set; } = null!;
    }

    /// <summary>
    /// 表示单次字幕内封结果。
    /// </summary>
    private sealed class WriteCandidateResult
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string MediaPath { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public string RiskVerdict { get; set; } = string.Empty;
        public string Pipeline { get; set; } = string.Empty;
        public bool NeedsCompatibilityRepair { get; set; }
        public bool UsedCompatibilityRepairReencode { get; set; }
        public ManagedEmbeddedSubtitleDto? EmbeddedSubtitle { get; set; }
        public ManagedExternalSubtitleDto? ExternalSubtitle { get; set; }
    }
}
