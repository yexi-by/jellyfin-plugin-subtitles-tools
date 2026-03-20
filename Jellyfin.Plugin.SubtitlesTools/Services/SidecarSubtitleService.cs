using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责扫描、命名和落盘媒体旁边的 sidecar 字幕文件。
/// </summary>
public sealed class SidecarSubtitleService
{
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".ass",
        ".ssa",
        ".vtt",
        ".sub"
    };

    private readonly SubtitleMetadataService _subtitleMetadataService;

    /// <summary>
    /// 初始化 sidecar 字幕服务。
    /// </summary>
    /// <param name="subtitleMetadataService">字幕元数据解析服务。</param>
    public SidecarSubtitleService(SubtitleMetadataService subtitleMetadataService)
    {
        _subtitleMetadataService = subtitleMetadataService;
    }

    /// <summary>
    /// 列出某个媒体文件旁边已存在的 sidecar 字幕。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <returns>按文件名排序的现有字幕列表。</returns>
    public List<ExistingSubtitleDto> GetExistingSubtitles(FileInfo mediaFile)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);

        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
        return EnumerateExistingSubtitleFiles(mediaFile)
            .Select(file => new ExistingSubtitleDto
            {
                Id = file.Name,
                FileName = file.Name,
                Language = _subtitleMetadataService.ResolveExistingSubtitleLanguage(mediaBaseName, file.Name),
                Format = _subtitleMetadataService.NormalizeFormat(file.Extension)
            })
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 按文件名查找某个媒体旁边已存在的 sidecar 字幕。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="subtitleFileName">字幕文件名。</param>
    /// <returns>找到时返回字幕摘要，否则返回空。</returns>
    public ExistingSubtitleDto? GetExistingSubtitle(FileInfo mediaFile, string subtitleFileName)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitleFileName);

        return GetExistingSubtitles(mediaFile)
            .FirstOrDefault(item => string.Equals(item.FileName, subtitleFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 查找与目标语言冲突的现有 sidecar 字幕文件。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="language">三字母语言码。</param>
    /// <returns>同语言现有字幕文件列表。</returns>
    public List<FileInfo> FindConflictingSubtitleFiles(FileInfo mediaFile, string language)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);

        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "und" : language.Trim().ToLowerInvariant();
        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);

        return EnumerateExistingSubtitleFiles(mediaFile)
            .Where(file =>
                string.Equals(
                    _subtitleMetadataService.ResolveExistingSubtitleLanguage(mediaBaseName, file.Name),
                    normalizedLanguage,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 基于媒体文件、语言和格式生成目标字幕文件路径。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="language">三字母语言码。</param>
    /// <param name="format">字幕格式。</param>
    /// <returns>计划写入的字幕文件。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "目标路径始终绑定在已验证存在的媒体文件目录下，语言与扩展名会被收窄为仅包含 ASCII 字母数字的安全 token。")]
    public FileInfo BuildTargetSubtitleFile(FileInfo mediaFile, string language, string format)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法生成目标字幕路径。");
        }

        var normalizedLanguage = NormalizeSubtitleToken(language, "und", 8);
        var normalizedFormat = NormalizeSubtitleToken(_subtitleMetadataService.NormalizeFormat(format), "srt", 8);
        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
        var subtitleFileName = $"{mediaBaseName}.{normalizedLanguage}.{normalizedFormat}";
        return new FileInfo(Path.Combine(mediaFile.Directory.FullName, subtitleFileName));
    }

    /// <summary>
    /// 将字幕字节写入媒体旁边，并在必要时清理旧的同语言字幕。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="language">三字母语言码。</param>
    /// <param name="format">字幕格式。</param>
    /// <param name="content">字幕原始字节。</param>
    /// <param name="conflictingFiles">调用方已识别出的冲突文件列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最终写入的字幕文件。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "写入路径来自 BuildTargetSubtitleFile 的安全结果，且最终文件固定写入已验证媒体文件所在目录。")]
    public async Task<FileInfo> WriteSubtitleAsync(
        FileInfo mediaFile,
        string language,
        string format,
        byte[] content,
        IReadOnlyList<FileInfo> conflictingFiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(conflictingFiles);

        var targetFile = BuildTargetSubtitleFile(mediaFile, language, format);
        Directory.CreateDirectory(targetFile.DirectoryName!);

        foreach (var file in conflictingFiles)
        {
            if (!file.Exists)
            {
                continue;
            }

            if (string.Equals(file.FullName, targetFile.FullName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            file.Delete();
        }

        await File.WriteAllBytesAsync(targetFile.FullName, content, cancellationToken).ConfigureAwait(false);
        return targetFile;
    }

    private static string NormalizeSubtitleToken(string? rawValue, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        var normalized = new string(rawValue
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsAsciiLetterOrDigit)
            .Take(maxLength)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static IEnumerable<FileInfo> EnumerateExistingSubtitleFiles(FileInfo mediaFile)
    {
        if (mediaFile.Directory is null || !mediaFile.Directory.Exists)
        {
            return [];
        }

        var mediaBaseName = $"{Path.GetFileNameWithoutExtension(mediaFile.Name)}.";
        return mediaFile.Directory
            .EnumerateFiles()
            .Where(file =>
                file.Name.StartsWith(mediaBaseName, StringComparison.OrdinalIgnoreCase)
                && SubtitleExtensions.Contains(file.Extension)
                && !string.Equals(file.FullName, mediaFile.FullName, StringComparison.OrdinalIgnoreCase));
    }
}
