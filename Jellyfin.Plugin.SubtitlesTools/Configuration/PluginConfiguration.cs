using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitlesTools.Configuration;

/// <summary>
/// ???????
/// ??????????????????? MKV??????????
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// ??? Python ?????
    /// </summary>
    public const string DefaultServiceBaseUrl = "http://127.0.0.1:8055";

    /// <summary>
    /// ??????????
    /// </summary>
    public const int DefaultRequestTimeoutSeconds = 10;

    /// <summary>
    /// ??????????????? MKV?
    /// </summary>
    public const bool DefaultEnableAutoVideoConvertToMkv = true;

    /// <summary>
    /// ???????????
    /// </summary>
    public const int DefaultVideoConvertConcurrency = 1;

    /// <summary>
    /// ?????????????????????????
    /// </summary>
    public PluginConfiguration()
    {
        ServiceBaseUrl = DefaultServiceBaseUrl;
        RequestTimeoutSeconds = DefaultRequestTimeoutSeconds;
        EnableAutoVideoConvertToMkv = DefaultEnableAutoVideoConvertToMkv;
        VideoConvertConcurrency = DefaultVideoConvertConcurrency;
        FfmpegExecutablePath = string.Empty;
    }

    /// <summary>
    /// ????? Python ??????
    /// </summary>
    public string ServiceBaseUrl { get; set; }

    /// <summary>
    /// ????????????
    /// </summary>
    public int RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// ???????????????????? MKV?
    /// </summary>
    public bool EnableAutoVideoConvertToMkv { get; set; }

    /// <summary>
    /// ????????????????
    /// </summary>
    public int VideoConvertConcurrency { get; set; }

    /// <summary>
    /// ????? FFmpeg ???????????
    /// </summary>
    public string FfmpegExecutablePath { get; set; }

    /// <summary>
    /// ??? Python ?????????????????????
    /// </summary>
    /// <param name="value">??????</param>
    /// <returns>???????????</returns>
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
    /// ???????????????????????
    /// </summary>
    /// <param name="value">???????</param>
    /// <returns>??????????</returns>
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
    /// ???????????????? remux ????????
    /// </summary>
    /// <param name="value">??????</param>
    /// <returns>?????????</returns>
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
    /// ??? FFmpeg ????????????????
    /// </summary>
    /// <param name="value">?????</param>
    /// <returns>????????</returns>
    public static string NormalizeFfmpegExecutablePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
