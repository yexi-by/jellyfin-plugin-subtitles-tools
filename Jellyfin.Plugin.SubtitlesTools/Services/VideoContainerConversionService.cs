using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责把视频统一纳管为 MKV，并在需要时执行 Intel QSV 兼容修复重编码。
/// 当前版本只支持“无损 remux”与“软件解码 + QSV 编码”两条路径，不再回退 CPU 或 VAAPI。
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
    /// 把当前视频无损纳管为 MKV。
    /// 若当前已是 MKV，则只返回原路径，不做额外操作。
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
                Message = "当前视频已经是 MKV，无需重复转换。"
            };
        }

        var targetPath = BuildTargetPath(sourceFile, keepMkvExtension: false);
        var tempOutputPath = BuildTemporaryOutputPath(sourceFile, "remux");

        try
        {
            await RunRemuxAsync(sourceFile.FullName, tempOutputPath, metadata, traceId, cancellationToken).ConfigureAwait(false);
            return FinalizeConversion(sourceFile, tempOutputPath, targetPath, false, "已通过 remux 输出为 MKV。");
        }
        finally
        {
            DeleteIfExists(tempOutputPath);
        }
    }

    /// <summary>
    /// 使用 Intel QSV 把当前视频修复为更适合安卓硬解的 H.264 + AAC + MKV。
    /// 该流程固定使用软件解码，不做 CPU x264 或 VAAPI 兜底。
    /// </summary>
    public async Task<VideoConversionResult> EnsureCompatibilityAsync(
        string mediaPath,
        string qsvRenderDevicePath,
        CancellationToken cancellationToken,
        string? traceId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var sourceFile = new FileInfo(mediaPath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("待修复的视频文件不存在。", mediaPath);
        }

        var normalizedQsvPath = PluginConfiguration.NormalizeQsvRenderDevicePath(qsvRenderDevicePath);
        if (!OperatingSystem.IsWindows() && !File.Exists(normalizedQsvPath))
        {
            throw new FfmpegExecutionException($"未找到 Intel QSV 渲染设备：{normalizedQsvPath}");
        }

        var targetPath = BuildTargetPath(sourceFile, keepMkvExtension: true);
        var tempOutputPath = BuildTemporaryOutputPath(sourceFile, "compatibility");

        try
        {
            try
            {
                await RunQsvTranscodeAsync(
                        sourceFile.FullName,
                        tempOutputPath,
                        normalizedQsvPath,
                        keepInputSubtitleStreams: true,
                        metadata,
                        traceId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FfmpegExecutionException ex)
            {
                _logger.LogWarning(
                    ex,
                    "trace={TraceId} video_convert_qsv_keep_subtitles_failed media_path={MediaPath}，将尝试丢弃输入字幕流后继续兼容修复。",
                    traceId ?? "-",
                    sourceFile.FullName);
                DeleteIfExists(tempOutputPath);
                await RunQsvTranscodeAsync(
                        sourceFile.FullName,
                        tempOutputPath,
                        normalizedQsvPath,
                        keepInputSubtitleStreams: false,
                        metadata,
                        traceId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return FinalizeConversion(sourceFile, tempOutputPath, targetPath, true, "已通过 Intel QSV 重编码修复为兼容安卓硬解的 MKV。");
        }
        finally
        {
            DeleteIfExists(tempOutputPath);
        }
    }

    /// <summary>
    /// 对现有 MKV 重新做一次无损 remux，并写入或覆盖插件自定义元数据。
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

        var tempOutputPath = BuildTemporaryOutputPath(mediaFile, "metadata");
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
            ReplaceSourceWithOutput(mediaFile, tempOutputPath, mediaFile.FullName);
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

    private async Task RunQsvTranscodeAsync(
        string sourcePath,
        string outputPath,
        string qsvRenderDevicePath,
        bool keepInputSubtitleStreams,
        IReadOnlyDictionary<string, string>? metadata,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-y",
            "-qsv_device",
            qsvRenderDevicePath,
            "-i",
            sourcePath,
            "-map",
            "0:v:0",
            "-map",
            "0:a?",
            "-map_metadata",
            "0",
            "-map_chapters",
            "0",
            "-vf",
            "format=nv12,hwupload=extra_hw_frames=64",
            "-c:v",
            "h264_qsv",
            "-preset",
            "medium",
            "-global_quality",
            "18",
            "-look_ahead",
            "0",
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
            keepInputSubtitleStreams ? "convert_mkv_qsv_keep_subtitles" : "convert_mkv_qsv_drop_subtitles",
            cancellationToken).ConfigureAwait(false);
    }

    private static VideoConversionResult FinalizeConversion(
        FileInfo sourceFile,
        string tempOutputPath,
        string targetPath,
        bool usedCompatibilityRepairReencode,
        string message)
    {
        if (!File.Exists(tempOutputPath))
        {
            throw new IOException("FFmpeg 未生成目标 MKV 临时文件。");
        }

        ReplaceSourceWithOutput(sourceFile, tempOutputPath, targetPath);
        return new VideoConversionResult
        {
            OutputPath = targetPath,
            Container = "mkv",
            UsedCompatibilityRepairReencode = usedCompatibilityRepairReencode,
            Message = message
        };
    }

    private static void ReplaceSourceWithOutput(FileInfo sourceFile, string tempOutputPath, string targetPath)
    {
        if (File.Exists(targetPath) && !string.Equals(sourceFile.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"目标 MKV 文件已存在：{targetPath}");
        }

        File.Delete(sourceFile.FullName);
        File.Move(tempOutputPath, targetPath);
    }

    private static string BuildTargetPath(FileInfo sourceFile, bool keepMkvExtension)
    {
        if (sourceFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法执行转换。");
        }

        if (keepMkvExtension && string.Equals(sourceFile.Extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return sourceFile.FullName;
        }

        return Path.Combine(sourceFile.Directory.FullName, $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}.mkv");
    }

    private static string BuildTemporaryOutputPath(FileInfo sourceFile, string operationName)
    {
        if (sourceFile.Directory is null)
        {
            throw new InvalidOperationException("媒体文件所在目录不存在，无法生成临时输出路径。");
        }

        return Path.Combine(
            sourceFile.Directory.FullName,
            $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}.subtitles-tools-{operationName}-{Guid.NewGuid():N}.tmp.mkv");
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
    public bool UsedCompatibilityRepairReencode { get; set; }
    public string Message { get; set; } = string.Empty;
}
