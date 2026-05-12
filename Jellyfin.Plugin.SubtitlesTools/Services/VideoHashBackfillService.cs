using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 扫描现有媒体库，把尚未被插件处理或仍需兼容修复的本地视频统一转为受管 MKV。
/// 当前版本不再依赖插件侧哈希归档，而是只认 MKV 自定义元数据和视频自身的兼容风险。
/// </summary>
public sealed class VideoHashBackfillService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly MkvMetadataIdentityService _mkvMetadataIdentityService;
    private readonly ILogger<VideoHashBackfillService> _logger;

    /// <summary>
    /// 初始化手动处理回填服务。
    /// </summary>
    public VideoHashBackfillService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        MkvMetadataIdentityService mkvMetadataIdentityService,
        ILogger<VideoHashBackfillService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _mkvMetadataIdentityService = mkvMetadataIdentityService;
        _logger = logger;
    }

    /// <summary>
    /// 扫描 Jellyfin 已入库的本地电影和剧集，把未处理文件统一转为 MKV，
    /// 并继续修复那些”已处理但仍存在兼容性问题”的旧视频。
    /// </summary>
    public async Task ManageUnprocessedVideosAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var candidates = EnumerateEligibleItems().ToArray();
        var pendingItems = new List<EligibleMediaItem>(candidates.Length);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inspection = await _mkvMetadataIdentityService
                .InspectAsync(candidate.MediaPath, cancellationToken)
                .ConfigureAwait(false);
            if (inspection.Identity is null || inspection.NeedsCompatibilityRepair)
            {
                pendingItems.Add(candidate);
            }
        }

        if (pendingItems.Count == 0)
        {
            progress.Report(100);
            _logger.LogInformation("manual_manage_backfill_complete candidates={CandidateCount} pending=0", candidates.Length);
            return;
        }

        var concurrency = GetNormalizedConcurrency();
        _logger.LogInformation(
            "manual_manage_backfill_start candidates={CandidateCount} pending={PendingCount} concurrency={Concurrency}",
            candidates.Length,
            pendingItems.Count,
            concurrency);

        var completedCount = 0;
        await Parallel.ForEachAsync(
            pendingItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cancellationToken
            },
            async (candidate, token) =>
            {
                var traceId = CreateTraceId();
                try
                {
                    var result = await _mkvMetadataIdentityService
                        .EnsureManagedAsync(candidate.MediaPath, token, traceId)
                        .ConfigureAwait(false);
                    if (result.ConvertedToMkv || result.WroteMetadata)
                    {
                        QueueItemRefresh(candidate.ItemId);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FfmpegExecutionException)
                {
                    _logger.LogWarning(
                        ex,
                        "trace={TraceId} manual_manage_backfill_item_failed media_path={MediaPath}",
                        traceId,
                        candidate.MediaPath);
                }
                finally
                {
                    var current = Interlocked.Increment(ref completedCount);
                    progress.Report(current * 100d / pendingItems.Count);
                }
            }).ConfigureAwait(false);

        _logger.LogInformation(
            "manual_manage_backfill_complete candidates={CandidateCount} pending={PendingCount} concurrency={Concurrency}",
            candidates.Length,
            pendingItems.Count,
            concurrency);
    }

    /// <summary>
    /// 判断一个已入库项目是否符合”本地电影或剧集文件”的处理条件。
    /// </summary>
    internal static bool TryGetEligibleMediaPath(BaseItem? item, out string mediaPath)
    {
        mediaPath = string.Empty;
        if (item is not Movie && item is not Episode)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return false;
        }

        var itemPath = item.Path.Trim();
        if (string.Equals(Path.GetExtension(itemPath), ".strm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var directFile = new FileInfo(itemPath);
        if (directFile.Exists)
        {
            mediaPath = directFile.FullName;
            return true;
        }

        var fallbackMkvPath = Path.Combine(
            Path.GetDirectoryName(itemPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(itemPath)}.mkv");
        var fallbackFile = new FileInfo(fallbackMkvPath);
        if (fallbackFile.Exists)
        {
            mediaPath = fallbackFile.FullName;
            return true;
        }

        return false;
    }

    private IEnumerable<EligibleMediaItem> EnumerateEligibleItems()
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _libraryManager.RootFolder.GetRecursiveChildren())
        {
            if (!TryGetEligibleMediaPath(item, out var mediaPath))
            {
                continue;
            }

            if (!seenPaths.Add(mediaPath))
            {
                continue;
            }

            yield return new EligibleMediaItem
            {
                ItemId = item.Id,
                MediaPath = mediaPath
            };
        }
    }

    private void QueueItemRefresh(Guid itemId)
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

        _providerManager.QueueRefresh(itemId, refreshOptions, RefreshPriority.High);
    }

    private static int GetNormalizedConcurrency()
    {
        var rawConfiguration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return Configuration.PluginConfiguration.NormalizeVideoConvertConcurrency(rawConfiguration.VideoConvertConcurrency);
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    /// <summary>
    /// 表示一个待处理的 Jellyfin 媒体项。
    /// </summary>
    private sealed class EligibleMediaItem
    {
        public Guid ItemId { get; set; }
        public string MediaPath { get; set; } = string.Empty;
    }
}
