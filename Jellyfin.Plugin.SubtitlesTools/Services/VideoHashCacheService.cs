using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 将视频哈希结果缓存到插件数据目录，避免重复全文件扫描。
/// </summary>
public sealed class VideoHashCacheService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<VideoHashCacheService> _logger;
    private readonly Func<DirectoryInfo> _cacheDirectoryAccessor;

    /// <summary>
    /// 初始化哈希缓存服务。
    /// </summary>
    /// <param name="logger">日志器。</param>
    public VideoHashCacheService(ILogger<VideoHashCacheService> logger)
        : this(logger, GetDefaultCacheDirectory)
    {
    }

    /// <summary>
    /// 仅供测试时替换缓存目录。
    /// </summary>
    /// <param name="logger">日志器。</param>
    /// <param name="cacheDirectoryAccessor">缓存目录访问器。</param>
    internal VideoHashCacheService(
        ILogger<VideoHashCacheService> logger,
        Func<DirectoryInfo> cacheDirectoryAccessor)
    {
        _logger = logger;
        _cacheDirectoryAccessor = cacheDirectoryAccessor;
    }

    /// <summary>
    /// 读取指定文件的哈希缓存。
    /// </summary>
    /// <param name="mediaPath">媒体文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中时返回缓存值，否则返回 <see langword="null"/>。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "缓存键和读取目标都绑定到已存在的本地媒体文件；方法只在插件自己的缓存目录内读写 JSON 缓存文件。")]
    public async Task<VideoHashResult?> TryGetAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(mediaPath);
        if (!fileInfo.Exists)
        {
            return null;
        }

        var cachePath = GetCacheFilePath(fileInfo);
        if (!cachePath.Exists)
        {
            return null;
        }

        try
        {
            await using var stream = cachePath.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            var payload = await JsonSerializer.DeserializeAsync<VideoHashCacheEntry>(
                stream,
                JsonSerializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (payload is null)
            {
                return null;
            }

            if (!string.Equals(payload.MediaPath, fileInfo.FullName, StringComparison.OrdinalIgnoreCase)
                || payload.FileSize != fileInfo.Length
                || payload.LastWriteTimeUtcTicks != fileInfo.LastWriteTimeUtc.Ticks)
            {
                return null;
            }

            return new VideoHashResult
            {
                MediaPath = payload.MediaPath,
                FileSize = payload.FileSize,
                LastWriteTimeUtcTicks = payload.LastWriteTimeUtcTicks,
                Cid = payload.Cid,
                Gcid = payload.Gcid
            };
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取视频哈希缓存失败，将回退到重新计算。");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "视频哈希缓存文件已损坏，将回退到重新计算。");
            return null;
        }
    }

    /// <summary>
    /// 保存视频哈希结果到磁盘。
    /// </summary>
    /// <param name="hashResult">待缓存的哈希结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task SaveAsync(VideoHashResult hashResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashResult);

        var fileInfo = new FileInfo(hashResult.MediaPath);
        var cachePath = GetCacheFilePath(fileInfo);
        Directory.CreateDirectory(cachePath.DirectoryName!);

        var payload = new VideoHashCacheEntry
        {
            MediaPath = hashResult.MediaPath,
            FileSize = hashResult.FileSize,
            LastWriteTimeUtcTicks = hashResult.LastWriteTimeUtcTicks,
            Cid = hashResult.Cid,
            Gcid = hashResult.Gcid
        };

        await using var stream = cachePath.Open(FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, payload, JsonSerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static DirectoryInfo GetDefaultCacheDirectory()
    {
        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            throw new InvalidOperationException("插件数据目录尚未初始化，无法创建哈希缓存目录。");
        }

        return new DirectoryInfo(Path.Combine(dataFolderPath, "hash-cache"));
    }

    private FileInfo GetCacheFilePath(FileInfo fileInfo)
    {
        var cacheDirectory = _cacheDirectoryAccessor();
        Directory.CreateDirectory(cacheDirectory.FullName);
        var cacheKey = BuildCacheKey(fileInfo);
        return new FileInfo(Path.Combine(cacheDirectory.FullName, $"{cacheKey}.json"));
    }

    private static string BuildCacheKey(FileInfo fileInfo)
    {
        var rawKey = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
