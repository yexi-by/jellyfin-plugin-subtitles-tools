using Jellyfin.Plugin.SubtitlesTools.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验视频哈希解析服务能够复用缓存，避免同一文件重复全量扫描。
/// </summary>
public sealed class VideoHashResolverServiceTests
{
    /// <summary>
    /// 首次解析应触发实际计算，第二次解析应直接命中缓存。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ShouldUseCacheOnSecondCall()
    {
        var tempDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mediaPath = Path.Combine(tempDirectoryPath, "movie.mkv");
        Directory.CreateDirectory(tempDirectoryPath);
        await File.WriteAllTextAsync(mediaPath, "resolver-test-content", CancellationToken.None);

        try
        {
            var cacheService = new VideoHashCacheService(
                NullLogger<VideoHashCacheService>.Instance,
                () => new DirectoryInfo(Path.Combine(tempDirectoryPath, "cache")));
            var resolverService = new VideoHashResolverService(
                new VideoHashCalculator(),
                cacheService,
                NullLogger<VideoHashResolverService>.Instance);

            var firstResult = await resolverService.ResolveAsync(mediaPath, CancellationToken.None);
            var secondResult = await resolverService.ResolveAsync(mediaPath, CancellationToken.None);

            Assert.False(firstResult.CacheHit);
            Assert.True(secondResult.CacheHit);
            Assert.Equal(firstResult.HashResult.Cid, secondResult.HashResult.Cid);
            Assert.Equal(firstResult.HashResult.Gcid, secondResult.HashResult.Gcid);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }
}
