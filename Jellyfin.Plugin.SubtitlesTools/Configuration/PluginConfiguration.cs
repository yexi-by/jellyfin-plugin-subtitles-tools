using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitlesTools.Configuration;

/// <summary>
/// 插件配置对象。
/// 当前版本默认把视频纳管到 MKV，并在命中高风险老编码时使用 Intel QSV 修复安卓硬解兼容性。
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// 默认 Python 服务地址。
    /// </summary>
    public const string DefaultServiceBaseUrl = "http://127.0.0.1:8055";

    /// <summary>
    /// 默认请求超时秒数。
    /// </summary>
    public const int DefaultRequestTimeoutSeconds = 10;

    /// <summary>
    /// 默认开启“新视频入库后自动纳管并修复安卓硬解兼容”。
    /// </summary>
    public const bool DefaultEnableAutoVideoConvertToMkv = true;

    /// <summary>
    /// 默认视频转换并发数。
    /// </summary>
    public const int DefaultVideoConvertConcurrency = 1;

    /// <summary>
    /// 默认 QSV 渲染设备路径。
    /// </summary>
    public const string DefaultQsvRenderDevicePath = "/dev/dri/renderD128";

    /// <summary>
    /// 初始化默认配置。
    /// </summary>
    public PluginConfiguration()
    {
        ServiceBaseUrl = DefaultServiceBaseUrl;
        RequestTimeoutSeconds = DefaultRequestTimeoutSeconds;
        EnableAutoVideoConvertToMkv = DefaultEnableAutoVideoConvertToMkv;
        VideoConvertConcurrency = DefaultVideoConvertConcurrency;
        FfmpegExecutablePath = string.Empty;
        QsvRenderDevicePath = DefaultQsvRenderDevicePath;
    }

    /// <summary>
    /// Python 服务地址。
    /// </summary>
    public string ServiceBaseUrl { get; set; }

    /// <summary>
    /// 请求超时秒数。
    /// </summary>
    public int RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// 是否在新视频入库后自动纳管并修复安卓硬解兼容。
    /// </summary>
    public bool EnableAutoVideoConvertToMkv { get; set; }

    /// <summary>
    /// 视频转换并发数。
    /// </summary>
    public int VideoConvertConcurrency { get; set; }

    /// <summary>
    /// FFmpeg 可执行文件路径。
    /// </summary>
    public string FfmpegExecutablePath { get; set; }

    /// <summary>
    /// Intel QSV 渲染设备路径。
    /// </summary>
    public string QsvRenderDevicePath { get; set; }

    /// <summary>
    /// 规范化 Python 服务地址。
    /// </summary>
    public static string NormalizeServiceBaseUrl(string? value)
    {
        var rawValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return DefaultServiceBaseUrl;
        }

        if (!Uri.TryCreate(rawValue, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("服务地址不是合法的绝对 URL。", nameof(value));
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("服务地址只支持 http 或 https 协议。", nameof(value));
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    /// <summary>
    /// 把请求超时收敛到安全范围。
    /// </summary>
    public static int NormalizeTimeoutSeconds(int value)
    {
        if (value < 1)
        {
            return 1;
        }

        if (value > 120)
        {
            return 120;
        }

        return value;
    }

    /// <summary>
    /// 限制视频转换并发，避免 NAS 过载。
    /// </summary>
    public static int NormalizeVideoConvertConcurrency(int value)
    {
        if (value < 1)
        {
            return 1;
        }

        if (value > 4)
        {
            return 4;
        }

        return value;
    }

    /// <summary>
    /// 规范化 FFmpeg 可执行文件路径。
    /// </summary>
    public static string NormalizeFfmpegExecutablePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// 规范化 Intel QSV 渲染设备路径。
    /// </summary>
    public static string NormalizeQsvRenderDevicePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultQsvRenderDevicePath : value.Trim();
    }
}
