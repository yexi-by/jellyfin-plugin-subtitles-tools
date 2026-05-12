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
/// 除字幕流探测外，本服务还负责读取 MKV 容器标签、视频流摘要以及编码器可用性。
/// </summary>
public sealed class FfmpegProcessService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<FfmpegProcessService> _logger;

    /// <summary>
    /// 初始化 FFmpeg 进程服务。
    /// </summary>
    public FfmpegProcessService(IApplicationPaths applicationPaths, ILogger<FfmpegProcessService> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>
    /// 读取 FFprobe 中的字幕流信息。
    /// </summary>
    public async Task<List<ProbedSubtitleTrack>> ProbeSubtitleStreamsAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var result = await RunFfprobeAsync(
            ["-v", "error", "-show_streams", "-print_format", "json", mediaPath],
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
                    : stream.Tags.Language.Trim().ToLowerInvariant(),
                Title = stream.Tags?.Title?.Trim() ?? string.Empty
            })
            .ToList();
    }

    /// <summary>
    /// 读取当前媒体的容器信息以及视频、音频、字幕流摘要。
    /// 该方法只返回插件做处理和兼容性判定所需的最小字段。
    /// </summary>
    public async Task<ProbedMediaInfo> ProbeMediaAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var result = await RunFfprobeAsync(
            [
                "-v",
                "error",
                // 这里只保留 show_entries 和 show_streams，避免 Windows 下 ffprobe 额外回传
                // filename 时把反斜杠路径写进 JSON，进而触发反序列化失败。
                "-show_entries",
                "format=format_name,format_long_name:stream=index,codec_type,codec_name,codec_long_name,profile,codec_tag_string,width,height,pix_fmt",
                "-show_streams",
                "-print_format",
                "json",
                mediaPath
            ],
            traceId,
            "probe_media",
            cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Deserialize<FfprobeMediaPayload>(result.StandardOutput, JsonSerializerOptions);
        return new ProbedMediaInfo
        {
            FormatName = payload?.Format?.FormatName?.Trim().ToLowerInvariant() ?? string.Empty,
            FormatLongName = payload?.Format?.FormatLongName?.Trim() ?? string.Empty,
            Streams = payload?.Streams?
                .OrderBy(stream => stream.Index)
                .Select(stream => new ProbedMediaStream
                {
                    Index = stream.Index,
                    CodecType = stream.CodecType?.Trim().ToLowerInvariant() ?? string.Empty,
                    CodecName = stream.CodecName?.Trim().ToLowerInvariant() ?? string.Empty,
                    CodecLongName = stream.CodecLongName?.Trim() ?? string.Empty,
                    Profile = stream.Profile?.Trim() ?? string.Empty,
                    CodecTagString = stream.CodecTagString?.Trim() ?? string.Empty,
                    Width = stream.Width,
                    Height = stream.Height,
                    PixelFormat = stream.PixelFormat?.Trim().ToLowerInvariant() ?? string.Empty
                })
                .ToList() ?? []
        };
    }

    /// <summary>
    /// 读取 MKV 容器的顶层标签。
    /// 这里只把标签当作插件内部的受管身份来源，不对外暴露通用元数据能力。
    /// </summary>
    public async Task<Dictionary<string, string>> ProbeContainerTagsAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var result = await RunFfprobeAsync(
            ["-v", "error", "-show_entries", "format_tags", "-print_format", "json", mediaPath],
            traceId,
            "probe_container_tags",
            cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Deserialize<FfprobeFormatPayload>(result.StandardOutput, JsonSerializerOptions);
        if (payload?.Format?.Tags is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(payload.Format.Tags, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 执行 FFmpeg 命令。
    /// </summary>
    public Task<FfmpegCommandResult> RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        string? traceId,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunProcessAsync(ResolveExecutablePath(isProbe: false), arguments, traceId, operationName, cancellationToken);
    }

    /// <summary>
    /// 验证当前配置下能否解析 FFmpeg 与 FFprobe。
    /// </summary>
    public (string FfmpegPath, string FfprobePath) ValidateExecutables()
    {
        return (ResolveExecutablePath(isProbe: false), ResolveExecutablePath(isProbe: true));
    }

    /// <summary>
    /// 判断当前 FFmpeg 是否编入了指定编码器。
    /// 这里只做“是否存在编码器名字”的轻量检测，不直接替代真实转码过程。
    /// </summary>
    public async Task<bool> SupportsEncoderAsync(
        string encoderName,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoderName);

        var result = await RunFfmpegAsync(
            ["-hide_banner", "-encoders"],
            traceId,
            "list_ffmpeg_encoders",
            cancellationToken).ConfigureAwait(false);

        return result.StandardOutput
            .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.Contains(encoderName, StringComparison.OrdinalIgnoreCase));
    }

    private Task<FfmpegCommandResult> RunFfprobeAsync(
        IReadOnlyList<string> arguments,
        string? traceId,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunProcessAsync(ResolveExecutablePath(isProbe: true), arguments, traceId, operationName, cancellationToken);
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

        using var process = new Process { StartInfo = processStartInfo };
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
                throw new FfmpegExecutionException($"FFmpeg 操作失败：{operationName}，退出码 {process.ExitCode}。{standardError}");
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
            throw new FfmpegExecutionException($"无法启动可执行文件 {executablePath}，请检查 FFmpeg 路径配置。", ex);
        }
    }

    private string ResolveExecutablePath(bool isProbe)
    {
        var configuredPath = Plugin.Instance?.Configuration?.FfmpegExecutablePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return ResolveFromConfiguredPath(configuredPath, isProbe);
        }

        foreach (var candidate in EnumerateAutomaticCandidates(isProbe))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return isProbe ? "ffprobe" : "ffmpeg";
    }

    private IEnumerable<string> EnumerateAutomaticCandidates(bool isProbe)
    {
        var executableName = isProbe
            ? OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe"
            : OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        if (!string.IsNullOrWhiteSpace(_applicationPaths.ProgramSystemPath))
        {
            var programSystemDirectory = new FileInfo(_applicationPaths.ProgramSystemPath).Directory;
            if (programSystemDirectory is not null)
            {
                yield return Path.Combine(programSystemDirectory.FullName, executableName);
                yield return Path.Combine(programSystemDirectory.FullName, "ffmpeg", executableName);
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            yield return Path.Combine("/usr/lib/jellyfin-ffmpeg", executableName);
            yield return Path.Combine("/usr/lib/jellyfin-ffmpeg7", executableName);
            yield return Path.Combine("/usr/bin", executableName);
        }

        foreach (var pathCandidate in TryResolveFromPath(isProbe ? "ffprobe" : "ffmpeg"))
        {
            yield return pathCandidate;
        }
    }

    private static string ResolveFromConfiguredPath(string configuredPath, bool isProbe)
    {
        var fullPath = Path.GetFullPath(configuredPath);
        var executableName = isProbe
            ? OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe"
            : OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        if (Directory.Exists(fullPath))
        {
            var executablePath = Path.Combine(fullPath, executableName);
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

        var siblingProbe = Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty, executableName);
        if (File.Exists(siblingProbe))
        {
            return siblingProbe;
        }

        throw new FileNotFoundException("未在 FFmpeg 同目录找到 FFprobe。", siblingProbe);
    }

    private static string[] TryResolveFromPath(string executableName)
    {
        var locator = OperatingSystem.IsWindows() ? "where" : "which";
        var startInfo = new ProcessStartInfo
        {
            FileName = locator,
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
        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; }
    }

    private sealed class FfprobeStream
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("codec_type")]
        public string CodecType { get; set; } = string.Empty;

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        [JsonPropertyName("tags")]
        public FfprobeTags? Tags { get; set; }
    }

    private sealed class FfprobeFormatPayload
    {
        [JsonPropertyName("format")]
        public FfprobeFormat? Format { get; set; }
    }

    private sealed class FfprobeMediaPayload
    {
        [JsonPropertyName("streams")]
        public List<FfprobeMediaStream>? Streams { get; set; }

        [JsonPropertyName("format")]
        public FfprobeMediaFormat? Format { get; set; }
    }

    private sealed class FfprobeFormat
    {
        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class FfprobeMediaFormat
    {
        [JsonPropertyName("format_name")]
        public string? FormatName { get; set; }

        [JsonPropertyName("format_long_name")]
        public string? FormatLongName { get; set; }
    }

    private sealed class FfprobeMediaStream
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        [JsonPropertyName("codec_long_name")]
        public string? CodecLongName { get; set; }

        [JsonPropertyName("profile")]
        public string? Profile { get; set; }

        [JsonPropertyName("codec_tag_string")]
        public string? CodecTagString { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("pix_fmt")]
        public string? PixelFormat { get; set; }
    }

    private sealed class FfprobeTags
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}

