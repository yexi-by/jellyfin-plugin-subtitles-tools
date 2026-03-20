using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责扫描、命名、写入和删除媒体旁边的 sidecar 字幕文件。
/// 这里不再按语言做覆盖管理，而是尽量保留原字幕标题并允许多条字幕并存。
/// </summary>
public sealed class SidecarSubtitleService
{
    private const int MaxTitleLength = 80;

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".ass",
        ".ssa",
        ".vtt",
        ".sub"
    };

    private static readonly HashSet<char> InvalidFileNameCharacters =
    [
        '<',
        '>',
        ':',
        '"',
        '/',
        '\\',
        '|',
        '?',
        '*'
    ];

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
    /// <returns>按文件名排序后的现有字幕列表。</returns>
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
    /// 基于媒体文件和字幕原始名称生成目标字幕文件路径。
    /// 命名规则为“视频名.清洗后的原字幕名.扩展名”，若同名已存在则自动追加序号。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="subtitleName">字幕原始名称。</param>
    /// <param name="format">字幕格式。</param>
    /// <returns>计划写入的字幕文件。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "目标路径始终绑定在已验证存在的媒体文件目录下，字幕标题会经过非法字符清洗和长度限制。")]
    public FileInfo BuildTargetSubtitleFile(FileInfo mediaFile, string subtitleName, string format)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法生成目标字幕路径。");
        }

        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
        var normalizedFormat = _subtitleMetadataService.NormalizeFormat(format);
        var cleanedSubtitleTitle = CleanSubtitleTitle(subtitleName);
        var targetFileName = BuildUniqueSubtitleFileName(mediaFile.Directory, mediaBaseName, cleanedSubtitleTitle, normalizedFormat);
        return new FileInfo(Path.Combine(mediaFile.Directory.FullName, targetFileName));
    }

    /// <summary>
    /// 将字幕字节写入媒体旁边。
    /// 新规则不会删除已有字幕，而是为重名文件自动追加序号，保留所有已下载字幕。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="subtitleName">字幕原始名称。</param>
    /// <param name="format">字幕格式。</param>
    /// <param name="content">字幕原始字节。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最终写入的字幕文件。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "写入路径来自 BuildTargetSubtitleFile 的安全结果，且最终文件固定写入已验证媒体文件所在目录。")]
    public async Task<FileInfo> WriteSubtitleAsync(
        FileInfo mediaFile,
        string subtitleName,
        string format,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentNullException.ThrowIfNull(content);

        var targetFile = BuildTargetSubtitleFile(mediaFile, subtitleName, format);
        Directory.CreateDirectory(targetFile.DirectoryName!);
        await File.WriteAllBytesAsync(targetFile.FullName, content, cancellationToken).ConfigureAwait(false);
        return targetFile;
    }

    /// <summary>
    /// 删除某个媒体文件旁边的一条 sidecar 字幕。
    /// 这里只允许删除已经属于当前媒体的字幕文件，避免误删目录中的无关文件。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="subtitleFileName">字幕文件名。</param>
    /// <returns>被删除的字幕文件。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "删除目标必须先通过 GetExistingSubtitle 校验其属于当前媒体文件，最终路径不接受目录穿越。")]
    public FileInfo DeleteSubtitle(FileInfo mediaFile, string subtitleFileName)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitleFileName);

        var subtitle = GetExistingSubtitle(mediaFile, subtitleFileName);
        if (subtitle is null)
        {
            throw new FileNotFoundException("目标字幕文件不属于当前分段，无法删除。", subtitleFileName);
        }

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法删除字幕。");
        }

        var targetFile = new FileInfo(Path.Combine(mediaFile.Directory.FullName, subtitle.FileName));
        if (!targetFile.Exists)
        {
            throw new FileNotFoundException("目标字幕文件已不存在。", targetFile.FullName);
        }

        targetFile.Delete();
        return targetFile;
    }

    private static string CleanSubtitleTitle(string? subtitleName)
    {
        var originalTitle = Path.GetFileNameWithoutExtension(subtitleName?.Trim());
        if (string.IsNullOrWhiteSpace(originalTitle))
        {
            return "subtitle";
        }

        var builder = new StringBuilder(originalTitle.Length);
        foreach (var character in originalTitle)
        {
            if (char.IsControl(character) || InvalidFileNameCharacters.Contains(character))
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(character);
        }

        var cleanedTitle = string.Join(
            " ",
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        cleanedTitle = cleanedTitle.Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(cleanedTitle))
        {
            return "subtitle";
        }

        if (cleanedTitle.Length <= MaxTitleLength)
        {
            return cleanedTitle;
        }

        return cleanedTitle[..MaxTitleLength].Trim('.', ' ');
    }

    private static string BuildUniqueSubtitleFileName(
        DirectoryInfo directory,
        string mediaBaseName,
        string cleanedSubtitleTitle,
        string normalizedFormat)
    {
        var baseFileName = $"{mediaBaseName}.{cleanedSubtitleTitle}";
        var candidateFileName = $"{baseFileName}.{normalizedFormat}";
        if (!File.Exists(Path.Combine(directory.FullName, candidateFileName)))
        {
            return candidateFileName;
        }

        for (var index = 2; index <= 9999; index++)
        {
            candidateFileName = $"{baseFileName}.{index}.{normalizedFormat}";
            if (!File.Exists(Path.Combine(directory.FullName, candidateFileName)))
            {
                return candidateFileName;
            }
        }

        throw new IOException("同名字幕文件数量过多，无法继续生成唯一文件名。");
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
