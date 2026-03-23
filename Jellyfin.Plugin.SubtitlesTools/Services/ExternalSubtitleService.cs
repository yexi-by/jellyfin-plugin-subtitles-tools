using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责列举和写入媒体同目录外挂字幕。
/// </summary>
public sealed class ExternalSubtitleService
{
    private static readonly HashSet<string> SupportedSubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".ass",
        ".ssa",
        ".vtt"
    };

    private readonly SubtitleMetadataService _subtitleMetadataService;

    /// <summary>
    /// 初始化外挂字幕服务。
    /// </summary>
    /// <param name="subtitleMetadataService">字幕元数据辅助服务。</param>
    public ExternalSubtitleService(SubtitleMetadataService subtitleMetadataService)
    {
        _subtitleMetadataService = subtitleMetadataService;
    }

    /// <summary>
    /// 列举与当前媒体同目录且命名可被 Jellyfin 识别的外挂字幕。
    /// </summary>
    /// <param name="mediaFile">媒体文件。</param>
    /// <returns>外挂字幕列表。</returns>
    public List<ManagedExternalSubtitleDto> GetExternalSubtitles(FileInfo mediaFile)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);

        if (!mediaFile.Exists || mediaFile.Directory is null)
        {
            return [];
        }

        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
        return mediaFile.Directory
            .EnumerateFiles()
            .Where(file => IsMatchingSidecar(mediaBaseName, file))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => BuildExternalSubtitle(mediaFile, file))
            .ToList();
    }

    /// <summary>
    /// 把临时 SRT 移动为媒体同目录外挂字幕文件。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="temporarySrtFile">临时 SRT 文件。</param>
    /// <param name="language">语言代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入后的外挂字幕信息。</returns>
    public async Task<ManagedExternalSubtitleDto> ReplaceExternalSubtitleAsync(
        FileInfo mediaFile,
        FileInfo temporarySrtFile,
        string language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentNullException.ThrowIfNull(temporarySrtFile);

        cancellationToken.ThrowIfCancellationRequested();

        if (!mediaFile.Exists)
        {
            throw new FileNotFoundException("目标媒体文件不存在。", mediaFile.FullName);
        }

        if (!temporarySrtFile.Exists)
        {
            throw new FileNotFoundException("临时 SRT 文件不存在。", temporarySrtFile.FullName);
        }

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法写入外挂字幕。");
        }

        var targetPath = Path.Combine(
            mediaFile.Directory.FullName,
            BuildSuggestedSidecarFileName(mediaFile, language, "srt"));

        if (!string.Equals(temporarySrtFile.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(temporarySrtFile.FullName, targetPath, overwrite: true);
        }

        var targetFile = new FileInfo(targetPath);
        return await Task.FromResult(BuildExternalSubtitle(mediaFile, targetFile)).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建外挂字幕建议文件名。
    /// </summary>
    /// <param name="mediaFile">媒体文件。</param>
    /// <param name="language">语言代码。</param>
    /// <param name="format">字幕格式。</param>
    /// <returns>建议文件名。</returns>
    public string BuildSuggestedSidecarFileName(FileInfo mediaFile, string language, string format)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentNullException.ThrowIfNull(format);

        var normalizedFormat = _subtitleMetadataService.NormalizeFormat(format);
        var normalizedLanguage = _subtitleMetadataService.ResolveThreeLetterLanguage([], fallbackLanguage: language);
        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
        return string.Equals(normalizedLanguage, "und", StringComparison.Ordinal)
            ? $"{mediaBaseName}.{normalizedFormat}"
            : $"{mediaBaseName}.{normalizedLanguage}.{normalizedFormat}";
    }

    private ManagedExternalSubtitleDto BuildExternalSubtitle(FileInfo mediaFile, FileInfo subtitleFile)
    {
        var mediaBaseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
        return new ManagedExternalSubtitleDto
        {
            FileName = subtitleFile.Name,
            FilePath = subtitleFile.FullName,
            Format = _subtitleMetadataService.NormalizeFormat(subtitleFile.Extension),
            Language = _subtitleMetadataService.ResolveExistingSubtitleLanguage(mediaBaseName, subtitleFile.Name)
        };
    }

    private static bool IsMatchingSidecar(string mediaBaseName, FileInfo file)
    {
        if (!SupportedSubtitleExtensions.Contains(file.Extension))
        {
            return false;
        }

        var subtitleBaseName = Path.GetFileNameWithoutExtension(file.Name);
        return string.Equals(subtitleBaseName, mediaBaseName, StringComparison.OrdinalIgnoreCase)
            || subtitleBaseName.StartsWith($"{mediaBaseName}.", StringComparison.OrdinalIgnoreCase);
    }
}