/// <summary>
/// 表示 FFmpeg 命令执行结果。
/// </summary>
public sealed class FfmpegCommandResult
{
    public string StandardOutput { get; set; } = string.Empty;
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
    public int StreamIndex { get; set; }
    public int SubtitleStreamIndex { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string Language { get; set; } = "und";
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 表示 ffprobe 返回的媒体探测摘要。
/// </summary>
public sealed class ProbedMediaInfo
{
    public string FormatName { get; set; } = string.Empty;
    public string FormatLongName { get; set; } = string.Empty;
    public List<ProbedMediaStream> Streams { get; set; } = [];
}

/// <summary>
/// 表示 ffprobe 返回的一条媒体流摘要。
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "该类型表示 ffprobe 返回的媒体流摘要，而不是 System.IO.Stream。")]
public sealed class ProbedMediaStream
{
    public int Index { get; set; }
    public string CodecType { get; set; } = string.Empty;
    public string CodecName { get; set; } = string.Empty;
    public string CodecLongName { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string CodecTagString { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string PixelFormat { get; set; } = string.Empty;
}

/// <summary>
/// 表示 FFmpeg 执行异常。
/// </summary>
public sealed class FfmpegExecutionException : Exception
{
    public FfmpegExecutionException(string message)
        : base(message)
    {
    }

    public FfmpegExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
