using Jellyfin.Plugin.SubtitlesTools.Configuration;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// ????????????
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// ??????????????????
    /// </summary>
    [Fact]
    public void NormalizeServiceBaseUrl_ShouldTrimTrailingSlash()
    {
        var value = PluginConfiguration.NormalizeServiceBaseUrl("http://127.0.0.1:8055/");

        Assert.Equal("http://127.0.0.1:8055", value);
    }

    /// <summary>
    /// ????????????
    /// </summary>
    [Fact]
    public void NormalizeServiceBaseUrl_ShouldRejectUnsupportedScheme()
    {
        Assert.Throws<ArgumentException>(() => PluginConfiguration.NormalizeServiceBaseUrl("ftp://127.0.0.1:8055"));
    }

    /// <summary>
    /// ???????????????
    /// </summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(999, 120)]
    public void NormalizeTimeoutSeconds_ShouldClampValue(int input, int expected)
    {
        var value = PluginConfiguration.NormalizeTimeoutSeconds(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// ??????????????????????? remux ??????????
    /// </summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(999, 4)]
    public void NormalizeVideoConvertConcurrency_ShouldClampValue(int input, int expected)
    {
        var value = PluginConfiguration.NormalizeVideoConvertConcurrency(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// FFmpeg ?????????????????????????????????
    /// </summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  C:\ffmpeg\bin\ffmpeg.exe  ", "C:\ffmpeg\bin\ffmpeg.exe")]
    public void NormalizeFfmpegExecutablePath_ShouldTrimOrReturnEmptyString(string? input, string expected)
    {
        var value = PluginConfiguration.NormalizeFfmpegExecutablePath(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// ???????????? MKV???????????? 1?
    /// </summary>
    [Fact]
    public void Constructor_ShouldUseExpectedDefaults()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.EnableAutoVideoConvertToMkv);
        Assert.Equal(1, configuration.VideoConvertConcurrency);
        Assert.Equal(string.Empty, configuration.FfmpegExecutablePath);
    }
}
