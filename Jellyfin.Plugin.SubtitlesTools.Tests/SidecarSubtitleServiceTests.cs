using Jellyfin.Plugin.SubtitlesTools.Services;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验 sidecar 字幕命名、并存写入和删除逻辑。
/// </summary>
public sealed class SidecarSubtitleServiceTests
{
    private readonly SidecarSubtitleService _service = new(new SubtitleMetadataService());

    /// <summary>
    /// 目标字幕文件名应沿用媒体文件名前缀，并附加清洗后的原字幕标题。
    /// </summary>
    [Fact]
    public void BuildTargetSubtitleFile_ShouldUseMediaPrefixAndCleanedOriginalTitle()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "GHNU-31.mp4"));

            var targetFile = _service.BuildTargetSubtitleFile(mediaFile, "网友上传:繁中?.srt", ".SRT");

            Assert.Equal("GHNU-31.网友上传 繁中.srt", targetFile.Name, ignoreCase: true);
            Assert.Equal(mediaFile.Directory!.FullName, targetFile.Directory!.FullName, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 若同名字幕已存在，应自动追加序号，避免覆盖旧字幕。
    /// </summary>
    [Fact]
    public void BuildTargetSubtitleFile_ShouldAppendSequenceWhenSameNameAlreadyExists()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "movie-cd2.mkv"));
            CreateSubtitleFile(tempDirectoryPath, "movie-cd2.网友上传.srt");

            var targetFile = _service.BuildTargetSubtitleFile(mediaFile, "网友上传.srt", "srt");

            Assert.Equal("movie-cd2.网友上传.2.srt", targetFile.Name, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 写入新字幕时，应保留旧字幕，并在需要时为新字幕生成唯一文件名。
    /// </summary>
    [Fact]
    public async Task WriteSubtitleAsync_ShouldKeepExistingSubtitlesAndWriteUniqueFile()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "movie-cd2.mkv"));
            var oldSubtitle = new FileInfo(CreateSubtitleFile(tempDirectoryPath, "movie-cd2.网友上传.srt"));

            var writtenFile = await _service.WriteSubtitleAsync(
                mediaFile,
                "网友上传.srt",
                "srt",
                "1\n00:00:00,000 --> 00:00:01,000\nhello\n"u8.ToArray(),
                CancellationToken.None);

            Assert.Equal("movie-cd2.网友上传.2.srt", writtenFile.Name, ignoreCase: true);
            Assert.True(writtenFile.Exists);
            Assert.True(oldSubtitle.Exists);
            Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\nhello\n", await File.ReadAllTextAsync(writtenFile.FullName));
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 删除已保存字幕时，应只删除指定文件，不影响同目录下的其他字幕。
    /// </summary>
    [Fact]
    public void DeleteSubtitle_ShouldRemoveOnlyRequestedSidecarFile()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "movie-cd2.mkv"));
            var keptSubtitle = new FileInfo(CreateSubtitleFile(tempDirectoryPath, "movie-cd2.字幕A.srt"));
            var deletedSubtitle = new FileInfo(CreateSubtitleFile(tempDirectoryPath, "movie-cd2.字幕B.srt"));

            var deletedFile = _service.DeleteSubtitle(mediaFile, "movie-cd2.字幕B.srt");

            Assert.Equal(deletedSubtitle.Name, deletedFile.Name, ignoreCase: true);
            Assert.True(keptSubtitle.Exists);
            Assert.False(deletedSubtitle.Exists);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectoryPath);
        return tempDirectoryPath;
    }

    private static string CreateMediaFile(string directoryPath, string fileName)
    {
        var mediaPath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(mediaPath, "demo");
        return mediaPath;
    }

    private static string CreateSubtitleFile(string directoryPath, string fileName)
    {
        var subtitlePath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(subtitlePath, "old subtitle");
        return subtitlePath;
    }
}
