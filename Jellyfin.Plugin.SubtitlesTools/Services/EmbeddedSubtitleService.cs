using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责查询、添加、删除和替换 MKV 内封字幕流。
/// </summary>
public sealed class EmbeddedSubtitleService
{
    private const string PluginTrackTitlePrefix = "SubtitlesTools|";
    private readonly FfmpegProcessService _ffmpegProcessService;
    private readonly SubtitleMetadataService _subtitleMetadataService;

    /// <summary>
    /// 初始化内封字幕服务。
    /// </summary>
    /// <param name="ffmpegProcessService">FFmpeg 进程服务。</param>
    /// <param name="subtitleMetadataService">字幕元数据服务。</param>
    public EmbeddedSubtitleService(
        FfmpegProcessService ffmpegProcessService,
        SubtitleMetadataService subtitleMetadataService)
    {
        _ffmpegProcessService = ffmpegProcessService;
        _subtitleMetadataService = subtitleMetadataService;
    }

    /// <summary>
    /// 读取当前媒体中的字幕流。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>字幕流列表。</returns>
    public async Task<List<ManagedEmbeddedSubtitleDto>> GetEmbeddedSubtitlesAsync(
        FileInfo mediaFile,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        if (!mediaFile.Exists || !string.Equals(mediaFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var streams = await _ffmpegProcessService
            .ProbeSubtitleStreamsAsync(mediaFile.FullName, cancellationToken, traceId)
            .ConfigureAwait(false);
        return streams
            .Select(stream => new ManagedEmbeddedSubtitleDto
            {
                StreamIndex = stream.StreamIndex,
                SubtitleStreamIndex = stream.SubtitleStreamIndex,
                Title = stream.Title,
                Language = _subtitleMetadataService.ResolveThreeLetterLanguage([], fallbackLanguage: stream.Language),
                Format = _subtitleMetadataService.NormalizeFormat(stream.Codec),
                IsPluginManaged = IsPluginManaged(stream.Title)
            })
            .ToList();
    }

    /// <summary>
    /// 用新下载的字幕替换当前分段中由插件写入的字幕流。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="temporarySrtFile">临时 SRT 文件。</param>
    /// <param name="candidateName">候选字幕名称。</param>
    /// <param name="language">三字母语言码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>新写入的字幕流信息。</returns>
    public async Task<ManagedEmbeddedSubtitleDto> ReplacePluginManagedSubtitleAsync(
        FileInfo mediaFile,
        FileInfo temporarySrtFile,
        string candidateName,
        string language,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentNullException.ThrowIfNull(temporarySrtFile);

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
            throw new InvalidOperationException("媒体文件所在目录不存在，无法内封字幕。");
        }

        var existingStreams = await GetEmbeddedSubtitlesAsync(mediaFile, cancellationToken, traceId).ConfigureAwait(false);
        var pluginManagedStreams = existingStreams
            .Where(stream => stream.IsPluginManaged)
            .OrderBy(stream => stream.StreamIndex)
            .ToList();

        var keptSubtitleCount = existingStreams.Count - pluginManagedStreams.Count;
        var newSubtitleStreamIndex = keptSubtitleCount;
        var trackTitle = BuildPluginTrackTitle(candidateName);
        var tempOutputPath = Path.Combine(
            mediaFile.Directory.FullName,
            $"{Path.GetFileNameWithoutExtension(mediaFile.Name)}.subtitles-tools-edit-{Guid.NewGuid():N}.tmp.mkv");

        try
        {
            var arguments = new List<string>
            {
                "-y",
                "-i",
                mediaFile.FullName,
                "-i",
                temporarySrtFile.FullName,
                "-map",
                "0"
            };

            foreach (var stream in pluginManagedStreams)
            {
                arguments.Add("-map");
                arguments.Add($"-0:{stream.StreamIndex}");
            }

            arguments.AddRange(
            [
                "-map",
                "1:0",
                "-c",
                "copy",
                $"-c:s:{newSubtitleStreamIndex}",
                "srt",
                $"-metadata:s:s:{newSubtitleStreamIndex}",
                $"title={trackTitle}",
                $"-metadata:s:s:{newSubtitleStreamIndex}",
                $"language={NormalizeLanguage(language)}",
                tempOutputPath
            ]);

            await _ffmpegProcessService.RunFfmpegAsync(
                arguments,
                traceId,
                "embed_subtitle_replace",
                cancellationToken).ConfigureAwait(false);

            File.Delete(mediaFile.FullName);
            File.Move(tempOutputPath, mediaFile.FullName);

            var refreshedStreams = await GetEmbeddedSubtitlesAsync(mediaFile, cancellationToken, traceId).ConfigureAwait(false);
            var createdStream = refreshedStreams
                .LastOrDefault(stream => stream.IsPluginManaged && string.Equals(stream.Title, trackTitle, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("内封字幕完成，但未能在输出文件中找到新字幕流。");
            return createdStream;
        }
        finally
        {
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }
        }
    }

    /// <summary>
    /// 删除当前媒体中由插件写入的一条字幕流。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="streamIndex">待删除的绝对流索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>异步任务。</returns>
    public async Task DeletePluginManagedSubtitleAsync(
        FileInfo mediaFile,
        int streamIndex,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);

        if (!mediaFile.Exists)
        {
            throw new FileNotFoundException("目标媒体文件不存在。", mediaFile.FullName);
        }

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法删除字幕流。");
        }

        var existingStreams = await GetEmbeddedSubtitlesAsync(mediaFile, cancellationToken, traceId).ConfigureAwait(false);
        var targetStream = existingStreams.FirstOrDefault(stream => stream.StreamIndex == streamIndex);
        if (targetStream is null)
        {
            throw new FileNotFoundException("未找到需要删除的字幕流。", mediaFile.FullName);
        }

        if (!targetStream.IsPluginManaged)
        {
            throw new InvalidOperationException("当前仅允许删除插件写入的字幕流。");
        }

        var tempOutputPath = Path.Combine(
            mediaFile.Directory.FullName,
            $"{Path.GetFileNameWithoutExtension(mediaFile.Name)}.subtitles-tools-delete-{Guid.NewGuid():N}.tmp.mkv");

        try
        {
            await _ffmpegProcessService.RunFfmpegAsync(
                [
                    "-y",
                    "-i",
                    mediaFile.FullName,
                    "-map",
                    "0",
                    "-map",
                    $"-0:{streamIndex}",
                    "-c",
                    "copy",
                    tempOutputPath
                ],
                traceId,
                "delete_embedded_subtitle",
                cancellationToken).ConfigureAwait(false);

            File.Delete(mediaFile.FullName);
            File.Move(tempOutputPath, mediaFile.FullName);
        }
        finally
        {
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }
        }
    }

    /// <summary>
    /// 构建用于标记插件写入字幕轨的标题。
    /// </summary>
    /// <param name="candidateName">原始字幕名称。</param>
    /// <returns>可识别的字幕轨标题。</returns>
    public string BuildPluginTrackTitle(string candidateName)
    {
        ArgumentNullException.ThrowIfNull(candidateName);

        var safeTitle = candidateName.Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = "subtitle";
        }

        return $"{PluginTrackTitlePrefix}{safeTitle}";
    }

    private static bool IsPluginManaged(string? title)
    {
        return !string.IsNullOrWhiteSpace(title)
            && title.StartsWith(PluginTrackTitlePrefix, StringComparison.Ordinal);
    }

    private static string NormalizeLanguage(string language)
    {
        return string.IsNullOrWhiteSpace(language)
            ? "und"
            : language.Trim().ToLowerInvariant();
    }
}
