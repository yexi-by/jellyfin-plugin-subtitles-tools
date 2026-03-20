using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责把下载到的字幕统一转换为 UTF-8 SRT 临时文件。
/// 当前只接受文本型字幕；图片字幕和 OCR 流程不在本期支持范围内。
/// </summary>
public sealed class SubtitleSrtConversionService
{
    private static readonly HashSet<string> SupportedTextSubtitleFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "srt",
        "ass",
        "ssa",
        "vtt"
    };

    private readonly FfmpegProcessService _ffmpegProcessService;

    /// <summary>
    /// 初始化字幕 SRT 转换服务。
    /// </summary>
    /// <param name="ffmpegProcessService">FFmpeg 进程服务。</param>
    public SubtitleSrtConversionService(FfmpegProcessService ffmpegProcessService)
    {
        _ffmpegProcessService = ffmpegProcessService;
    }

    /// <summary>
    /// 将下载到的字幕转换为临时 SRT 文件。
    /// 输入文件统一写入插件数据目录下的临时目录，输出文件统一写到当前媒体文件旁边，
    /// 以便后续直接交给 FFmpeg 内封；内封完成后由调用方负责删除输出 SRT。
    /// </summary>
    /// <param name="mediaFile">目标媒体文件。</param>
    /// <param name="downloadedSubtitle">下载到的字幕内容。</param>
    /// <param name="sourceFormat">源字幕格式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>临时 SRT 文件。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "输入文件固定写入插件数据目录，输出文件名直接取当前媒体主文件名并固定为 .srt，不接受外部自由拼接目录。")]
    public async Task<FileInfo> ConvertToTemporarySrtAsync(
        FileInfo mediaFile,
        DownloadedSubtitle downloadedSubtitle,
        string sourceFormat,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        ArgumentNullException.ThrowIfNull(downloadedSubtitle);
        ArgumentNullException.ThrowIfNull(sourceFormat);

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法生成临时 SRT。");
        }

        var normalizedFormat = NormalizeFormat(sourceFormat, downloadedSubtitle.FileName);
        if (!SupportedTextSubtitleFormats.Contains(normalizedFormat))
        {
            throw new InvalidOperationException($"当前仅支持文本字幕转 SRT，暂不支持 {normalizedFormat}。");
        }

        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            throw new InvalidOperationException("插件数据目录尚未初始化。");
        }

        var tempDirectoryPath = Path.Combine(dataFolderPath, "temp-subtitle-conversion");
        Directory.CreateDirectory(tempDirectoryPath);

        var inputPath = Path.Combine(tempDirectoryPath, $"{Guid.NewGuid():N}.{normalizedFormat}");
        var outputPath = Path.Combine(mediaFile.Directory.FullName, $"{Path.GetFileNameWithoutExtension(mediaFile.Name)}.srt");

        try
        {
            await File.WriteAllBytesAsync(inputPath, downloadedSubtitle.Content, cancellationToken).ConfigureAwait(false);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            await _ffmpegProcessService.RunFfmpegAsync(
                [
                    "-y",
                    "-i",
                    inputPath,
                    outputPath
                ],
                traceId,
                "subtitle_to_srt",
                cancellationToken).ConfigureAwait(false);

            return new FileInfo(outputPath);
        }
        finally
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private static string NormalizeFormat(string sourceFormat, string downloadedFileName)
    {
        var trimmedFormat = sourceFormat.Trim().TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(trimmedFormat))
        {
            return trimmedFormat;
        }

        return Path.GetExtension(downloadedFileName).Trim().TrimStart('.').ToLowerInvariant();
    }
}
