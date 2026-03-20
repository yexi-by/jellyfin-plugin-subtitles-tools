using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 统一处理字幕语言、格式和展示名称的解析逻辑，避免在不同流程里重复拼装规则。
/// </summary>
public sealed class SubtitleMetadataService
{
    private static readonly Dictionary<string, string> LanguageMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh"] = "zho",
        ["zh-cn"] = "zho",
        ["zh-hans"] = "zho",
        ["zh-tw"] = "zho",
        ["zh-hant"] = "zho",
        ["chs"] = "zho",
        ["cht"] = "zho",
        ["chi"] = "zho",
        ["en"] = "eng",
        ["eng"] = "eng",
        ["ja"] = "jpn",
        ["jpn"] = "jpn",
        ["ko"] = "kor",
        ["kor"] = "kor"
    };

    /// <summary>
    /// 构造前端展示用的字幕名称；额外说明不为空时会追加到主名称后方。
    /// </summary>
    /// <param name="item">服务端返回的字幕候选。</param>
    /// <returns>适合直接展示的字幕名称。</returns>
    public string BuildDisplayName(SubtitleSearchItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.ExtraName))
        {
            return item.Name;
        }

        return $"{item.Name} {item.ExtraName}";
    }

    /// <summary>
    /// 将上游返回的扩展名规范化为 Jellyfin 常见的字幕格式标识。
    /// </summary>
    /// <param name="ext">原始扩展名或文件后缀。</param>
    /// <returns>去掉前导点并转为小写后的格式名；为空时回退到 srt。</returns>
    public string NormalizeFormat(string? ext)
    {
        var normalized = ext?.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "srt" : normalized;
    }

    /// <summary>
    /// 将上游语言列表和可选回退语言解析为三字母语言码。
    /// </summary>
    /// <param name="languages">上游返回的语言列表。</param>
    /// <param name="fallbackLanguage">可选的主回退语言。</param>
    /// <param name="fallbackTwoLetterLanguage">可选的双字母回退语言。</param>
    /// <returns>解析成功时返回三字母语言码，否则返回 und。</returns>
    public string ResolveThreeLetterLanguage(
        IEnumerable<string> languages,
        string? fallbackLanguage = null,
        string? fallbackTwoLetterLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(languages);

        foreach (var language in languages)
        {
            var resolved = TryMapLanguage(language);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        var fallbackResolved = TryMapLanguage(fallbackLanguage);
        if (!string.IsNullOrWhiteSpace(fallbackResolved))
        {
            return fallbackResolved;
        }

        var twoLetterResolved = TryMapLanguage(fallbackTwoLetterLanguage);
        if (!string.IsNullOrWhiteSpace(twoLetterResolved))
        {
            return twoLetterResolved;
        }

        return "und";
    }

    /// <summary>
    /// 从现有 sidecar 字幕文件名中解析语言码。
    /// </summary>
    /// <param name="mediaBaseName">媒体文件去掉扩展名后的基名。</param>
    /// <param name="subtitleFileName">字幕文件名。</param>
    /// <returns>三字母语言码；无法解析时返回 und。</returns>
    public string ResolveExistingSubtitleLanguage(string mediaBaseName, string subtitleFileName)
    {
        if (string.IsNullOrWhiteSpace(mediaBaseName) || string.IsNullOrWhiteSpace(subtitleFileName))
        {
            return "und";
        }

        var subtitleBaseName = System.IO.Path.GetFileNameWithoutExtension(subtitleFileName);
        if (!subtitleBaseName.StartsWith($"{mediaBaseName}.", StringComparison.OrdinalIgnoreCase))
        {
            return "und";
        }

        var suffix = subtitleBaseName[(mediaBaseName.Length + 1)..];
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return "und";
        }

        var tokens = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return "und";
        }

        return TryMapLanguage(tokens[0]) ?? "und";
    }

    private static string? TryMapLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (LanguageMappings.TryGetValue(normalized, out var mappedLanguage))
        {
            return mappedLanguage;
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            var prefix = normalized[..dashIndex];
            if (LanguageMappings.TryGetValue(prefix, out mappedLanguage))
            {
                return mappedLanguage;
            }

            normalized = prefix;
        }

        if (normalized.Length == 3)
        {
            return normalized;
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized).ThreeLetterISOLanguageName.ToLowerInvariant();
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
