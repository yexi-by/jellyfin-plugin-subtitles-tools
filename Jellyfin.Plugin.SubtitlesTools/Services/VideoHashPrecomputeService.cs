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
/// 在插件后台持续消费“新视频入库后的自动处理”队列。
/// 该服务负责两件事：
/// 1. 先为新媒体确保原始 CID/GCID 已被持久化。
/// 2. 若配置开启，则继续把该媒体自动转换为 MKV。
/// </summary>
public sealed class VideoHashPrecomputeService : BackgroundService
{
    private readonly record struct HashPrecomputeWorkItem(string MediaPath, string TraceId, bool IsAutomatic);

    private readonly ILibraryManager _libraryManager;
    private readonly OriginalVideoHashArchiveService _originalVideoHashArchiveService;
    private readonly VideoContainerConversionService _videoContainerConversionService;
    private readonly ILogger<VideoHashPrecomputeService> _logger;
    private readonly Channel<HashPrecomputeWorkItem> _queue = Channel.CreateUnbounded<HashPrecomputeWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _scheduledMediaPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化自动哈希与自动转 MKV 后台服务。
    /// </summary>
    /// <param name="libraryManager">媒体库管理器。</param>
    /// <param name="originalVideoHashArchiveService">原始媒体哈希档案服务。</param>
    /// <param name="videoContainerConversionService">视频容器转换服务。</param>
    /// <param name="logger">日志记录器。</param>
    public VideoHashPrecomputeService(
        ILibraryManager libraryManager,
        OriginalVideoHashArchiveService originalVideoHashArchiveService,
        VideoContainerConversionService videoContainerConversionService,
        ILogger<VideoHashPrecomputeService> logger)
    {
        _libraryManager = libraryManager;
        _originalVideoHashArchiveService = originalVideoHashArchiveService;
        _videoContainerConversionService = videoContainerConversionService;
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

            if (runningTasks.Count >= GetEffectiveWorkerConcurrency())
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
        if (!ShouldHandleNewItem())
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
            _logger.LogInformation("auto_media_enqueue media_path={MediaPath}", mediaPath);
        }
    }

    private async Task ProcessWorkItemAsync(HashPrecomputeWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            if (workItem.IsAutomatic && !ShouldHandleNewItem())
            {
                _logger.LogDebug(
                    "trace={TraceId} auto_media_skip_disabled media_path={MediaPath}",
                    workItem.TraceId,
                    workItem.MediaPath);
                return;
            }

            var configuration = GetNormalizedConfiguration();
            var hashArchive = await _originalVideoHashArchiveService
                .EnsureAsync(workItem.MediaPath, cancellationToken, workItem.TraceId)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "trace={TraceId} auto_hash_complete media_path={MediaPath} original_gcid={OriginalGcid} current_path={CurrentPath}",
                workItem.TraceId,
                workItem.MediaPath,
                hashArchive.OriginalGcid,
                hashArchive.CurrentMediaPath);

            if (!configuration.EnableAutoVideoConvertToMkv)
            {
                return;
            }

            var conversionResult = await _videoContainerConversionService
                .EnsureMkvAsync(hashArchive.CurrentMediaPath, cancellationToken, workItem.TraceId)
                .ConfigureAwait(false);

            if (!string.Equals(hashArchive.CurrentMediaPath, conversionResult.OutputPath, StringComparison.OrdinalIgnoreCase))
            {
                await _originalVideoHashArchiveService
                    .UpdateCurrentPathAsync(hashArchive, conversionResult.OutputPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "trace={TraceId} auto_video_convert_complete media_path={MediaPath} output_path={OutputPath} used_transcode_fallback={UsedTranscodeFallback}",
                workItem.TraceId,
                workItem.MediaPath,
                conversionResult.OutputPath,
                conversionResult.UsedTranscodeFallback);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FfmpegExecutionException)
        {
            _logger.LogWarning(
                ex,
                "trace={TraceId} auto_media_process_failed media_path={MediaPath}",
                workItem.TraceId,
                workItem.MediaPath);
        }
        finally
        {
            _scheduledMediaPaths.TryRemove(workItem.MediaPath, out _);
        }
    }

    private bool ShouldHandleNewItem()
    {
        var configuration = GetNormalizedConfiguration();
        return configuration.EnableAutoHashPrecompute || configuration.EnableAutoVideoConvertToMkv;
    }

    private int GetEffectiveWorkerConcurrency()
    {
        var configuration = GetNormalizedConfiguration();
        if (configuration.EnableAutoVideoConvertToMkv)
        {
            return configuration.VideoConvertConcurrency;
        }

        return configuration.HashPrecomputeConcurrency;
    }

    private PluginConfiguration GetNormalizedConfiguration()
    {
        var rawConfiguration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new PluginConfiguration
        {
            ServiceBaseUrl = PluginConfiguration.NormalizeServiceBaseUrl(rawConfiguration.ServiceBaseUrl),
            RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(rawConfiguration.RequestTimeoutSeconds),
            EnableAutoHashPrecompute = rawConfiguration.EnableAutoHashPrecompute,
            HashPrecomputeConcurrency = PluginConfiguration.NormalizeHashPrecomputeConcurrency(rawConfiguration.HashPrecomputeConcurrency),
            EnableAutoVideoConvertToMkv = rawConfiguration.EnableAutoVideoConvertToMkv,
            VideoConvertConcurrency = PluginConfiguration.NormalizeVideoConvertConcurrency(rawConfiguration.VideoConvertConcurrency),
            FfmpegExecutablePath = PluginConfiguration.NormalizeFfmpegExecutablePath(rawConfiguration.FfmpegExecutablePath)
        };
    }
}
