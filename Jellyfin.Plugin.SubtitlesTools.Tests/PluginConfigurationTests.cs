using Jellyfin.Plugin.SubtitlesTools.Configuration;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验插件配置规范化逻辑。
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// 尾部斜杠会被移除，避免重复拼接路径。
    /// </summary>
    [Fact]
    public void NormalizeServiceBaseUrl_ShouldTrimTrailingSlash()
    {
        var value = PluginConfiguration.NormalizeServiceBaseUrl("http://127.0.0.1:8055/");

        Assert.Equal("http://127.0.0.1:8055", value);
    }

    /// <summary>
    /// 不支持的协议必须被拒绝。
    /// </summary>
    [Fact]
    public void NormalizeServiceBaseUrl_ShouldRejectUnsupportedScheme()
    {
        Assert.Throws<ArgumentException>(() => PluginConfiguration.NormalizeServiceBaseUrl("ftp://127.0.0.1:8055"));
    }

    /// <summary>
    /// 超时秒数会被限制在安全区间内。
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
    /// 哈希后台并发数应被限制在安全范围内，避免过高并发拖垮磁盘。
    /// </summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(999, 8)]
    public void NormalizeHashPrecomputeConcurrency_ShouldClampValue(int input, int expected)
    {
        var value = PluginConfiguration.NormalizeHashPrecomputeConcurrency(input);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// 视频转换并发数也应被限制在安全范围内，避免大文件 remux 和转码同时并发过高。
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
    /// FFmpeg 路径应去掉空白；若最终为空，则应回退为空字符串，表示交给自动探测。
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
    /// 默认配置应开启自动哈希和自动转 MKV，并把两类后台并发数都设为 1。
    /// </summary>
    [Fact]
    public void Constructor_ShouldUseExpectedDefaults()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.EnableAutoHashPrecompute);
        Assert.Equal(1, configuration.HashPrecomputeConcurrency);
        Assert.True(configuration.EnableAutoVideoConvertToMkv);
        Assert.Equal(1, configuration.VideoConvertConcurrency);
        Assert.Equal(string.Empty, configuration.FfmpegExecutablePath);
    }
}
