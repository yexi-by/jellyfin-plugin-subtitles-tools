using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 维护“原始媒体哈希档案”。
/// 该档案独立于按路径命中的短期缓存，负责在视频转容器后继续保存旧的 CID/GCID。
/// </summary>
public sealed class OriginalVideoHashArchiveService : IDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly VideoHashResolverService _videoHashResolverService;
    private readonly ILogger<OriginalVideoHashArchiveService> _logger;
    private readonly Func<FileInfo> _archiveFileAccessor;
    private readonly SemaphoreSlim _archiveLock = new(1, 1);

    /// <summary>
    /// 初始化原始媒体哈希档案服务。
    /// </summary>
    /// <param name="videoHashResolverService">视频哈希解析服务。</param>
    /// <param name="logger">日志记录器。</param>
    public OriginalVideoHashArchiveService(
        VideoHashResolverService videoHashResolverService,
        ILogger<OriginalVideoHashArchiveService> logger)
        : this(videoHashResolverService, logger, GetDefaultArchiveFile)
    {
    }

    /// <summary>
    /// 仅供测试时替换档案文件位置。
    /// </summary>
    internal OriginalVideoHashArchiveService(
        VideoHashResolverService videoHashResolverService,
        ILogger<OriginalVideoHashArchiveService> logger,
        Func<FileInfo> archiveFileAccessor)
    {
        _videoHashResolverService = videoHashResolverService;
        _logger = logger;
        _archiveFileAccessor = archiveFileAccessor;
    }

    /// <summary>
    /// 按当前路径或原始路径读取档案。
    /// </summary>
    /// <param name="mediaPath">当前媒体路径或历史原始路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中时返回档案，否则返回空。</returns>
    public async Task<OriginalVideoIdentityEntry?> TryGetByPathAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        var normalizedPath = Path.GetFullPath(mediaPath);
        await _archiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesAsync(cancellationToken).ConfigureAwait(false);
            return entries.FirstOrDefault(entry =>
                string.Equals(entry.CurrentMediaPath, normalizedPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.OriginalMediaPath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _archiveLock.Release();
        }
    }

    /// <summary>
    /// 确保指定媒体已经拥有原始哈希档案；若不存在则先计算并写入。
    /// </summary>
    /// <param name="mediaPath">当前媒体路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>原始哈希档案。</returns>
    public async Task<OriginalVideoIdentityEntry> EnsureAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        var normalizedPath = Path.GetFullPath(mediaPath);
        var existingEntry = await TryGetByPathAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        if (existingEntry is not null)
        {
            if (!string.Equals(existingEntry.CurrentMediaPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateCurrentPathAsync(existingEntry, normalizedPath, cancellationToken).ConfigureAwait(false);
                existingEntry.CurrentMediaPath = normalizedPath;
                existingEntry.CurrentContainer = NormalizeContainer(Path.GetExtension(normalizedPath));
            }

            return existingEntry;
        }

        var metrics = await _videoHashResolverService
            .ResolveAsync(normalizedPath, cancellationToken, traceId)
            .ConfigureAwait(false);

        await _archiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesAsync(cancellationToken).ConfigureAwait(false);
            var concurrentEntry = entries.FirstOrDefault(entry =>
                string.Equals(entry.CurrentMediaPath, normalizedPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.OriginalMediaPath, normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (concurrentEntry is not null)
            {
                return concurrentEntry;
            }

            var nowUtcTicks = DateTime.UtcNow.Ticks;
            var createdEntry = new OriginalVideoIdentityEntry
            {
                OriginalMediaPath = normalizedPath,
                CurrentMediaPath = normalizedPath,
                OriginalCid = metrics.HashResult.Cid,
                OriginalGcid = metrics.HashResult.Gcid,
                CurrentContainer = NormalizeContainer(Path.GetExtension(normalizedPath)),
                CreatedAtUtcTicks = nowUtcTicks,
                UpdatedAtUtcTicks = nowUtcTicks
            };
            entries.Add(createdEntry);
            await SaveEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
            return createdEntry;
        }
        finally
        {
            _archiveLock.Release();
        }
    }

    /// <summary>
    /// 在媒体路径发生变化后更新档案中的当前路径与容器信息。
    /// </summary>
    /// <param name="entry">待更新档案。</param>
    /// <param name="newCurrentMediaPath">新的当前路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task UpdateCurrentPathAsync(
        OriginalVideoIdentityEntry entry,
        string newCurrentMediaPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var normalizedPath = Path.GetFullPath(newCurrentMediaPath);
        await _archiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesAsync(cancellationToken).ConfigureAwait(false);
            var targetEntry = entries.FirstOrDefault(item =>
                string.Equals(item.OriginalMediaPath, entry.OriginalMediaPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.CurrentMediaPath, entry.CurrentMediaPath, StringComparison.OrdinalIgnoreCase));
            if (targetEntry is null)
            {
                throw new FileNotFoundException("未找到需要更新的原始媒体哈希档案。", entry.CurrentMediaPath);
            }

            targetEntry.CurrentMediaPath = normalizedPath;
            targetEntry.CurrentContainer = NormalizeContainer(Path.GetExtension(normalizedPath));
            targetEntry.UpdatedAtUtcTicks = DateTime.UtcNow.Ticks;
            await SaveEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _archiveLock.Release();
        }
    }

    private static FileInfo GetDefaultArchiveFile()
    {
        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            throw new InvalidOperationException("插件数据目录尚未初始化，无法创建原始媒体哈希档案。");
        }

        return new FileInfo(Path.Combine(dataFolderPath, "original-video-hash-archive.json"));
    }

    private static string NormalizeContainer(string extension)
    {
        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }

    private async Task<List<OriginalVideoIdentityEntry>> LoadEntriesAsync(CancellationToken cancellationToken)
    {
        var archiveFile = _archiveFileAccessor();
        if (!archiveFile.Exists)
        {
            return [];
        }

        try
        {
            await using var stream = archiveFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            var payload = await JsonSerializer.DeserializeAsync<List<OriginalVideoIdentityEntry>>(
                stream,
                JsonSerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return payload ?? [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取原始媒体哈希档案失败，将回退为空档案。");
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "原始媒体哈希档案损坏，将回退为空档案。");
            return [];
        }
    }

    private async Task SaveEntriesAsync(
        List<OriginalVideoIdentityEntry> entries,
        CancellationToken cancellationToken)
    {
        var archiveFile = _archiveFileAccessor();
        Directory.CreateDirectory(archiveFile.DirectoryName!);
        await using var stream = archiveFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, entries, JsonSerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _archiveLock.Dispose();
    }
}

/// <summary>
/// 表示原始媒体哈希档案中的一条记录。
/// </summary>
public sealed class OriginalVideoIdentityEntry
{
    /// <summary>
    /// 获取或设置原始媒体路径。
    /// </summary>
    public string OriginalMediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前媒体路径。
    /// </summary>
    public string CurrentMediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置原始 CID。
    /// </summary>
    public string OriginalCid { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置原始 GCID。
    /// </summary>
    public string OriginalGcid { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前容器格式。
    /// </summary>
    public string CurrentContainer { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置创建时间的 UTC Tick。
    /// </summary>
    public long CreatedAtUtcTicks { get; set; }

    /// <summary>
    /// 获取或设置更新时间的 UTC Tick。
    /// </summary>
    public long UpdatedAtUtcTicks { get; set; }
}
