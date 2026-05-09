using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SubtitlesTools.Models;
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
    /// 默认字幕写入模式。
    /// </summary>
    public const string DefaultSubtitleWriteModeValue = SubtitleWriteMode.Embedded;

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
        DefaultSubtitleWriteMode = DefaultSubtitleWriteModeValue;
        FfmpegExecutablePath = string.Empty;
        QsvRenderDevicePath = DefaultQsvRenderDevicePath;
        AutoPreprocessPathBlacklist = [];
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
    /// 默认字幕写入模式。
    /// </summary>
    public string DefaultSubtitleWriteMode { get; set; }

    /// <summary>
    /// FFmpeg 可执行文件路径。
    /// </summary>
    public string FfmpegExecutablePath { get; set; }

    /// <summary>
    /// Intel QSV 渲染设备路径。
    /// </summary>
    public string QsvRenderDevicePath { get; set; }

    /// <summary>
    /// 新视频入库自动预处理的路径黑名单。
    /// </summary>
    public string[] AutoPreprocessPathBlacklist { get; set; }

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
    /// 规范化默认字幕写入模式。
    /// </summary>
    /// <param name="value">原始写入模式。</param>
    /// <returns>规范化后的模式。</returns>
    public static string NormalizeSubtitleWriteMode(string? value)
    {
        return SubtitleWriteMode.Normalize(value);
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

    /// <summary>
    /// 规范化自动预处理路径黑名单，去除空白项并按路径语义去重。
    /// </summary>
    /// <param name="values">用户配置的黑名单路径集合。</param>
    /// <returns>可持久化的黑名单路径集合。</returns>
    public static string[] NormalizeAutoPreprocessPathBlacklist(IEnumerable<string?>? values)
    {
        if (values is null)
        {
            return [];
        }

        var normalizedPaths = new List<string>();
        var seenComparisonPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var normalizedPath = NormalizeBlacklistPath(value);
            if (normalizedPath.Length == 0)
            {
                continue;
            }

            var comparisonPath = NormalizePathForComparison(normalizedPath);
            if (seenComparisonPaths.Add(comparisonPath))
            {
                normalizedPaths.Add(normalizedPath);
            }
        }

        return [.. normalizedPaths];
    }

    /// <summary>
    /// 判断媒体路径是否命中自动预处理路径黑名单。
    /// </summary>
    /// <param name="mediaPath">待检查的媒体文件路径。</param>
    /// <param name="blacklistPaths">用户配置的黑名单目录集合。</param>
    /// <returns>命中黑名单时返回 <c>true</c>。</returns>
    public static bool IsAutoPreprocessPathBlacklisted(string? mediaPath, IEnumerable<string?>? blacklistPaths)
    {
        return TryMatchAutoPreprocessBlacklistPath(mediaPath, blacklistPaths, out _);
    }

    /// <summary>
    /// 判断媒体路径是否命中自动预处理路径黑名单，并返回命中的黑名单目录。
    /// </summary>
    /// <param name="mediaPath">待检查的媒体文件路径。</param>
    /// <param name="blacklistPaths">用户配置的黑名单目录集合。</param>
    /// <param name="matchedBlacklistPath">命中的黑名单目录。</param>
    /// <returns>命中黑名单时返回 <c>true</c>。</returns>
    public static bool TryMatchAutoPreprocessBlacklistPath(
        string? mediaPath,
        IEnumerable<string?>? blacklistPaths,
        out string matchedBlacklistPath)
    {
        matchedBlacklistPath = string.Empty;

        var comparisonMediaPath = NormalizePathForComparison(mediaPath);
        if (comparisonMediaPath.Length == 0)
        {
            return false;
        }

        foreach (var blacklistPath in NormalizeAutoPreprocessPathBlacklist(blacklistPaths))
        {
            var comparisonBlacklistPath = NormalizePathForComparison(blacklistPath);
            if (IsSamePathOrChild(comparisonMediaPath, comparisonBlacklistPath))
            {
                matchedBlacklistPath = blacklistPath;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeBlacklistPath(string? value)
    {
        var normalizedValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return string.Empty;
        }

        normalizedValue = normalizedValue.Trim('"').Trim();
        return TrimTrailingPathSeparators(normalizedValue);
    }

    private static string NormalizePathForComparison(string? value)
    {
        var normalizedValue = NormalizeBlacklistPath(value);
        return normalizedValue.Replace('\\', '/');
    }

    private static string TrimTrailingPathSeparators(string value)
    {
        var normalizedValue = value;
        while (ShouldTrimTrailingPathSeparator(normalizedValue))
        {
            normalizedValue = normalizedValue[..^1];
        }

        return normalizedValue;
    }

    private static bool ShouldTrimTrailingPathSeparator(string value)
    {
        if (value.Length <= 1)
        {
            return false;
        }

        if (!IsPathSeparator(value[^1]))
        {
            return false;
        }

        return !IsWindowsDriveRoot(value);
    }

    private static bool IsSamePathOrChild(string mediaPath, string blacklistPath)
    {
        if (blacklistPath.Length == 0)
        {
            return false;
        }

        if (string.Equals(blacklistPath, "/", StringComparison.Ordinal))
        {
            return mediaPath.Length > 0 && mediaPath[0] == '/';
        }

        if (blacklistPath[^1] == '/')
        {
            return mediaPath.StartsWith(blacklistPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(mediaPath, blacklistPath, StringComparison.OrdinalIgnoreCase)
            || mediaPath.StartsWith($"{blacklistPath}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsDriveRoot(string value)
    {
        return value.Length == 3
            && char.IsLetter(value[0])
            && value[1] == ':'
            && IsPathSeparator(value[2]);
    }

    private static bool IsPathSeparator(char value)
    {
        return value is '/' or '\\';
    }
}
