using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 统一封装 FFmpeg 与 FFprobe 的定位、执行和探测逻辑。
/// </summary>
public sealed class FfmpegProcessService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<FfmpegProcessService> _logger;

    /// <summary>
    /// 初始化 FFmpeg 进程服务。
    /// </summary>
    /// <param name="applicationPaths">Jellyfin 应用路径服务。</param>
    /// <param name="logger">日志记录器。</param>
    public FfmpegProcessService(
        IApplicationPaths applicationPaths,
        ILogger<FfmpegProcessService> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>
    /// 读取 FFprobe 中的字幕流信息。
    /// </summary>
    /// <param name="mediaPath">目标媒体路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <returns>字幕流信息列表。</returns>
    public async Task<List<ProbedSubtitleTrack>> ProbeSubtitleStreamsAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var result = await RunFfprobeAsync(
            [
                "-v",
                "error",
                "-show_streams",
                "-print_format",
                "json",
                mediaPath
            ],
            traceId,
            "probe_streams",
            cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Deserialize<FfprobePayload>(result.StandardOutput, JsonSerializerOptions);
        if (payload?.Streams is null)
        {
            return [];
        }

        return payload.Streams
            .Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.Index)
            .Select((stream, subtitleStreamIndex) => new ProbedSubtitleTrack
            {
                StreamIndex = stream.Index,
                SubtitleStreamIndex = subtitleStreamIndex,
                Codec = stream.CodecName ?? "unknown",
                Language = string.IsNullOrWhiteSpace(stream.Tags?.Language)
                    ? "und"
                    : stream.Tags.Language!.Trim().ToLowerInvariant(),
                Title = stream.Tags?.Title?.Trim() ?? string.Empty
            })
            .ToList();
    }

    /// <summary>
    /// 执行 FFmpeg 命令。
    /// </summary>
    /// <param name="arguments">参数列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">可选链路追踪标识。</param>
    /// <param name="operationName">操作名称。</param>
    /// <returns>执行结果。</returns>
    public Task<FfmpegCommandResult> RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        string? traceId,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunProcessAsync(
            ResolveExecutablePath(isProbe: false),
            arguments,
            traceId,
            operationName,
            cancellationToken);
    }

    /// <summary>
    /// 验证当前配置下是否能够解析 FFmpeg 与 FFprobe。
    /// </summary>
    /// <returns>验证结果。</returns>
    public (string FfmpegPath, string FfprobePath) ValidateExecutables()
    {
        return (ResolveExecutablePath(isProbe: false), ResolveExecutablePath(isProbe: true));
    }

    private Task<FfmpegCommandResult> RunFfprobeAsync(
        IReadOnlyList<string> arguments,
        string? traceId,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunProcessAsync(
            ResolveExecutablePath(isProbe: true),
            arguments,
            traceId,
            operationName,
            cancellationToken);
    }

    private async Task<FfmpegCommandResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? traceId,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = processStartInfo
        };

        var normalizedTraceId = string.IsNullOrWhiteSpace(traceId) ? "-" : traceId;
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "trace={TraceId} ffmpeg_process_start operation={Operation} executable={Executable} args={Arguments}",
            normalizedTraceId,
            operationName,
            executablePath,
            string.Join(' ', arguments));

        try
        {
            if (!process.Start())
            {
                throw new FfmpegExecutionException("无法启动 FFmpeg 进程。");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            stopwatch.Stop();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "trace={TraceId} ffmpeg_process_failed operation={Operation} executable={Executable} exit_code={ExitCode} total_ms={ElapsedMs:F2} stderr={StandardError}",
                    normalizedTraceId,
                    operationName,
                    executablePath,
                    process.ExitCode,
                    stopwatch.Elapsed.TotalMilliseconds,
                    string.IsNullOrWhiteSpace(standardError) ? "[empty]" : standardError);
                throw new FfmpegExecutionException(
                    $"FFmpeg 操作失败：{operationName}，退出码 {process.ExitCode}。{standardError}");
            }

            _logger.LogInformation(
                "trace={TraceId} ffmpeg_process_complete operation={Operation} executable={Executable} total_ms={ElapsedMs:F2}",
                normalizedTraceId,
                operationName,
                executablePath,
                stopwatch.Elapsed.TotalMilliseconds);

            return new FfmpegCommandResult
            {
                StandardOutput = standardOutput,
                StandardError = standardError
            };
        }
        catch (Win32Exception ex)
        {
            throw new FfmpegExecutionException(
                $"无法启动可执行文件 {executablePath}，请检查 FFmpeg 路径配置。",
                ex);
        }
    }

    private string ResolveExecutablePath(bool isProbe)
    {
        var configuredPath = Plugin.Instance?.Configuration?.FfmpegExecutablePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return ResolveFromConfiguredPath(configuredPath, isProbe);
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_applicationPaths.ProgramSystemPath))
        {
            var programSystemDirectory = new FileInfo(_applicationPaths.ProgramSystemPath).Directory;
            if (programSystemDirectory is not null)
            {
                candidates.Add(Path.Combine(programSystemDirectory.FullName, isProbe ? "ffprobe.exe" : "ffmpeg.exe"));
                candidates.Add(Path.Combine(programSystemDirectory.FullName, "ffmpeg", isProbe ? "ffprobe.exe" : "ffmpeg.exe"));
            }
        }

        candidates.AddRange(TryResolveFromPath(isProbe ? "ffprobe" : "ffmpeg"));

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return isProbe ? "ffprobe" : "ffmpeg";
    }

    private static string ResolveFromConfiguredPath(string configuredPath, bool isProbe)
    {
        var fullPath = Path.GetFullPath(configuredPath);
        if (Directory.Exists(fullPath))
        {
            var executablePath = Path.Combine(fullPath, isProbe ? "ffprobe.exe" : "ffmpeg.exe");
            if (File.Exists(executablePath))
            {
                return executablePath;
            }

            throw new FileNotFoundException("配置的 FFmpeg 目录中未找到可执行文件。", executablePath);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("配置的 FFmpeg 可执行文件不存在。", fullPath);
        }

        if (!isProbe)
        {
            return fullPath;
        }

        var siblingProbe = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? string.Empty,
            "ffprobe" + Path.GetExtension(fullPath));
        if (File.Exists(siblingProbe))
        {
            return siblingProbe;
        }

        throw new FileNotFoundException("未在 FFmpeg 同目录找到 FFprobe。", siblingProbe);
    }

    private static string[] TryResolveFromPath(string executableName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "where",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(executableName);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return [];
            }

            return output
                .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private sealed class FfprobePayload
    {
        /// <summary>
        /// 获取或设置流集合。
        /// </summary>
        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; }
    }

    private sealed class FfprobeStream
    {
        /// <summary>
        /// 获取或设置绝对流索引。
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>
        /// 获取或设置编解码类型。
        /// </summary>
        [JsonPropertyName("codec_type")]
        public string CodecType { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置编解码名称。
        /// </summary>
        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        /// <summary>
        /// 获取或设置标签信息。
        /// </summary>
        [JsonPropertyName("tags")]
        public FfprobeTags? Tags { get; set; }
    }

    private sealed class FfprobeTags
    {
        /// <summary>
        /// 获取或设置语言标签。
        /// </summary>
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        /// <summary>
        /// 获取或设置标题标签。
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}

/// <summary>
/// 表示 FFmpeg 命令执行结果。
/// </summary>
public sealed class FfmpegCommandResult
{
    /// <summary>
    /// 获取或设置标准输出。
    /// </summary>
    public string StandardOutput { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置标准错误。
    /// </summary>
    public string StandardError { get; set; } = string.Empty;
}

/// <summary>
/// 表示探测出的字幕流信息。
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "该类型表示探测出的字幕轨摘要，而不是 System.IO.Stream。")]
public sealed class ProbedSubtitleTrack
{
    /// <summary>
    /// 获取或设置绝对流索引。
    /// </summary>
    public int StreamIndex { get; set; }

    /// <summary>
    /// 获取或设置字幕流相对索引。
    /// </summary>
    public int SubtitleStreamIndex { get; set; }

    /// <summary>
    /// 获取或设置字幕编码格式。
    /// </summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置字幕轨标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 表示 FFmpeg 执行异常。
/// </summary>
public sealed class FfmpegExecutionException : Exception
{
    /// <summary>
    /// 初始化 FFmpeg 执行异常。
    /// </summary>
    public FfmpegExecutionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化带内部异常的 FFmpeg 执行异常。
    /// </summary>
    public FfmpegExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
