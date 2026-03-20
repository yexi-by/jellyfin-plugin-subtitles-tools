using Jellyfin.Plugin.SubtitlesTools.Services;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验多分段媒体识别逻辑。
/// </summary>
public sealed class MultipartMediaParserServiceTests
{
    private readonly MultipartMediaParserService _parser = new();

    /// <summary>
    /// 同目录下符合 cd1/cd2/cd3 命名的文件应被识别为同一组分段；
    /// 即使其中某个分段已经提前转成 MKV，也不能因为扩展名不同而拆组。
    /// </summary>
    [Fact]
    public void Parse_ShouldGroupCdPartsInSameDirectory()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var cd1Path = CreateMediaFile(tempDirectoryPath, "movie-cd1.mkv");
            var cd2Path = CreateMediaFile(tempDirectoryPath, "movie-cd2.mp4");
            var cd3Path = CreateMediaFile(tempDirectoryPath, "movie-cd3.avi");
            CreateMediaFile(tempDirectoryPath, "movie-trailer.mp4");

            var group = _parser.Parse(cd2Path);

            Assert.Equal("movie", group.CanonicalBaseName);
            Assert.Equal(3, group.Parts.Count);
            Assert.Equal(
                group.Parts.Single(part => string.Equals(part.MediaFile.FullName, cd2Path, StringComparison.OrdinalIgnoreCase)).Id,
                group.CurrentPartId);
            Assert.Collection(
                group.Parts.OrderBy(part => part.Index),
                part =>
                {
                    Assert.Equal("CD1", part.Label);
                    Assert.Equal(cd1Path, part.MediaFile.FullName, ignoreCase: true);
                },
                part =>
                {
                    Assert.Equal("CD2", part.Label);
                    Assert.Equal(cd2Path, part.MediaFile.FullName, ignoreCase: true);
                },
                part =>
                {
                    Assert.Equal("CD3", part.Label);
                    Assert.Equal(cd3Path, part.MediaFile.FullName, ignoreCase: true);
                });
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 同目录下的 part1/part2 命名也应被识别为多分段媒体。
    /// </summary>
    [Fact]
    public void Parse_ShouldGroupPartTokens()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            CreateMediaFile(tempDirectoryPath, "demo.part1.mkv");
            var part2Path = CreateMediaFile(tempDirectoryPath, "demo.part2.mkv");

            var group = _parser.Parse(part2Path);

            Assert.Equal(2, group.Parts.Count);
            Assert.All(group.Parts, part => Assert.Equal("part", part.PartKind));
            Assert.Equal(
                group.Parts.Single(part => string.Equals(part.MediaFile.FullName, part2Path, StringComparison.OrdinalIgnoreCase)).Id,
                group.CurrentPartId);
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, recursive: true);
        }
    }

    /// <summary>
    /// 不满足分段命名或没有兄弟分段时，应回退为单文件媒体。
    /// </summary>
    [Fact]
    public void Parse_ShouldReturnSinglePartGroupWhenNotMultipart()
    {
        var tempDirectoryPath = CreateTempDirectory();

        try
        {
            var mediaPath = CreateMediaFile(tempDirectoryPath, "single-movie.mp4");

            var group = _parser.Parse(mediaPath);

            var singlePart = Assert.Single(group.Parts);
            Assert.Equal(singlePart.Id, group.CurrentPartId);
            Assert.Equal("single", singlePart.PartKind);
            Assert.Equal("单文件", singlePart.Label);
            Assert.Equal(mediaPath, singlePart.MediaFile.FullName, ignoreCase: true);
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
}
