using System;
using Jellyfin.Plugin.SubtitlesTools.Configuration;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 覆盖插件配置中的规范化规则和默认值。
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// 迅雷字幕接口地址应自动去掉尾部斜杠。
    /// </summary>
    [Fact]
    public void NormalizeThunderBaseUrl_ShouldTrimTrailingSlash()
    {
        var value = PluginConfiguration.NormalizeThunderBaseUrl("https://subtitle.example/");

        Assert.Equal("https://subtitle.example", value);
    }

    /// <summary>
    /// 迅雷字幕接口地址只允许 http 和 https 协议。
    /// </summary>
    [Fact]
    public void NormalizeThunderBaseUrl_ShouldRejectUnsupportedScheme()
    {
        Assert.Throws<ArgumentException>(() => PluginConfiguration.NormalizeThunderBaseUrl("ftp://subtitle.example"));
    }

    /// <summary>
    /// 请求超时应被收敛到安全范围。
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
    /// 搜索缓存有效期应被限制在 60 秒到 7 天之间。
    /// </summary>
    [Theory]
    [InlineData(-1, 60)]
    [InlineData(0, 60)]
    [InlineData(60, 60)]
    [InlineData(3600, 3600)]
    [InlineData(9999999, 604800)]
    public void NormalizeSearchCacheTtlSeconds_ShouldClampValue(int input, int expected)
    {
        var value = PluginConfiguration.NormalizeSearchCacheTtlSeconds(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// 字幕缓存有效期应被限制在 60 秒到 30 天之间。
    /// </summary>
    [Theory]
    [InlineData(-1, 60)]
    [InlineData(0, 60)]
    [InlineData(60, 60)]
    [InlineData(3600, 3600)]
    [InlineData(9999999, 2592000)]
    public void NormalizeSubtitleCacheTtlSeconds_ShouldClampValue(int input, int expected)
    {
        var value = PluginConfiguration.NormalizeSubtitleCacheTtlSeconds(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// 同时转换数应被限制在 1 到 4 之间。
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
    /// FFmpeg 路径应在空白时归零，其余场景只做 trim。
    /// </summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  C:\\ffmpeg\\bin\\ffmpeg.exe  ", "C:\\ffmpeg\\bin\\ffmpeg.exe")]
    public void NormalizeFfmpegExecutablePath_ShouldTrimOrReturnEmptyString(string? input, string expected)
    {
        var value = PluginConfiguration.NormalizeFfmpegExecutablePath(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// QSV 设备路径允许留空，但会自动回落到默认渲染节点。
    /// </summary>
    [Theory]
    [InlineData(null, "/dev/dri/renderD128")]
    [InlineData("", "/dev/dri/renderD128")]
    [InlineData("   ", "/dev/dri/renderD128")]
    [InlineData("  /dev/dri/renderD129  ", "/dev/dri/renderD129")]
    public void NormalizeQsvRenderDevicePath_ShouldTrimOrUseDefault(string? input, string expected)
    {
        var value = PluginConfiguration.NormalizeQsvRenderDevicePath(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// 构造默认配置时，应使用内置迅雷字幕源、自动处理、同时转换数 1、默认 QSV 渲染节点。
    /// </summary>
    [Fact]
    public void Constructor_ShouldUseExpectedDefaults()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal("https://api-shoulei-ssl.xunlei.com", configuration.ThunderBaseUrl);
        Assert.Equal(86400, configuration.SearchCacheTtlSeconds);
        Assert.Equal(604800, configuration.SubtitleCacheTtlSeconds);
        Assert.True(configuration.EnableAutoVideoConvertToMkv);
        Assert.Equal(PluginConfiguration.DefaultSubtitleWriteModeValue, configuration.DefaultSubtitleWriteMode);
        Assert.Equal(1, configuration.VideoConvertConcurrency);
        Assert.Equal(string.Empty, configuration.FfmpegExecutablePath);
        Assert.Equal("/dev/dri/renderD128", configuration.QsvRenderDevicePath);
        Assert.Empty(configuration.AutoPreprocessPathBlacklist);
    }

    /// <summary>
    /// 默认字幕写入模式只允许内封或外挂；未知值回落到内封。
    /// </summary>
    [Theory]
    [InlineData(null, "embedded")]
    [InlineData("", "embedded")]
    [InlineData("embedded", "embedded")]
    [InlineData("sidecar", "sidecar")]
    [InlineData("SIDEcar", "sidecar")]
    [InlineData("unknown", "embedded")]
    public void NormalizeSubtitleWriteMode_ShouldClampToKnownValues(string? input, string expected)
    {
        var value = PluginConfiguration.NormalizeSubtitleWriteMode(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// 自动转换路径黑名单应去除空白项、尾部分隔符和重复目录。
    /// </summary>
    [Fact]
    public void NormalizeAutoPreprocessPathBlacklist_ShouldTrimAndDeduplicatePaths()
    {
        var value = PluginConfiguration.NormalizeAutoPreprocessPathBlacklist(
            [
                "  /media/archive/  ",
                "",
                " /media/archive ",
                " C:\\Media\\Skip\\ "
            ]);

        Assert.Equal(["/media/archive", "C:\\Media\\Skip"], value);
    }

    /// <summary>
    /// 媒体文件位于黑名单目录自身或子目录时，应判定为命中。
    /// </summary>
    [Theory]
    [InlineData("/media/archive/movie.mkv", true)]
    [InlineData("/media/archive/nested/movie.mkv", true)]
    [InlineData("/media/archive", true)]
    [InlineData("/media/archive-other/movie.mkv", false)]
    [InlineData("/media/other/movie.mkv", false)]
    public void IsAutoPreprocessPathBlacklisted_ShouldMatchDirectoryBoundary(string mediaPath, bool expected)
    {
        var value = PluginConfiguration.IsAutoPreprocessPathBlacklisted(
            mediaPath,
            ["/media/archive"]);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// 路径匹配应兼容 Windows 与通用斜杠写法。
    /// </summary>
    [Theory]
    [InlineData("C:\\Media\\Skip\\Movie.mkv", true)]
    [InlineData("C:/Media/Skip/Nested/Movie.mkv", true)]
    [InlineData("C:\\Media\\SkipSibling\\Movie.mkv", false)]
    public void IsAutoPreprocessPathBlacklisted_ShouldNormalizeSeparators(string mediaPath, bool expected)
    {
        var value = PluginConfiguration.IsAutoPreprocessPathBlacklisted(
            mediaPath,
            ["C:/Media/Skip"]);

        Assert.Equal(expected, value);
    }
}
