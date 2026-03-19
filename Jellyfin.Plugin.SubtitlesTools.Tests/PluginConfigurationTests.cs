using Jellyfin.Plugin.SubtitlesTools.Configuration;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验插件配置规范化逻辑。
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// 尾斜杠会被移除，避免重复拼接路径。
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
    /// 预计算并发数应被限制在安全范围内，避免一次性拉高太多文件读取并发。
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
    /// 默认配置应关闭自动预计算，并把预计算并发数设为 1。
    /// </summary>
    [Fact]
    public void Constructor_ShouldUseExpectedPrecomputeDefaults()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.EnableAutoHashPrecompute);
        Assert.Equal(1, configuration.HashPrecomputeConcurrency);
    }
}
