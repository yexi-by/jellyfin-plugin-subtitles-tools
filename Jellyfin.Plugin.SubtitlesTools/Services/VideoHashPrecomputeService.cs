using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 在插件后台持续处理“新视频入库后自动纳管为 MKV”的任务队列。
/// 自动纳管会统一走“确保当前文件已被 MKV 元数据纳管”的流程。
/// </summary>
public sealed class VideoHashPrecomputeService : BackgroundService
{
    private readonly record struct ManageWorkItem(Guid ItemId, string MediaPath, string TraceId);

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly MkvMetadataIdentityService _mkvMetadataIdentityService;
    private readonly ILogger<VideoHashPrecomputeService> _logger;
    private readonly Channel<ManageWorkItem> _queue = Channel.CreateUnbounded<ManageWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _scheduledMediaPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化后台自动纳管服务。
    /// </summary>
    public VideoHashPrecomputeService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        MkvMetadataIdentityService mkvMetadataIdentityService,
        ILogger<VideoHashPrecomputeService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _mkvMetadataIdentityService = mkvMetadataIdentityService;
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
        while (!stoppingToken.IsCancellationRequested)
        {
            ManageWorkItem workItem;
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

            await ProcessWorkItemAsync(workItem, stoppingToken).ConfigureAwait(false);
        }
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

        if (TryMatchAutoPreprocessBlacklistPath(mediaPath, out var blacklistPath))
        {
            _logger.LogInformation(
                "auto_manage_skip_blacklisted media_path={MediaPath} blacklist_path={BlacklistPath}",
                mediaPath,
                blacklistPath);
            return;
        }

        if (_scheduledMediaPaths.TryAdd(mediaPath, 0))
        {
            _queue.Writer.TryWrite(new ManageWorkItem(eventArgs.Item.Id, mediaPath, CreateTraceId()));
            _logger.LogInformation("auto_manage_enqueue media_path={MediaPath}", mediaPath);
        }
    }

    private async Task ProcessWorkItemAsync(ManageWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            if (!ShouldHandleNewItem())
            {
                _logger.LogDebug(
                    "trace={TraceId} auto_manage_skip_disabled media_path={MediaPath}",
                    workItem.TraceId,
                    workItem.MediaPath);
                return;
            }

            if (TryMatchAutoPreprocessBlacklistPath(workItem.MediaPath, out var blacklistPath))
            {
                _logger.LogInformation(
                    "trace={TraceId} auto_manage_skip_blacklisted media_path={MediaPath} blacklist_path={BlacklistPath}",
                    workItem.TraceId,
                    workItem.MediaPath,
                    blacklistPath);
                return;
            }

            var result = await _mkvMetadataIdentityService
                .EnsureManagedAsync(workItem.MediaPath, cancellationToken, workItem.TraceId)
                .ConfigureAwait(false);
            if (result.ConvertedToMkv || result.WroteMetadata)
            {
                QueueItemRefresh(workItem.ItemId);
            }

            _logger.LogInformation(
                "trace={TraceId} auto_manage_complete media_path={MediaPath} output_path={OutputPath} converted={ConvertedToMkv} wrote_metadata={WroteMetadata}",
                workItem.TraceId,
                workItem.MediaPath,
                result.Identity.MediaPath,
                result.ConvertedToMkv,
                result.WroteMetadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FfmpegExecutionException)
        {
            _logger.LogWarning(
                ex,
                "trace={TraceId} auto_manage_failed media_path={MediaPath}",
                workItem.TraceId,
                workItem.MediaPath);
        }
        finally
        {
            _scheduledMediaPaths.TryRemove(workItem.MediaPath, out _);
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
            IsAutomated = true,
            RemoveOldMetadata = false,
            RegenerateTrickplay = false
        };

        _providerManager.QueueRefresh(itemId, refreshOptions, RefreshPriority.High);
    }

    private static bool ShouldHandleNewItem()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return configuration.EnableAutoVideoConvertToMkv;
    }

    private static bool TryMatchAutoPreprocessBlacklistPath(string mediaPath, out string blacklistPath)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return PluginConfiguration.TryMatchAutoPreprocessBlacklistPath(
            mediaPath,
            configuration.AutoPreprocessPathBlacklist,
            out blacklistPath);
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }
}
