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
/// 负责“分段字幕管理页”所需的媒体识别、搜索、下载、覆盖确认和元数据刷新流程。
/// </summary>
public sealed class MultipartSubtitleManagerService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly VideoHashResolverService _videoHashResolverService;
    private readonly SubtitlesToolsApiClient _subtitlesToolsApiClient;
    private readonly MultipartMediaParserService _multipartMediaParserService;
    private readonly SidecarSubtitleService _sidecarSubtitleService;
    private readonly SubtitleMetadataService _subtitleMetadataService;
    private readonly ILogger<MultipartSubtitleManagerService> _logger;

    /// <summary>
    /// 初始化分段字幕管理服务。
    /// </summary>
    public MultipartSubtitleManagerService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        VideoHashResolverService videoHashResolverService,
        SubtitlesToolsApiClient subtitlesToolsApiClient,
        MultipartMediaParserService multipartMediaParserService,
        SidecarSubtitleService sidecarSubtitleService,
        SubtitleMetadataService subtitleMetadataService,
        ILogger<MultipartSubtitleManagerService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _videoHashResolverService = videoHashResolverService;
        _subtitlesToolsApiClient = subtitlesToolsApiClient;
        _multipartMediaParserService = multipartMediaParserService;
        _sidecarSubtitleService = sidecarSubtitleService;
        _subtitleMetadataService = subtitleMetadataService;
        _logger = logger;
    }

    /// <summary>
    /// 获取媒体项的分段结构和现有字幕状态。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分段首页响应。</returns>
    public Task<ManagedItemPartsResponseDto> GetItemPartsAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var context = ResolveContext(itemId, cancellationToken);
        return Task.FromResult(BuildItemPartsResponse(context));
    }

    /// <summary>
    /// 为指定分段计算哈希并向 Python 服务端搜索字幕。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="partId">分段标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分段搜索结果。</returns>
    public async Task<ManagedPartSearchResponseDto> SearchPartAsync(
        Guid itemId,
        string partId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("分段标识不能为空。", nameof(partId));
        }

        var context = ResolveContext(itemId, cancellationToken);
        var part = GetPart(context, partId);
        var traceId = CreateTraceId();
        var totalStopwatch = Stopwatch.StartNew();

        var hashMetrics = await _videoHashResolverService
            .ResolveAsync(part.MediaFile.FullName, cancellationToken, traceId)
            .ConfigureAwait(false);

        var serviceResponse = await _subtitlesToolsApiClient.SearchAsync(
            new SubtitleSearchRequestDto
            {
                Gcid = hashMetrics.HashResult.Gcid,
                Cid = hashMetrics.HashResult.Cid,
                Name = part.MediaFile.Name
            },
            cancellationToken,
            traceId).ConfigureAwait(false);

        var items = serviceResponse.Items
            .Select(item => BuildManagedCandidate(part, item))
            .ToList();

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
            Items = items
        };
    }

    /// <summary>
    /// 将指定候选字幕下载并写入某个分段旁边。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="partId">分段标识。</param>
    /// <param name="request">下载请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载结果。</returns>
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

        var context = ResolveContext(itemId, cancellationToken);
        var part = GetPart(context, partId);
        var language = ResolveRequestLanguage(request);
        var conflicts = _sidecarSubtitleService.FindConflictingSubtitleFiles(part.MediaFile, language);
        if (conflicts.Count > 0 && !request.OverwriteExisting)
        {
            return new ManagedPartDownloadResponseDto
            {
                Status = "confirmation_required",
                Message = "当前分段已存在同语言字幕，确认后将覆盖或替换旧字幕。",
                Conflict = BuildConflict(part, language, request.Ext, conflicts)
            };
        }

        var traceId = CreateTraceId();
        var downloadedSubtitle = await _subtitlesToolsApiClient
            .DownloadSubtitleAsync(request.SubtitleId, cancellationToken, traceId)
            .ConfigureAwait(false);
        var targetFile = await _sidecarSubtitleService
            .WriteSubtitleAsync(
                part.MediaFile,
                language,
                request.Ext,
                downloadedSubtitle.Content,
                conflicts,
                cancellationToken)
            .ConfigureAwait(false);

        QueueItemRefresh(context.Item);

        _logger.LogInformation(
            "trace={TraceId} multipart_download_complete item_id={ItemId} part_id={PartId} subtitle_id={SubtitleId} target_file={TargetFile}",
            traceId,
            itemId,
            part.Id,
            request.SubtitleId,
            targetFile.Name);

        return new ManagedPartDownloadResponseDto
        {
            Status = "downloaded",
            Message = "字幕已写入媒体目录。",
            WrittenSubtitle = new ManagedWrittenSubtitleDto
            {
                FileName = targetFile.Name,
                Language = language,
                Format = _subtitleMetadataService.NormalizeFormat(request.Ext)
            }
        };
    }

    /// <summary>
    /// 为所有分段分别搜索最佳字幕，并按每段第一名结果执行写入。
    /// </summary>
    /// <param name="itemId">Jellyfin 媒体项标识。</param>
    /// <param name="request">批量下载请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>批量下载结果。</returns>
    public async Task<ManagedDownloadBestResponseDto> DownloadBestAsync(
        Guid itemId,
        ManagedDownloadBestRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = ResolveContext(itemId, cancellationToken);
        var previewResults = new List<ManagedBatchPartResultDto>();
        var conflicts = new List<ManagedDownloadConflictDto>();
        var pendingDownloads = new List<PendingBestDownload>();
        var traceId = CreateTraceId();

        foreach (var part in context.Group.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hashMetrics = await _videoHashResolverService
                .ResolveAsync(part.MediaFile.FullName, cancellationToken, traceId)
                .ConfigureAwait(false);
            var searchResponse = await _subtitlesToolsApiClient.SearchAsync(
                new SubtitleSearchRequestDto
                {
                    Gcid = hashMetrics.HashResult.Gcid,
                    Cid = hashMetrics.HashResult.Cid,
                    Name = part.MediaFile.Name
                },
                cancellationToken,
                traceId).ConfigureAwait(false);

            var bestCandidate = searchResponse.Items.FirstOrDefault();
            if (bestCandidate is null)
            {
                previewResults.Add(new ManagedBatchPartResultDto
                {
                    PartId = part.Id,
                    Label = part.Label,
                    Status = "no_candidates",
                    Message = "未找到可下载的字幕候选。"
                });
                continue;
            }

            var language = _subtitleMetadataService.ResolveThreeLetterLanguage(bestCandidate.Languages);
            var conflictingFiles = _sidecarSubtitleService.FindConflictingSubtitleFiles(part.MediaFile, language);
            if (conflictingFiles.Count > 0 && !request.OverwriteExisting)
            {
                var conflict = BuildConflict(part, language, bestCandidate.Ext, conflictingFiles);
                conflicts.Add(conflict);
                previewResults.Add(new ManagedBatchPartResultDto
                {
                    PartId = part.Id,
                    Label = part.Label,
                    Status = "confirmation_required",
                    Message = "存在同语言字幕冲突，等待确认。",
                    Conflict = conflict
                });
                continue;
            }

            pendingDownloads.Add(new PendingBestDownload
            {
                Part = part,
                Candidate = bestCandidate,
                Language = language,
                ConflictingFiles = conflictingFiles
            });
        }

        if (conflicts.Count > 0 && !request.OverwriteExisting)
        {
            return new ManagedDownloadBestResponseDto
            {
                Status = "confirmation_required",
                Message = "部分分段已存在同语言字幕，确认后将统一覆盖或替换。",
                Items = previewResults,
                Conflicts = conflicts
            };
        }

        foreach (var pendingDownload in pendingDownloads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var downloadedSubtitle = await _subtitlesToolsApiClient
                    .DownloadSubtitleAsync(pendingDownload.Candidate.Id, cancellationToken, traceId)
                    .ConfigureAwait(false);
                var targetFile = await _sidecarSubtitleService
                    .WriteSubtitleAsync(
                        pendingDownload.Part.MediaFile,
                        pendingDownload.Language,
                        pendingDownload.Candidate.Ext,
                        downloadedSubtitle.Content,
                        pendingDownload.ConflictingFiles,
                        cancellationToken)
                    .ConfigureAwait(false);

                previewResults.Add(new ManagedBatchPartResultDto
                {
                    PartId = pendingDownload.Part.Id,
                    Label = pendingDownload.Part.Label,
                    Status = "downloaded",
                    Message = "字幕已写入媒体目录。",
                    WrittenSubtitle = new ManagedWrittenSubtitleDto
                    {
                        FileName = targetFile.Name,
                        Language = pendingDownload.Language,
                        Format = _subtitleMetadataService.NormalizeFormat(pendingDownload.Candidate.Ext)
                    }
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SubtitlesToolsApiException)
            {
                _logger.LogWarning(
                    ex,
                    "trace={TraceId} multipart_download_best_part_failed item_id={ItemId} part_id={PartId}",
                    traceId,
                    itemId,
                    pendingDownload.Part.Id);
                previewResults.Add(new ManagedBatchPartResultDto
                {
                    PartId = pendingDownload.Part.Id,
                    Label = pendingDownload.Part.Label,
                    Status = "failed",
                    Message = ex.Message
                });
            }
        }

        if (previewResults.Any(item => string.Equals(item.Status, "downloaded", StringComparison.OrdinalIgnoreCase)))
        {
            QueueItemRefresh(context.Item);
        }

        var successCount = previewResults.Count(item => string.Equals(item.Status, "downloaded", StringComparison.OrdinalIgnoreCase));
        var failedCount = previewResults.Count - successCount;
        return new ManagedDownloadBestResponseDto
        {
            Status = failedCount > 0 ? "partial" : "completed",
            Message = failedCount > 0
                ? $"批量下载完成，成功 {successCount} 个分段，失败或跳过 {failedCount} 个分段。"
                : $"批量下载完成，共写入 {successCount} 个分段字幕。",
            Items = previewResults
        };
    }

    private ManagedItemPartsResponseDto BuildItemPartsResponse(ManagedItemContext context)
    {
        return new ManagedItemPartsResponseDto
        {
            ItemId = context.Item.Id.ToString("D"),
            Name = context.Item.Name,
            ItemType = context.Item.GetType().Name,
            IsMultipart = context.Group.Parts.Count > 1,
            CurrentPartId = context.Group.CurrentPartId,
            Parts = context.Group.Parts
                .Select(part => new ManagedMediaPartDto
                {
                    Id = part.Id,
                    Index = part.Index,
                    Label = part.Label,
                    FileName = part.MediaFile.Name,
                    PartKind = part.PartKind,
                    PartNumber = part.PartNumber,
                    IsCurrent = string.Equals(part.Id, context.Group.CurrentPartId, StringComparison.Ordinal),
                    ExistingSubtitles = _sidecarSubtitleService.GetExistingSubtitles(part.MediaFile)
                })
                .ToList()
        };
    }

    private ManagedSubtitleCandidateDto BuildManagedCandidate(MultipartMediaPart part, SubtitleSearchItemDto item)
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
            TargetFileName = _sidecarSubtitleService.BuildTargetSubtitleFile(part.MediaFile, language, item.Ext).Name
        };
    }

    private ManagedDownloadConflictDto BuildConflict(
        MultipartMediaPart part,
        string language,
        string format,
        IReadOnlyList<FileInfo> conflictingFiles)
    {
        return new ManagedDownloadConflictDto
        {
            PartId = part.Id,
            PartLabel = part.Label,
            Language = language,
            TargetFileName = _sidecarSubtitleService.BuildTargetSubtitleFile(part.MediaFile, language, format).Name,
            ExistingFiles = conflictingFiles.Select(file => file.Name).ToList()
        };
    }

    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "媒体路径不直接来自外部输入，而是来自 Jellyfin 已入库项；这里只允许 Movie/Episode、本地路径且文件必须存在。")]
    private ManagedItemContext ResolveContext(Guid itemId, CancellationToken cancellationToken)
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

        var mediaFile = new FileInfo(item.Path);
        if (!mediaFile.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在或 Jellyfin 无法读取该路径。", item.Path);
        }

        var group = _multipartMediaParserService.Parse(mediaFile.FullName);
        return new ManagedItemContext
        {
            Item = item,
            Group = group
        };
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
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            return request.Language.Trim().ToLowerInvariant();
        }

        return "und";
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    /// <summary>
    /// 表示当前管理流程中的媒体上下文。
    /// </summary>
    private sealed class ManagedItemContext
    {
        /// <summary>
        /// 获取或设置当前 Jellyfin 媒体项。
        /// </summary>
        public BaseItem Item { get; set; } = null!;

        /// <summary>
        /// 获取或设置通过文件系统识别出的分段组。
        /// </summary>
        public MultipartMediaGroup Group { get; set; } = null!;
    }

    /// <summary>
    /// 表示待执行的一键最佳匹配下载项。
    /// </summary>
    private sealed class PendingBestDownload
    {
        /// <summary>
        /// 获取或设置目标分段。
        /// </summary>
        public MultipartMediaPart Part { get; set; } = null!;

        /// <summary>
        /// 获取或设置该分段选中的最佳候选。
        /// </summary>
        public SubtitleSearchItemDto Candidate { get; set; } = null!;

        /// <summary>
        /// 获取或设置写入时使用的语言码。
        /// </summary>
        public string Language { get; set; } = "und";

        /// <summary>
        /// 获取或设置同语言冲突字幕列表。
        /// </summary>
        public List<FileInfo> ConflictingFiles { get; set; } = [];
    }
}
