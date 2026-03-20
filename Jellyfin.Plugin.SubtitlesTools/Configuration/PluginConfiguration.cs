using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitlesTools.Configuration;

/// <summary>
/// 插件配置模型。
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// 默认的 Python 服务地址。
    /// </summary>
    public const string DefaultServiceBaseUrl = "http://127.0.0.1:8055";

    /// <summary>
    /// 默认的请求超时秒数。
    /// </summary>
    public const int DefaultRequestTimeoutSeconds = 10;

    /// <summary>
    /// 默认是否在媒体入库后自动预计算视频哈希。
    /// </summary>
    public const bool DefaultEnableAutoHashPrecompute = true;

    /// <summary>
    /// 默认的视频哈希预计算并发数。
    /// </summary>
    public const int DefaultHashPrecomputeConcurrency = 1;

    /// <summary>
    /// 默认是否在媒体入库后自动转换为 MKV。
    /// </summary>
    public const bool DefaultEnableAutoVideoConvertToMkv = true;

    /// <summary>
    /// 默认的视频转换并发数。
    /// </summary>
    public const int DefaultVideoConvertConcurrency = 1;

    /// <summary>
    /// 初始化一个插件配置实例，并写入可直接运行的默认值。
    /// </summary>
    public PluginConfiguration()
    {
        ServiceBaseUrl = DefaultServiceBaseUrl;
        RequestTimeoutSeconds = DefaultRequestTimeoutSeconds;
        EnableAutoHashPrecompute = DefaultEnableAutoHashPrecompute;
        HashPrecomputeConcurrency = DefaultHashPrecomputeConcurrency;
        EnableAutoVideoConvertToMkv = DefaultEnableAutoVideoConvertToMkv;
        VideoConvertConcurrency = DefaultVideoConvertConcurrency;
        FfmpegExecutablePath = string.Empty;
    }

    /// <summary>
    /// 获取或设置 Python 服务基地址。
    /// </summary>
    public string ServiceBaseUrl { get; set; }

    /// <summary>
    /// 获取或设置请求超时秒数。
    /// </summary>
    public int RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// 获取或设置是否在新增媒体入库后自动预计算视频哈希。
    /// </summary>
    public bool EnableAutoHashPrecompute { get; set; }

    /// <summary>
    /// 获取或设置视频哈希预计算的最大并发数。
    /// </summary>
    public int HashPrecomputeConcurrency { get; set; }

    /// <summary>
    /// 获取或设置是否在新增媒体入库后自动转换到 MKV。
    /// </summary>
    public bool EnableAutoVideoConvertToMkv { get; set; }

    /// <summary>
    /// 获取或设置视频转换的最大并发数。
    /// </summary>
    public int VideoConvertConcurrency { get; set; }

    /// <summary>
    /// 获取或设置 FFmpeg 可执行文件或目录路径。
    /// </summary>
    public string FfmpegExecutablePath { get; set; }

    /// <summary>
    /// 规范化 Python 服务基地址，确保协议合法且去掉多余尾斜杠。
    /// </summary>
    /// <param name="value">原始配置值。</param>
    /// <returns>规范化后的服务基地址。</returns>
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
    /// 规范化请求超时秒数，避免异常值影响调用稳定性。
    /// </summary>
    /// <param name="value">原始超时秒数。</param>
    /// <returns>规范化后的超时秒数。</returns>
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
    /// 规范化视频哈希预计算并发数，避免后台任务对磁盘造成过大压力。
    /// </summary>
    /// <param name="value">原始并发数。</param>
    /// <returns>规范化后的并发数。</returns>
    public static int NormalizeHashPrecomputeConcurrency(int value)
    {
        if (value < 1)
        {
            return 1;
        }

        if (value > 8)
        {
            return 8;
        }

        return value;
    }

    /// <summary>
    /// 规范化视频转换并发数，避免大文件转码对设备和磁盘造成过大压力。
    /// </summary>
    /// <param name="value">原始并发数。</param>
    /// <returns>规范化后的并发数。</returns>
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
    /// 规范化 FFmpeg 路径，确保空值以空字符串持久化。
    /// </summary>
    /// <param name="value">原始路径。</param>
    /// <returns>规范化后的路径。</returns>
    public static string NormalizeFfmpegExecutablePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
