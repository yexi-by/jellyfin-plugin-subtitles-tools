using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责把视频统一转换为 MKV。
/// 优先尝试 remux，失败时自动退回到转码流程。
/// </summary>
public sealed class VideoContainerConversionService
{
    private readonly FfmpegProcessService _ffmpegProcessService;
    private readonly ILogger<VideoContainerConversionService> _logger;

    /// <summary>
    /// 初始化视频容器转换服务。
    /// </summary>
    /// <param name="ffmpegProcessService">FFmpeg 进程服务。</param>
    /// <param name="logger">日志记录器。</param>
    public VideoContainerConversionService(
        FfmpegProcessService ffmpegProcessService,
        ILogger<VideoContainerConversionService> logger)
    {
        _ffmpegProcessService = ffmpegProcessService;
        _logger = logger;
    }

    /// <summary>
    /// 确保目标视频已经转成 MKV；若当前已是 MKV，则直接返回。
    /// </summary>
    /// <param name="mediaPath">当前媒体路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>转换结果。</returns>
    public async Task<VideoConversionResult> EnsureMkvAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        var sourceFile = new FileInfo(mediaPath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("待转换的视频文件不存在。", mediaPath);
        }

        if (string.Equals(sourceFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoConversionResult
            {
                OutputPath = sourceFile.FullName,
                Container = "mkv",
                UsedTranscodeFallback = false,
                Message = "当前视频已经是 MKV，无需重复转换。"
            };
        }

        if (sourceFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法执行转换。");
        }

        var targetPath = Path.Combine(sourceFile.Directory.FullName, $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}.mkv");
        if (File.Exists(targetPath))
        {
            throw new IOException($"目标 MKV 文件已存在：{targetPath}");
        }

        var tempOutputPath = Path.Combine(
            sourceFile.Directory.FullName,
            $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}.subtitles-tools-{Guid.NewGuid():N}.tmp.mkv");

        try
        {
            try
            {
                await RunRemuxAsync(sourceFile.FullName, tempOutputPath, traceId, cancellationToken).ConfigureAwait(false);
                return FinalizeConversion(sourceFile, tempOutputPath, targetPath, usedTranscodeFallback: false);
            }
            catch (FfmpegExecutionException ex)
            {
                _logger.LogWarning(
                    ex,
                    "trace={TraceId} video_convert_remux_failed media_path={MediaPath}，将尝试自动转码。",
                    traceId ?? "-",
                    sourceFile.FullName);
                DeleteIfExists(tempOutputPath);

                try
                {
                    await RunTranscodeAsync(
                        sourceFile.FullName,
                        tempOutputPath,
                        keepInputSubtitleStreams: true,
                        traceId,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (FfmpegExecutionException)
                {
                    DeleteIfExists(tempOutputPath);
                    await RunTranscodeAsync(
                        sourceFile.FullName,
                        tempOutputPath,
                        keepInputSubtitleStreams: false,
                        traceId,
                        cancellationToken).ConfigureAwait(false);
                }

                return FinalizeConversion(sourceFile, tempOutputPath, targetPath, usedTranscodeFallback: true);
            }
        }
        finally
        {
            DeleteIfExists(tempOutputPath);
        }
    }

    private async Task RunRemuxAsync(
        string sourcePath,
        string outputPath,
        string? traceId,
        CancellationToken cancellationToken)
    {
        await _ffmpegProcessService.RunFfmpegAsync(
            [
                "-y",
                "-i",
                sourcePath,
                "-map",
                "0",
                "-c",
                "copy",
                outputPath
            ],
            traceId,
            "convert_mkv_remux",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunTranscodeAsync(
        string sourcePath,
        string outputPath,
        bool keepInputSubtitleStreams,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-y",
            "-i",
            sourcePath,
            "-map",
            "0:v",
            "-map",
            "0:a?",
            "-map_metadata",
            "0",
            "-map_chapters",
            "0",
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-crf",
            "18",
            "-c:a",
            "aac",
            "-b:a",
            "192k"
        };

        if (keepInputSubtitleStreams)
        {
            arguments.AddRange(["-map", "0:s?", "-c:s", "copy"]);
        }
        else
        {
            arguments.Add("-sn");
        }

        arguments.Add(outputPath);

        await _ffmpegProcessService.RunFfmpegAsync(
            arguments,
            traceId,
            keepInputSubtitleStreams ? "convert_mkv_transcode_keep_subtitles" : "convert_mkv_transcode_drop_subtitles",
            cancellationToken).ConfigureAwait(false);
    }

    private static VideoConversionResult FinalizeConversion(
        FileInfo sourceFile,
        string tempOutputPath,
        string targetPath,
        bool usedTranscodeFallback)
    {
        if (!File.Exists(tempOutputPath))
        {
            throw new IOException("FFmpeg 未生成目标 MKV 临时文件。");
        }

        File.Delete(sourceFile.FullName);
        File.Move(tempOutputPath, targetPath);
        return new VideoConversionResult
        {
            OutputPath = targetPath,
            Container = "mkv",
            UsedTranscodeFallback = usedTranscodeFallback,
            Message = usedTranscodeFallback
                ? "已自动转码并输出为 MKV。"
                : "已通过 remux 输出为 MKV。"
        };
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// 表示视频转换结果。
/// </summary>
public sealed class VideoConversionResult
{
    /// <summary>
    /// 获取或设置输出路径。
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置输出容器格式。
    /// </summary>
    public string Container { get; set; } = "mkv";

    /// <summary>
    /// 获取或设置是否使用了自动转码回退。
    /// </summary>
    public bool UsedTranscodeFallback { get; set; }

    /// <summary>
    /// 获取或设置结果消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
