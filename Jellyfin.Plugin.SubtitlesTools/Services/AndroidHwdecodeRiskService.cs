using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 统一评估安卓端（以 MX Player / MediaCodec 链路为代表）的硬解风险。
/// 当前规则与独立转码工具保持一致，只区分“高风险不能硬解 / 中风险待复核 / 大概率可硬解”。
/// </summary>
public sealed class AndroidHwdecodeRiskService
{
    public const string HighRiskVerdict = "高风险不能硬解";
    public const string MediumRiskVerdict = "中风险待复核";
    public const string LowRiskVerdict = "大概率可硬解";

    private static readonly HashSet<string> HighRiskContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "avi",
        "asf",
        "flv",
        "rm",
        "rmvb"
    };

    private static readonly HashSet<string> HighRiskVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "flv1",
        "h263",
        "indeo3",
        "indeo5",
        "mpeg4",
        "msmpeg4v1",
        "msmpeg4v2",
        "msmpeg4v3",
        "rv30",
        "rv40",
        "svq3",
        "theora",
        "vc1",
        "vp6",
        "vp6f",
        "wmv1",
        "wmv2",
        "wmv3"
    };

    private static readonly HashSet<string> MediumRiskVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "mpeg1video",
        "mpeg2video"
    };

    private static readonly HashSet<string> SupportedVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "av1",
        "h264",
        "hevc",
        "vp8",
        "vp9"
    };

    private static readonly HashSet<string> Modern10BitVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "av1",
        "hevc",
        "vp9"
    };

    private readonly FfmpegProcessService _ffmpegProcessService;

    /// <summary>
    /// 初始化安卓硬解风险判定服务。
    /// </summary>
    public AndroidHwdecodeRiskService(FfmpegProcessService ffmpegProcessService)
    {
        _ffmpegProcessService = ffmpegProcessService;
    }

    /// <summary>
    /// 探测当前媒体文件并给出硬解风险结论。
    /// </summary>
    public async Task<MediaCompatibilityAssessment> AssessAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        var probe = await _ffmpegProcessService
            .ProbeMediaAsync(mediaPath, cancellationToken, traceId)
            .ConfigureAwait(false);
        return Assess(probe);
    }

    /// <summary>
    /// 根据已探测到的媒体结构给出硬解风险结论。
    /// </summary>
    public static MediaCompatibilityAssessment Assess(ProbedMediaInfo probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var videoStream = probe.Streams.FirstOrDefault(stream => string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase));
        if (videoStream is null)
        {
            return new MediaCompatibilityAssessment
            {
                Verdict = HighRiskVerdict,
                NeedsCompatibilityRepair = true,
                Container = probe.FormatName,
                VideoCodec = string.Empty,
                AudioCodec = probe.Streams.FirstOrDefault(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))?.CodecName ?? string.Empty
            };
        }

        var containerNames = SplitFormatNames(probe.FormatName);
        var videoCodec = videoStream.CodecName ?? string.Empty;
        var videoProfile = videoStream.Profile ?? string.Empty;
        var videoTag = videoStream.CodecTagString ?? string.Empty;
        var pixelFormat = videoStream.PixelFormat ?? string.Empty;
        var riskLevel = 0;
        var hasOldFormatSignal = false;

        if (containerNames.Any(container => HighRiskContainers.Contains(container)))
        {
            riskLevel = Math.Max(riskLevel, 2);
        }

        if (HighRiskVideoCodecs.Contains(videoCodec))
        {
            hasOldFormatSignal = true;
            riskLevel = Math.Max(riskLevel, 2);
        }

        if (MediumRiskVideoCodecs.Contains(videoCodec))
        {
            hasOldFormatSignal = true;
            riskLevel = Math.Max(riskLevel, 1);
        }

        if (string.Equals(videoCodec, "mpeg4", StringComparison.OrdinalIgnoreCase)
            && (videoProfile.Contains("advanced simple", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoTag, "XVID", StringComparison.OrdinalIgnoreCase)))
        {
            hasOldFormatSignal = true;
            riskLevel = Math.Max(riskLevel, 2);
        }

        if (IsHighRiskPixelFormat(videoCodec, pixelFormat))
        {
            riskLevel = Math.Max(riskLevel, 2);
        }
        else if (IsMediumRiskPixelFormat(videoCodec, pixelFormat))
        {
            riskLevel = Math.Max(riskLevel, 1);
        }

        if (hasOldFormatSignal
            && videoStream.Width is not null
            && videoStream.Height is not null
            && (videoStream.Width.Value % 16 != 0 || videoStream.Height.Value % 16 != 0))
        {
            riskLevel = Math.Max(riskLevel, 1);
        }

        var verdict = riskLevel switch
        {
            >= 2 => HighRiskVerdict,
            1 => MediumRiskVerdict,
            _ => LowRiskVerdict
        };

        return new MediaCompatibilityAssessment
        {
            Verdict = verdict,
            NeedsCompatibilityRepair = string.Equals(verdict, HighRiskVerdict, StringComparison.Ordinal),
            Container = probe.FormatName,
            VideoCodec = videoCodec,
            AudioCodec = probe.Streams.FirstOrDefault(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))?.CodecName ?? string.Empty
        };
    }

    private static HashSet<string> SplitFormatNames(string formatName)
    {
        return formatName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsHighRiskPixelFormat(string videoCodec, string pixelFormat)
    {
        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            return false;
        }

        if (pixelFormat.Contains("422", StringComparison.OrdinalIgnoreCase)
            || pixelFormat.Contains("444", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase)
            && pixelFormat.Contains("10", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!SupportedVideoCodecs.Contains(videoCodec)
            && pixelFormat.Contains("10", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsMediumRiskPixelFormat(string videoCodec, string pixelFormat)
    {
        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            return false;
        }

        if (string.Equals(pixelFormat, "nv12", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pixelFormat, "p010le", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pixelFormat, "yuvj420p", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(pixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase))
        {
            return !Modern10BitVideoCodecs.Contains(videoCodec);
        }

        if (pixelFormat.Contains("420", StringComparison.OrdinalIgnoreCase)
            && pixelFormat.Contains("10", StringComparison.OrdinalIgnoreCase))
        {
            return !Modern10BitVideoCodecs.Contains(videoCodec);
        }

        return false;
    }
}

/// <summary>
/// 表示媒体当前的安卓硬解兼容性评估结果。
/// </summary>
public sealed class MediaCompatibilityAssessment
{
    public string Verdict { get; set; } = AndroidHwdecodeRiskService.LowRiskVerdict;
    public bool NeedsCompatibilityRepair { get; set; }
    public string Container { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
}
