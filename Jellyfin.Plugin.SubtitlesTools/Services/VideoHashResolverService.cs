using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 统一处理视频哈希缓存读取、按需计算与结果回写，避免多处重复实现同一套流程。
/// </summary>
public sealed class VideoHashResolverService
{
    private readonly VideoHashCalculator _videoHashCalculator;
    private readonly VideoHashCacheService _videoHashCacheService;
    private readonly ILogger<VideoHashResolverService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化视频哈希解析服务。
    /// </summary>
    /// <param name="videoHashCalculator">视频哈希计算器。</param>
    /// <param name="videoHashCacheService">视频哈希缓存服务。</param>
    /// <param name="logger">日志记录器。</param>
    public VideoHashResolverService(
        VideoHashCalculator videoHashCalculator,
        VideoHashCacheService videoHashCacheService,
        ILogger<VideoHashResolverService> logger)
    {
        _videoHashCalculator = videoHashCalculator;
        _videoHashCacheService = videoHashCacheService;
        _logger = logger;
    }

    /// <summary>
    /// 获取指定媒体文件的有效哈希结果；若缓存不存在，则会即时计算并写回缓存。
    /// </summary>
    /// <param name="mediaPath">媒体文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>包含缓存命中情况和耗时的解析结果。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "仅处理 Jellyfin 已验证存在的本地媒体路径；方法入口会再次校验文件存在性，后续只在该固定路径上读取哈希。")]
    public async Task<VideoHashResolutionMetrics> ResolveAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            throw new ArgumentException("媒体路径不能为空。", nameof(mediaPath));
        }

        var fileInfo = new FileInfo(mediaPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在。", mediaPath);
        }

        var pathLock = _pathLocks.GetOrAdd(fileInfo.FullName, static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cacheLookupStopwatch = Stopwatch.StartNew();
            var cached = await _videoHashCacheService.TryGetAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
            cacheLookupStopwatch.Stop();
            if (cached is not null)
            {
                return new VideoHashResolutionMetrics
                {
                    HashResult = cached,
                    CacheHit = true,
                    CacheLookupMs = cacheLookupStopwatch.Elapsed.TotalMilliseconds,
                    ComputeMs = 0,
                    CacheSaveMs = 0
                };
            }

            var computeStopwatch = Stopwatch.StartNew();
            var computed = await _videoHashCalculator
                .ComputeAsync(fileInfo.FullName, cancellationToken, traceId)
                .ConfigureAwait(false);
            computeStopwatch.Stop();

            var cacheSaveStopwatch = Stopwatch.StartNew();
            await _videoHashCacheService.SaveAsync(computed, cancellationToken).ConfigureAwait(false);
            cacheSaveStopwatch.Stop();

            return new VideoHashResolutionMetrics
            {
                HashResult = computed,
                CacheHit = false,
                CacheLookupMs = cacheLookupStopwatch.Elapsed.TotalMilliseconds,
                ComputeMs = computeStopwatch.Elapsed.TotalMilliseconds,
                CacheSaveMs = cacheSaveStopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "trace={TraceId} video_hash_resolve_failed media_path={MediaPath}",
                traceId ?? "-",
                fileInfo.FullName);
            throw;
        }
        finally
        {
            pathLock.Release();
        }
    }

    /// <summary>
    /// 仅尝试读取现有缓存，不触发新的哈希计算。
    /// </summary>
    /// <param name="mediaPath">媒体文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中时返回缓存结果，否则返回 <see langword="null"/>。</returns>
    public Task<VideoHashResult?> TryGetCachedAsync(string mediaPath, CancellationToken cancellationToken)
    {
        return _videoHashCacheService.TryGetAsync(mediaPath, cancellationToken);
    }
}
