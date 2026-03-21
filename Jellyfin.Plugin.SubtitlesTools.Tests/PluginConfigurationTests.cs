using System;
using Jellyfin.Plugin.SubtitlesTools.Configuration;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 覆盖插件配置中的规范化规则和默认值。
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// 服务地址应自动去掉尾部斜杠。
    /// </summary>
    [Fact]
    public void NormalizeServiceBaseUrl_ShouldTrimTrailingSlash()
    {
        var value = PluginConfiguration.NormalizeServiceBaseUrl("http://127.0.0.1:8055/");

        Assert.Equal("http://127.0.0.1:8055", value);
    }

    /// <summary>
    /// 只允许 http 和 https 协议。
    /// </summary>
    [Fact]
    public void NormalizeServiceBaseUrl_ShouldRejectUnsupportedScheme()
    {
        Assert.Throws<ArgumentException>(() => PluginConfiguration.NormalizeServiceBaseUrl("ftp://127.0.0.1:8055"));
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
    /// 视频转换并发数应被限制在 1 到 4 之间。
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
    /// 构造默认配置时，应使用自动纳管、并发 1、默认 QSV 渲染节点。
    /// </summary>
    [Fact]
    public void Constructor_ShouldUseExpectedDefaults()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.EnableAutoVideoConvertToMkv);
        Assert.Equal(1, configuration.VideoConvertConcurrency);
        Assert.Equal(string.Empty, configuration.FfmpegExecutablePath);
        Assert.Equal("/dev/dri/renderD128", configuration.QsvRenderDevicePath);
    }
}
