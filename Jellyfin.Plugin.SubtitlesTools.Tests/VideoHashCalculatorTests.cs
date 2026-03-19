using System.Text;
using Jellyfin.Plugin.SubtitlesTools.Services;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 校验 CID 与 GCID 计算结果。
/// </summary>
public sealed class VideoHashCalculatorTests
{
    private readonly VideoHashCalculator _calculator = new();

    /// <summary>
    /// 小文件应按整文件 SHA1 计算 CID，并正确得到 GCID。
    /// </summary>
    [Fact]
    public async Task ComputeAsync_ShouldReturnExpectedHashForSmallFile()
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");

        try
        {
            await File.WriteAllBytesAsync(tempFilePath, Encoding.UTF8.GetBytes("hello-subtitles-tools"));

            var result = await _calculator.ComputeAsync(tempFilePath, CancellationToken.None);

            Assert.Equal("E3908EE98B37C56925CB036554850D360AE2510D", result.Cid);
            Assert.Equal("DB1F8AB96B65E05022A6211DA4226D52F8E54B16", result.Gcid);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    /// <summary>
    /// 大文件应按三段采样计算 CID，并正确得到 GCID。
    /// </summary>
    [Fact]
    public async Task ComputeAsync_ShouldReturnExpectedHashForLargeFile()
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        var content = Enumerable.Range(0, 120000).Select(index => (byte)((index * 37) % 256)).ToArray();

        try
        {
            await File.WriteAllBytesAsync(tempFilePath, content);

            var result = await _calculator.ComputeAsync(tempFilePath, CancellationToken.None);

            Assert.Equal("587529BF8377CDAE7A469165BD6C730E1EE49848", result.Cid);
            Assert.Equal("4C656359008640B38F105C554773B18B2D3A13D9", result.Gcid);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }
}
