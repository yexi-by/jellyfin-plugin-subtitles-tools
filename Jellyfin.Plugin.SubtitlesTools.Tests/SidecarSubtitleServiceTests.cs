using Jellyfin.Plugin.SubtitlesTools.Services;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验 sidecar 字幕命名、冲突识别和覆盖写入逻辑。
/// </summary>
public sealed class SidecarSubtitleServiceTests
{
    private readonly SidecarSubtitleService _service = new(new SubtitleMetadataService());

    /// <summary>
    /// 目标字幕文件名应沿用媒体文件名，并附加三字母语言码和规范化扩展名。
    /// </summary>
    [Fact]
    public void BuildTargetSubtitleFile_ShouldUsePartFileNameAndNormalizedSuffix()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "movie-cd2.mkv"));

            var targetFile = _service.BuildTargetSubtitleFile(mediaFile, "zho", ".SRT");

            Assert.Equal("movie-cd2.zho.srt", targetFile.Name, ignoreCase: true);
            Assert.Equal(mediaFile.Directory!.FullName, targetFile.Directory!.FullName, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 同语言的现有字幕文件应被识别为覆盖冲突，不同语言文件不应混入。
    /// </summary>
    [Fact]
    public void FindConflictingSubtitleFiles_ShouldReturnOnlySameLanguageFiles()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "movie-cd2.mkv"));
            CreateSubtitleFile(tempDirectoryPath, "movie-cd2.zho.srt");
            CreateSubtitleFile(tempDirectoryPath, "movie-cd2.zho.ass");
            CreateSubtitleFile(tempDirectoryPath, "movie-cd2.eng.srt");

            var conflicts = _service.FindConflictingSubtitleFiles(mediaFile, "zho");

            Assert.Equal(2, conflicts.Count);
            Assert.All(conflicts, file => Assert.Contains(".zho.", file.Name, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 写入新字幕时，应删除冲突的旧字幕并在目标路径生成新文件。
    /// </summary>
    [Fact]
    public async Task WriteSubtitleAsync_ShouldReplaceConflictingFiles()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaFile = new FileInfo(CreateMediaFile(tempDirectoryPath, "movie-cd2.mkv"));
            var oldSubtitle = new FileInfo(CreateSubtitleFile(tempDirectoryPath, "movie-cd2.zho.ass"));
            var conflicts = _service.FindConflictingSubtitleFiles(mediaFile, "zho");

            var writtenFile = await _service.WriteSubtitleAsync(
                mediaFile,
                "zho",
                "srt",
                "1\n00:00:00,000 --> 00:00:01,000\nhello\n"u8.ToArray(),
                conflicts,
                CancellationToken.None);

            Assert.Equal("movie-cd2.zho.srt", writtenFile.Name, ignoreCase: true);
            Assert.True(writtenFile.Exists);
            Assert.False(oldSubtitle.Exists);
            Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\nhello\n", await File.ReadAllTextAsync(writtenFile.FullName));
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
