using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Services;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 覆盖外挂字幕文件命名、枚举和写入规则。
/// </summary>
public sealed class ExternalSubtitleServiceTests
{
    /// <summary>
    /// 建议文件名应优先使用三字母语言码，未知语言则回落为不带语言后缀。
    /// </summary>
    [Fact]
    public void BuildSuggestedSidecarFileName_ShouldUseNormalizedLanguageSuffix()
    {
        var service = new ExternalSubtitleService(new SubtitleMetadataService());
        var mediaFile = new FileInfo(Path.Combine(Path.GetTempPath(), "Movie.mkv"));

        var chineseFileName = service.BuildSuggestedSidecarFileName(mediaFile, "zh-CN", "srt");
        var unknownFileName = service.BuildSuggestedSidecarFileName(mediaFile, "unknown", "srt");

        Assert.Equal("Movie.zho.srt", chineseFileName);
        Assert.Equal("Movie.srt", unknownFileName);
    }

    /// <summary>
    /// 只应返回与媒体同目录、同基名且扩展名受支持的外挂字幕。
    /// </summary>
    [Fact]
    public void GetExternalSubtitles_ShouldOnlyReturnMatchingFiles()
    {
        var service = new ExternalSubtitleService(new SubtitleMetadataService());
        using var scope = new TemporaryDirectoryScope();
        var mediaFilePath = Path.Combine(scope.DirectoryPath, "Movie.mkv");
        var zhSubtitlePath = Path.Combine(scope.DirectoryPath, "Movie.zho.srt");
        var enSubtitlePath = Path.Combine(scope.DirectoryPath, "Movie.eng.ass");
        var otherSubtitlePath = Path.Combine(scope.DirectoryPath, "AnotherMovie.srt");

        File.WriteAllText(mediaFilePath, "media");
        File.WriteAllText(zhSubtitlePath, "zh");
        File.WriteAllText(enSubtitlePath, "en");
        File.WriteAllText(otherSubtitlePath, "other");

        var subtitles = service.GetExternalSubtitles(new FileInfo(mediaFilePath));

        Assert.Equal(2, subtitles.Count);
        Assert.Equal(["Movie.eng.ass", "Movie.zho.srt"], subtitles.Select(item => item.FileName).ToArray());
        Assert.Equal(["eng", "zho"], subtitles.Select(item => item.Language).ToArray());
    }

    /// <summary>
    /// 写入外挂字幕时应覆盖目标文件，并把临时 SRT 移动到规范命名的位置。
    /// </summary>
    [Fact]
    public async Task ReplaceExternalSubtitleAsync_ShouldOverwriteExistingTarget()
    {
        var service = new ExternalSubtitleService(new SubtitleMetadataService());
        using var scope = new TemporaryDirectoryScope();
        var mediaFilePath = Path.Combine(scope.DirectoryPath, "Movie.mkv");
        var tempSubtitlePath = Path.Combine(scope.DirectoryPath, "downloaded.srt");
        var targetSubtitlePath = Path.Combine(scope.DirectoryPath, "Movie.zho.srt");

        File.WriteAllText(mediaFilePath, "media");
        File.WriteAllText(tempSubtitlePath, "new subtitle");
        File.WriteAllText(targetSubtitlePath, "old subtitle");

        var result = await service.ReplaceExternalSubtitleAsync(
            new FileInfo(mediaFilePath),
            new FileInfo(tempSubtitlePath),
            "zh-CN",
            CancellationToken.None);

        Assert.Equal("Movie.zho.srt", result.FileName);
        Assert.Equal(targetSubtitlePath, result.FilePath);
        Assert.Equal("new subtitle", File.ReadAllText(targetSubtitlePath));
        Assert.False(File.Exists(tempSubtitlePath));
    }

    /// <summary>
    /// 提供最小的临时目录作用域，确保测试结束后清理文件。
    /// </summary>
    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
