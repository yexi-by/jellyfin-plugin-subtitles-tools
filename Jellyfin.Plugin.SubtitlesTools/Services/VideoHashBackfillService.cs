using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 扫描现有媒体库并补算缺失的视频哈希，供计划任务手动触发。
/// </summary>
public sealed class VideoHashBackfillService
{
    private readonly ILibraryManager _libraryManager;
    private readonly VideoHashResolverService _videoHashResolverService;
    private readonly ILogger<VideoHashBackfillService> _logger;

    /// <summary>
    /// 初始化缺失视频哈希回填服务。
    /// </summary>
    /// <param name="libraryManager">媒体库管理器。</param>
    /// <param name="videoHashResolverService">视频哈希解析服务。</param>
    /// <param name="logger">日志记录器。</param>
    public VideoHashBackfillService(
        ILibraryManager libraryManager,
        VideoHashResolverService videoHashResolverService,
        ILogger<VideoHashBackfillService> logger)
    {
        _libraryManager = libraryManager;
        _videoHashResolverService = videoHashResolverService;
        _logger = logger;
    }

    /// <summary>
    /// 扫描 Jellyfin 已入库的本地电影和剧集，补算所有缺失的视频哈希。
    /// </summary>
    /// <param name="progress">任务进度汇报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task PrecomputeMissingHashesAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var concurrency = GetNormalizedConcurrency();
        var candidatePaths = EnumerateEligibleMediaPaths().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingPaths = new List<string>(candidatePaths.Length);

        foreach (var mediaPath in candidatePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await _videoHashResolverService.TryGetCachedAsync(mediaPath, cancellationToken).ConfigureAwait(false);
            if (cached is null)
            {
                missingPaths.Add(mediaPath);
            }
        }

        if (missingPaths.Count == 0)
        {
            progress.Report(100);
            _logger.LogInformation("manual_hash_backfill_complete candidates={CandidateCount} missing=0", candidatePaths.Length);
            return;
        }

        _logger.LogInformation(
            "manual_hash_backfill_start candidates={CandidateCount} missing={MissingCount} concurrency={Concurrency}",
            candidatePaths.Length,
            missingPaths.Count,
            concurrency);

        var completedCount = 0;
        await Parallel.ForEachAsync(
            missingPaths,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cancellationToken
            },
            async (mediaPath, token) =>
            {
                var traceId = CreateTraceId();
                try
                {
                    await _videoHashResolverService.ResolveAsync(mediaPath, token, traceId).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _logger.LogWarning(
                        ex,
                        "trace={TraceId} manual_hash_backfill_item_failed media_path={MediaPath}",
                        traceId,
                        mediaPath);
                }
                finally
                {
                    var current = Interlocked.Increment(ref completedCount);
                    progress.Report(current * 100d / missingPaths.Count);
                }
            }).ConfigureAwait(false);

        _logger.LogInformation(
            "manual_hash_backfill_complete candidates={CandidateCount} missing={MissingCount} concurrency={Concurrency}",
            candidatePaths.Length,
            missingPaths.Count,
            concurrency);
    }

    /// <summary>
    /// 判断一个已入库项目是否符合“本地电影或剧集文件”的预计算条件。
    /// </summary>
    /// <param name="item">待判断的媒体库项目。</param>
    /// <param name="mediaPath">输出的本地文件路径。</param>
    /// <returns>符合条件时返回 <see langword="true"/>。</returns>
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

        if (string.Equals(Path.GetExtension(item.Path), ".strm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileInfo = new FileInfo(item.Path);
        if (!fileInfo.Exists)
        {
            return false;
        }

        mediaPath = fileInfo.FullName;
        return true;
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private IEnumerable<string> EnumerateEligibleMediaPaths()
    {
        foreach (var item in _libraryManager.RootFolder.GetRecursiveChildren())
        {
            if (TryGetEligibleMediaPath(item, out var mediaPath))
            {
                yield return mediaPath;
            }
        }
    }

    private static int GetNormalizedConcurrency()
    {
        var rawConfiguration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return PluginConfiguration.NormalizeHashPrecomputeConcurrency(rawConfiguration.HashPrecomputeConcurrency);
    }
}
