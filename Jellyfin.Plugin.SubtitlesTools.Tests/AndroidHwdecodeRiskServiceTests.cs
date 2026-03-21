using Jellyfin.Plugin.SubtitlesTools.Services;

namespace Jellyfin.Plugin.SubtitlesTools.Tests;

/// <summary>
/// 覆盖安卓硬解风险判定的关键分支，确保插件与独立转码工具的结论保持一致。
/// </summary>
public sealed class AndroidHwdecodeRiskServiceTests
{
    /// <summary>
    /// 现代 MKV + H.264 组合应被视为大概率可硬解。
    /// </summary>
    [Fact]
    public void Assess_ShouldMarkModernMkvAsLowRisk()
    {
        var result = AndroidHwdecodeRiskService.Assess(new ProbedMediaInfo
        {
            FormatName = "matroska,webm",
            Streams =
            [
                new ProbedMediaStream
                {
                    CodecType = "video",
                    CodecName = "h264",
                    Profile = "High",
                    PixelFormat = "yuv420p",
                    Width = 1280,
                    Height = 720
                },
                new ProbedMediaStream
                {
                    CodecType = "audio",
                    CodecName = "aac"
                }
            ]
        });

        Assert.Equal(AndroidHwdecodeRiskService.LowRiskVerdict, result.Verdict);
        Assert.False(result.NeedsCompatibilityRepair);
    }

    /// <summary>
    /// AVI + MPEG-4 ASP / XVID 应被直接判定为高风险。
    /// </summary>
    [Fact]
    public void Assess_ShouldMarkAviXvidAspAsHighRisk()
    {
        var result = AndroidHwdecodeRiskService.Assess(new ProbedMediaInfo
        {
            FormatName = "avi",
            Streams =
            [
                new ProbedMediaStream
                {
                    CodecType = "video",
                    CodecName = "mpeg4",
                    Profile = "Advanced Simple Profile",
                    CodecTagString = "XVID",
                    PixelFormat = "yuv420p",
                    Width = 728,
                    Height = 408
                },
                new ProbedMediaStream
                {
                    CodecType = "audio",
                    CodecName = "mp3"
                }
            ]
        });

        Assert.Equal(AndroidHwdecodeRiskService.HighRiskVerdict, result.Verdict);
        Assert.True(result.NeedsCompatibilityRepair);
    }

    /// <summary>
    /// WMV3 这类老编码应被视为高风险，即便已经放在 MKV 容器里也不能直接跳过。
    /// </summary>
    [Fact]
    public void Assess_ShouldMarkWmv3AsHighRisk()
    {
        var result = AndroidHwdecodeRiskService.Assess(new ProbedMediaInfo
        {
            FormatName = "matroska,webm",
            Streams =
            [
                new ProbedMediaStream
                {
                    CodecType = "video",
                    CodecName = "wmv3",
                    PixelFormat = "yuv420p",
                    Width = 640,
                    Height = 480
                }
            ]
        });

        Assert.Equal(AndroidHwdecodeRiskService.HighRiskVerdict, result.Verdict);
        Assert.True(result.NeedsCompatibilityRepair);
    }
}
