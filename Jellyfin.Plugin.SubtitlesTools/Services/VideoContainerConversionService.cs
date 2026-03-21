using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责把视频统一转换为 MKV，并在需要时把插件自定义元数据写回 MKV。
/// 优先尝试 remux，失败时自动退回到转码流程。
/// </summary>
public sealed class VideoContainerConversionService
{
    private readonly FfmpegProcessService _ffmpegProcessService;
    private readonly ILogger<VideoContainerConversionService> _logger;

    /// <summary>
    /// 初始化视频容器转换服务。
    /// </summary>
    public VideoContainerConversionService(
        FfmpegProcessService ffmpegProcessService,
        ILogger<VideoContainerConversionService> logger)
    {
        _ffmpegProcessService = ffmpegProcessService;
        _logger = logger;
    }

    /// <summary>
    /// 确保目标视频已经转成 MKV；若当前已是 MKV，则直接返回原路径。
    /// 如果调用方传入自定义元数据，则会在输出 MKV 中一并写入。
    /// </summary>
    public async Task<VideoConversionResult> EnsureMkvAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
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
                await RunRemuxAsync(sourceFile.FullName, tempOutputPath, metadata, traceId, cancellationToken).ConfigureAwait(false);
                return FinalizeConversion(sourceFile, tempOutputPath, targetPath, usedTranscodeFallback: false, "已通过 remux 输出为 MKV。");
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
                    await RunTranscodeAsync(sourceFile.FullName, tempOutputPath, keepInputSubtitleStreams: true, metadata, traceId, cancellationToken).ConfigureAwait(false);
                }
                catch (FfmpegExecutionException)
                {
                    DeleteIfExists(tempOutputPath);
                    await RunTranscodeAsync(sourceFile.FullName, tempOutputPath, keepInputSubtitleStreams: false, metadata, traceId, cancellationToken).ConfigureAwait(false);
                }

                return FinalizeConversion(sourceFile, tempOutputPath, targetPath, usedTranscodeFallback: true, "已自动转码并输出为 MKV。");
            }
        }
        finally
        {
            DeleteIfExists(tempOutputPath);
        }
    }

    /// <summary>
    /// 对现有 MKV 重新做一次无损 remux，并写入/覆盖插件自定义元数据。
    /// 该方法只用于已是 MKV 但尚未带插件标签的场景。
    /// </summary>
    public async Task WriteMetadataAsync(
        string mediaPath,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(metadata);

        var mediaFile = new FileInfo(mediaPath);
        if (!mediaFile.Exists)
        {
            throw new FileNotFoundException("待写入元数据的媒体文件不存在。", mediaPath);
        }

        if (!string.Equals(mediaFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只有 MKV 文件才能直接写入插件元数据。");
        }

        if (mediaFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法写入元数据。");
        }

        var tempOutputPath = Path.Combine(
            mediaFile.Directory.FullName,
            $"{Path.GetFileNameWithoutExtension(mediaFile.Name)}.subtitles-tools-metadata-{Guid.NewGuid():N}.tmp.mkv");

        try
        {
            var arguments = new List<string>
            {
                "-y",
                "-i",
                mediaFile.FullName,
                "-map",
                "0",
                "-map_metadata",
                "0",
                "-map_chapters",
                "0",
                "-c",
                "copy"
            };
            AppendMetadataArguments(arguments, metadata);
            arguments.Add(tempOutputPath);

            await _ffmpegProcessService.RunFfmpegAsync(arguments, traceId, "write_mkv_metadata", cancellationToken).ConfigureAwait(false);

            File.Delete(mediaFile.FullName);
            File.Move(tempOutputPath, mediaFile.FullName);
        }
        finally
        {
            DeleteIfExists(tempOutputPath);
        }
    }

    private async Task RunRemuxAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<string, string>? metadata,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-y",
            "-i",
            sourcePath,
            "-map",
            "0",
            "-map_metadata",
            "0",
            "-map_chapters",
            "0",
            "-c",
            "copy"
        };
        AppendMetadataArguments(arguments, metadata);
        arguments.Add(outputPath);

        await _ffmpegProcessService.RunFfmpegAsync(arguments, traceId, "convert_mkv_remux", cancellationToken).ConfigureAwait(false);
    }

    private async Task RunTranscodeAsync(
        string sourcePath,
        string outputPath,
        bool keepInputSubtitleStreams,
        IReadOnlyDictionary<string, string>? metadata,
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

        AppendMetadataArguments(arguments, metadata);
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
        bool usedTranscodeFallback,
        string message)
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
            Message = message
        };
    }

    private static void AppendMetadataArguments(List<string> arguments, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        foreach (var entry in metadata)
        {
            arguments.Add("-metadata");
            arguments.Add($"{entry.Key}={entry.Value}");
        }
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
    public string OutputPath { get; set; } = string.Empty;
    public string Container { get; set; } = "mkv";
    public bool UsedTranscodeFallback { get; set; }
    public string Message { get; set; } = string.Empty;
}
