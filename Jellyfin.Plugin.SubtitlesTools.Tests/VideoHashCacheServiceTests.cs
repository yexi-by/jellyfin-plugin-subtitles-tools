using Jellyfin.Plugin.SubtitlesTools.Models;
using Jellyfin.Plugin.SubtitlesTools.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验视频哈希磁盘缓存。
/// </summary>
public sealed class VideoHashCacheServiceTests
{
    /// <summary>
    /// 保存后再次读取应命中相同结果。
    /// </summary>
    [Fact]
    public async Task SaveAsync_ThenTryGetAsync_ShouldReturnCachedValue()
    {
        var tempDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mediaPath = Path.Combine(tempDirectoryPath, "movie.mkv");
        Directory.CreateDirectory(tempDirectoryPath);
        await File.WriteAllTextAsync(mediaPath, "demo", CancellationToken.None);
        var fileInfo = new FileInfo(mediaPath);

        try
        {
            var cacheService = new VideoHashCacheService(
                NullLogger<VideoHashCacheService>.Instance,
                () => new DirectoryInfo(Path.Combine(tempDirectoryPath, "cache")));

            var expected = new VideoHashResult
            {
                MediaPath = fileInfo.FullName,
                FileSize = fileInfo.Length,
                LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                Cid = "CID",
                Gcid = "GCID"
            };

            await cacheService.SaveAsync(expected, CancellationToken.None);
            var actual = await cacheService.TryGetAsync(mediaPath, CancellationToken.None);

            Assert.NotNull(actual);
            Assert.Equal(expected.Cid, actual!.Cid);
            Assert.Equal(expected.Gcid, actual.Gcid);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 文件元数据发生变化后应视为缓存失效。
    /// </summary>
    [Fact]
    public async Task TryGetAsync_ShouldReturnNullAfterFileChanges()
    {
        var tempDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mediaPath = Path.Combine(tempDirectoryPath, "episode.mkv");
        Directory.CreateDirectory(tempDirectoryPath);
        await File.WriteAllTextAsync(mediaPath, "demo", CancellationToken.None);
        var fileInfo = new FileInfo(mediaPath);

        try
        {
            var cacheService = new VideoHashCacheService(
                NullLogger<VideoHashCacheService>.Instance,
                () => new DirectoryInfo(Path.Combine(tempDirectoryPath, "cache")));

            await cacheService.SaveAsync(
                new VideoHashResult
                {
                    MediaPath = fileInfo.FullName,
                    FileSize = fileInfo.Length,
                    LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                    Cid = "CID",
                    Gcid = "GCID"
                },
                CancellationToken.None);

            await File.AppendAllTextAsync(mediaPath, "changed", CancellationToken.None);
            File.SetLastWriteTimeUtc(mediaPath, DateTime.UtcNow.AddMinutes(1));

            var actual = await cacheService.TryGetAsync(mediaPath, CancellationToken.None);

            Assert.Null(actual);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }
}
