using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 管理内置字幕源的搜索结果、字幕元数据和字幕文件缓存。
/// 缓存同时保存在内存和插件数据目录，避免频繁访问上游字幕源。
/// </summary>
public sealed class SubtitleSourceCacheStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SearchCacheEntry> _searchMemoryCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedSubtitleMetadata> _subtitleMetaMemoryCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DownloadedSubtitle> _subtitleBinaryMemoryCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _diskLock = new(1, 1);
    private readonly string _cacheRootPath;

    /// <summary>
    /// 初始化字幕源缓存。
    /// </summary>
    public SubtitleSourceCacheStore()
        : this(GetDefaultCacheRootPath())
    {
    }

    /// <summary>
    /// 使用指定缓存根目录初始化缓存；该构造器供测试隔离磁盘缓存。
    /// </summary>
    /// <param name="cacheRootPath">缓存根目录。</param>
    internal SubtitleSourceCacheStore(string cacheRootPath)
    {
        if (string.IsNullOrWhiteSpace(cacheRootPath))
        {
            throw new ArgumentException("字幕源缓存目录不能为空。", nameof(cacheRootPath));
        }

        _cacheRootPath = cacheRootPath;
        Directory.CreateDirectory(SearchCacheDirectoryPath);
        Directory.CreateDirectory(SubtitleMetaCacheDirectoryPath);
        Directory.CreateDirectory(SubtitleFileCacheDirectoryPath);
    }

    private string SearchCacheDirectoryPath => Path.Combine(_cacheRootPath, "search");

    private string SubtitleMetaCacheDirectoryPath => Path.Combine(_cacheRootPath, "subtitle-meta");

    private string SubtitleFileCacheDirectoryPath => Path.Combine(_cacheRootPath, "subtitle-files");

    /// <summary>
    /// 读取搜索结果缓存。
    /// </summary>
    /// <param name="cacheKey">搜索缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>未过期时返回缓存条目，否则返回 <c>null</c>。</returns>
    internal async Task<SearchCacheEntry?> GetSearchEntryAsync(string cacheKey, CancellationToken cancellationToken)
    {
        if (_searchMemoryCache.TryGetValue(cacheKey, out var memoryEntry) && !IsExpired(memoryEntry.ExpiresAt))
        {
            return memoryEntry;
        }

        if (memoryEntry is not null)
        {
            _searchMemoryCache.TryRemove(cacheKey, out _);
        }

        var filePath = SearchCachePath(cacheKey);
        var entry = await ReadJsonFileAsync<SearchCacheEntry>(filePath, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        if (IsExpired(entry.ExpiresAt))
        {
            await DeleteFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            return null;
        }

        _searchMemoryCache[cacheKey] = entry;
        return entry;
    }

    /// <summary>
    /// 写入搜索结果缓存。
    /// </summary>
    /// <param name="cacheKey">搜索缓存键。</param>
    /// <param name="entry">搜索结果缓存条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task SetSearchEntryAsync(string cacheKey, SearchCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _searchMemoryCache[cacheKey] = entry;
        await WriteJsonFileAsync(SearchCachePath(cacheKey), entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取字幕元数据缓存。
    /// </summary>
    /// <param name="subtitleId">字幕稳定标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>未过期时返回字幕元数据，否则返回 <c>null</c>。</returns>
    internal async Task<CachedSubtitleMetadata?> GetSubtitleMetadataAsync(string subtitleId, CancellationToken cancellationToken)
    {
        if (_subtitleMetaMemoryCache.TryGetValue(subtitleId, out var memoryEntry) && !IsExpired(memoryEntry.ExpiresAt))
        {
            return memoryEntry;
        }

        if (memoryEntry is not null)
        {
            _subtitleMetaMemoryCache.TryRemove(subtitleId, out _);
        }

        var filePath = SubtitleMetaCachePath(subtitleId);
        var entry = await ReadJsonFileAsync<CachedSubtitleMetadata>(filePath, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        if (IsExpired(entry.ExpiresAt))
        {
            await DeleteFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            await DeleteFileAsync(SubtitleFilePath(entry), cancellationToken).ConfigureAwait(false);
            return null;
        }

        _subtitleMetaMemoryCache[subtitleId] = entry;
        return entry;
    }

    /// <summary>
    /// 写入字幕元数据缓存。
    /// </summary>
    /// <param name="metadata">字幕下载元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task SetSubtitleMetadataAsync(CachedSubtitleMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        _subtitleMetaMemoryCache[metadata.SubtitleId] = metadata;
        await WriteJsonFileAsync(SubtitleMetaCachePath(metadata.SubtitleId), metadata, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取字幕文件缓存。
    /// </summary>
    /// <param name="metadata">字幕下载元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>未过期且文件存在时返回字幕内容，否则返回 <c>null</c>。</returns>
    internal async Task<DownloadedSubtitle?> GetSubtitleContentAsync(CachedSubtitleMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (_subtitleBinaryMemoryCache.TryGetValue(metadata.SubtitleId, out var memoryEntry) && !IsExpired(metadata.ExpiresAt))
        {
            return memoryEntry;
        }

        if (memoryEntry is not null)
        {
            _subtitleBinaryMemoryCache.TryRemove(metadata.SubtitleId, out _);
        }

        var filePath = SubtitleFilePath(metadata);
        if (!File.Exists(filePath))
        {
            return null;
        }

        if (IsExpired(metadata.ExpiresAt))
        {
            await DeleteFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var content = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var downloaded = new DownloadedSubtitle
        {
            FileName = metadata.Name,
            MediaType = GuessMediaType(metadata.Ext),
            Content = content
        };
        _subtitleBinaryMemoryCache[metadata.SubtitleId] = downloaded;
        return downloaded;
    }

    /// <summary>
    /// 写入字幕文件缓存。
    /// </summary>
    /// <param name="metadata">字幕下载元数据。</param>
    /// <param name="downloaded">字幕下载结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task SetSubtitleContentAsync(
        CachedSubtitleMetadata metadata,
        DownloadedSubtitle downloaded,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(downloaded);

        _subtitleBinaryMemoryCache[metadata.SubtitleId] = downloaded;
        await _diskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.WriteAllBytesAsync(SubtitleFilePath(metadata), downloaded.Content, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _diskLock.Release();
        }
    }

    /// <summary>
    /// 构造稳定的搜索缓存键。
    /// </summary>
    /// <param name="gcid">媒体文件 GCID。</param>
    /// <param name="cid">媒体文件 CID。</param>
    /// <param name="name">媒体文件名。</param>
    /// <returns>搜索缓存键。</returns>
    public string BuildSearchCacheKey(string? gcid, string? cid, string? name)
    {
        var payload = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["cid"] = cid ?? string.Empty,
            ["gcid"] = gcid ?? string.Empty,
            ["name"] = name ?? string.Empty
        };
        return JsonSerializer.Serialize(payload, JsonSerializerOptions);
    }

    /// <summary>
    /// 释放缓存写入锁。
    /// </summary>
    public void Dispose()
    {
        _diskLock.Dispose();
    }

    private static string GetDefaultCacheRootPath()
    {
        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            throw new InvalidOperationException("插件数据目录尚未初始化，无法创建字幕源缓存。");
        }

        return Path.Combine(dataFolderPath, "subtitle-source-cache");
    }

    private string SearchCachePath(string cacheKey)
    {
        return Path.Combine(SearchCacheDirectoryPath, $"{Sha1Hex(cacheKey)}.json");
    }

    private string SubtitleMetaCachePath(string subtitleId)
    {
        return Path.Combine(SubtitleMetaCacheDirectoryPath, $"{subtitleId}.json");
    }

    private string SubtitleFilePath(CachedSubtitleMetadata metadata)
    {
        var ext = metadata.Ext.Trim().TrimStart('.').ToLowerInvariant();
        return Path.Combine(SubtitleFileCacheDirectoryPath, $"{metadata.SubtitleId}.{ext}");
    }

    private async Task<T?> ReadJsonFileAsync<T>(string filePath, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"字幕源缓存文件无法解析：{filePath}", ex);
        }
    }

    private async Task WriteJsonFileAsync<T>(string filePath, T payload, CancellationToken cancellationToken)
        where T : class
    {
        await _diskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, payload, JsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _diskLock.Release();
        }
    }

    private async Task DeleteFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        await _diskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        finally
        {
            _diskLock.Release();
        }
    }

    private static bool IsExpired(DateTime expiresAt)
    {
        return expiresAt <= DateTime.UtcNow;
    }

    private static string GuessMediaType(string ext)
    {
        return ext.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "ass" => "text/x-ssa; charset=utf-8",
            "srt" => "application/x-subrip; charset=utf-8",
            "ssa" => "text/x-ssa; charset=utf-8",
            "vtt" => "text/vtt; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static string Sha1Hex(string value)
    {
        // 字幕源缓存键需要沿用稳定 SHA1 摘要；这里不是安全用途。
#pragma warning disable CA5350
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(value));
#pragma warning restore CA5350
        return Convert.ToHexString(hash);
    }
}
