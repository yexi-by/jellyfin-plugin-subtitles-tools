using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 在插件后台持续消费“自动预计算”队列。
/// 该服务仅处理新增媒体入库后的自动预计算，不负责手动全库回填。
/// </summary>
public sealed class VideoHashPrecomputeService : BackgroundService
{
    private readonly record struct HashPrecomputeWorkItem(string MediaPath, string TraceId, bool IsAutomatic);

    private readonly ILibraryManager _libraryManager;
    private readonly VideoHashResolverService _videoHashResolverService;
    private readonly ILogger<VideoHashPrecomputeService> _logger;
    private readonly Channel<HashPrecomputeWorkItem> _queue = Channel.CreateUnbounded<HashPrecomputeWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _scheduledMediaPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化自动预计算后台服务。
    /// </summary>
    /// <param name="libraryManager">媒体库管理器。</param>
    /// <param name="videoHashResolverService">视频哈希解析服务。</param>
    /// <param name="logger">日志记录器。</param>
    public VideoHashPrecomputeService(
        ILibraryManager libraryManager,
        VideoHashResolverService videoHashResolverService,
        ILogger<VideoHashPrecomputeService> logger)
    {
        _libraryManager = libraryManager;
        _videoHashResolverService = videoHashResolverService;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnLibraryItemAdded;
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnLibraryItemAdded;
        _queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runningTasks = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            runningTasks.RemoveAll(static task => task.IsCompleted);

            if (runningTasks.Count >= GetNormalizedConfiguration().HashPrecomputeConcurrency)
            {
                await Task.WhenAny(runningTasks).ConfigureAwait(false);
                continue;
            }

            HashPrecomputeWorkItem workItem;
            try
            {
                workItem = await _queue.Reader.ReadAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            runningTasks.Add(ProcessWorkItemAsync(workItem, stoppingToken));
        }

        await Task.WhenAll(runningTasks).ConfigureAwait(false);
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private void OnLibraryItemAdded(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (!GetNormalizedConfiguration().EnableAutoHashPrecompute)
        {
            return;
        }

        if (!VideoHashBackfillService.TryGetEligibleMediaPath(eventArgs.Item, out var mediaPath))
        {
            return;
        }

        if (_scheduledMediaPaths.TryAdd(mediaPath, 0))
        {
            _queue.Writer.TryWrite(new HashPrecomputeWorkItem(mediaPath, CreateTraceId(), IsAutomatic: true));
            _logger.LogInformation("auto_hash_enqueue media_path={MediaPath}", mediaPath);
        }
    }

    private async Task ProcessWorkItemAsync(HashPrecomputeWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            if (workItem.IsAutomatic && !GetNormalizedConfiguration().EnableAutoHashPrecompute)
            {
                _logger.LogDebug(
                    "trace={TraceId} auto_hash_skip_disabled media_path={MediaPath}",
                    workItem.TraceId,
                    workItem.MediaPath);
                return;
            }

            var metrics = await _videoHashResolverService
                .ResolveAsync(workItem.MediaPath, cancellationToken, workItem.TraceId)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} auto_hash_complete media_path={MediaPath} cache_hit={CacheHit} cache_lookup_ms={CacheLookupMs:F2} compute_ms={ComputeMs:F2} cache_save_ms={CacheSaveMs:F2}",
                workItem.TraceId,
                workItem.MediaPath,
                metrics.CacheHit,
                metrics.CacheLookupMs,
                metrics.ComputeMs,
                metrics.CacheSaveMs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "trace={TraceId} auto_hash_failed media_path={MediaPath}",
                workItem.TraceId,
                workItem.MediaPath);
        }
        finally
        {
            _scheduledMediaPaths.TryRemove(workItem.MediaPath, out _);
        }
    }

    private PluginConfiguration GetNormalizedConfiguration()
    {
        var rawConfiguration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new PluginConfiguration
        {
            ServiceBaseUrl = PluginConfiguration.NormalizeServiceBaseUrl(rawConfiguration.ServiceBaseUrl),
            RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(rawConfiguration.RequestTimeoutSeconds),
            EnableAutoHashPrecompute = rawConfiguration.EnableAutoHashPrecompute,
            HashPrecomputeConcurrency = PluginConfiguration.NormalizeHashPrecomputeConcurrency(rawConfiguration.HashPrecomputeConcurrency)
        };
    }
}
