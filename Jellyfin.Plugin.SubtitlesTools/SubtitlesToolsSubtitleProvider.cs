using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 通过 Python 服务端搜索和下载字幕的 Jellyfin 字幕提供器。
/// </summary>
public sealed class SubtitlesToolsSubtitleProvider : ISubtitleProvider
{
    private readonly record struct HashResolutionMetrics(
        VideoHashResult HashResult,
        bool CacheHit,
        double CacheLookupMs,
        double ComputeMs,
        double CacheSaveMs);

    private static readonly VideoContentType[] SupportedTypes =
    [
        VideoContentType.Episode,
        VideoContentType.Movie
    ];

    private static readonly Dictionary<string, string> LanguageMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh"] = "zho",
        ["zh-cn"] = "zho",
        ["zh-hans"] = "zho",
        ["zh-tw"] = "zho",
        ["zh-hant"] = "zho",
        ["chs"] = "zho",
        ["cht"] = "zho",
        ["chi"] = "zho",
        ["en"] = "eng",
        ["eng"] = "eng",
        ["ja"] = "jpn",
        ["jpn"] = "jpn",
        ["ko"] = "kor",
        ["kor"] = "kor"
    };

    private static readonly string[] ForcedMarkers =
    [
        ".forced",
        "[forced]",
        "(forced)",
        " forced "
    ];

    private static readonly string[] HearingImpairedMarkers =
    [
        ".sdh",
        "[sdh]",
        "(sdh)",
        ".cc",
        "[cc]",
        "closed captions",
        "hearing impaired",
        "听障"
    ];

    private readonly SubtitlesToolsApiClient _apiClient;
    private readonly VideoHashCalculator _videoHashCalculator;
    private readonly VideoHashCacheService _videoHashCacheService;
    private readonly ILogger<SubtitlesToolsSubtitleProvider> _logger;
    private readonly ConcurrentDictionary<string, SubtitleSnapshot> _subtitleSnapshotCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 初始化字幕提供器。
    /// </summary>
    /// <param name="apiClient">Python 服务端客户端。</param>
    /// <param name="videoHashCalculator">视频哈希计算器。</param>
    /// <param name="videoHashCacheService">视频哈希缓存服务。</param>
    /// <param name="logger">日志器。</param>
    public SubtitlesToolsSubtitleProvider(
        SubtitlesToolsApiClient apiClient,
        VideoHashCalculator videoHashCalculator,
        VideoHashCacheService videoHashCacheService,
        ILogger<SubtitlesToolsSubtitleProvider> logger)
    {
        _apiClient = apiClient;
        _videoHashCalculator = videoHashCalculator;
        _videoHashCacheService = videoHashCacheService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Subtitles Tools";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes => SupportedTypes;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var traceId = CreateTraceId();
        var totalStopwatch = Stopwatch.StartNew();

        if (!SupportedTypes.Contains(request.ContentType))
        {
            return Array.Empty<RemoteSubtitleInfo>();
        }

        if (string.IsNullOrWhiteSpace(request.MediaPath))
        {
            _logger.LogDebug("字幕搜索被跳过：媒体路径为空。");
            return Array.Empty<RemoteSubtitleInfo>();
        }

        if (string.Equals(Path.GetExtension(request.MediaPath), ".strm", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("字幕搜索被跳过：当前仅支持本地文件，不处理 .strm。路径：{MediaPath}", request.MediaPath);
            return Array.Empty<RemoteSubtitleInfo>();
        }

        var fileInfo = new FileInfo(request.MediaPath);
        if (!fileInfo.Exists)
        {
            _logger.LogDebug("字幕搜索被跳过：媒体文件不存在。路径：{MediaPath}", request.MediaPath);
            return Array.Empty<RemoteSubtitleInfo>();
        }

        VideoHashResult hashResult;
        HashResolutionMetrics hashMetrics;
        try
        {
            hashMetrics = await GetOrComputeHashesAsync(fileInfo, traceId, cancellationToken).ConfigureAwait(false);
            hashResult = hashMetrics.HashResult;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            totalStopwatch.Stop();
            _logger.LogWarning(
                ex,
                "trace={TraceId} client_search_hash_failed media_path={MediaPath} total_ms={ElapsedMs:F2}",
                traceId,
                request.MediaPath,
                totalStopwatch.Elapsed.TotalMilliseconds);
            return Array.Empty<RemoteSubtitleInfo>();
        }

        try
        {
            var serviceStopwatch = Stopwatch.StartNew();
            var response = await _apiClient.SearchAsync(
                new SubtitleSearchRequestDto
                {
                    Gcid = hashResult.Gcid,
                    Cid = hashResult.Cid,
                    Name = fileInfo.Name
                },
                cancellationToken,
                traceId).ConfigureAwait(false);
            serviceStopwatch.Stop();

            var isHashMatch = string.Equals(response.MatchedBy, "gcid", StringComparison.OrdinalIgnoreCase);
            var remoteItems = response.Items
                .Select(item => CreateRemoteSubtitleInfo(item, request, isHashMatch))
                .ToArray();
            totalStopwatch.Stop();
            _logger.LogInformation(
                "trace={TraceId} client_search_complete media_path={MediaPath} hash_cache_hit={HashCacheHit} cache_lookup_ms={CacheLookupMs:F2} hash_compute_ms={HashComputeMs:F2} hash_save_ms={HashSaveMs:F2} service_ms={ServiceMs:F2} matched_by={MatchedBy} confidence={Confidence} items={ItemCount} total_ms={TotalMs:F2}",
                traceId,
                fileInfo.FullName,
                hashMetrics.CacheHit,
                hashMetrics.CacheLookupMs,
                hashMetrics.ComputeMs,
                hashMetrics.CacheSaveMs,
                serviceStopwatch.Elapsed.TotalMilliseconds,
                response.MatchedBy,
                response.Confidence,
                remoteItems.Length,
                totalStopwatch.Elapsed.TotalMilliseconds);
            return remoteItems;
        }
        catch (SubtitlesToolsApiException ex)
        {
            totalStopwatch.Stop();
            _logger.LogWarning(
                ex,
                "trace={TraceId} client_search_service_failed media_path={MediaPath} hash_cache_hit={HashCacheHit} cache_lookup_ms={CacheLookupMs:F2} hash_compute_ms={HashComputeMs:F2} hash_save_ms={HashSaveMs:F2} total_ms={TotalMs:F2}",
                traceId,
                request.MediaPath,
                hashMetrics.CacheHit,
                hashMetrics.CacheLookupMs,
                hashMetrics.ComputeMs,
                hashMetrics.CacheSaveMs,
                totalStopwatch.Elapsed.TotalMilliseconds);
            return Array.Empty<RemoteSubtitleInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("字幕标识不能为空。", nameof(id));
        }

        var traceId = CreateTraceId();
        var totalStopwatch = Stopwatch.StartNew();
        var downloadedSubtitle = await _apiClient.DownloadSubtitleAsync(id, cancellationToken, traceId).ConfigureAwait(false);
        var snapshot = _subtitleSnapshotCache.TryGetValue(id, out var cachedSnapshot)
            ? cachedSnapshot
            : CreateFallbackSnapshot(downloadedSubtitle.FileName);
        totalStopwatch.Stop();
        _logger.LogInformation(
            "trace={TraceId} client_download_complete subtitle_id={SubtitleId} file_name={FileName} format={Format} bytes={ByteCount} total_ms={ElapsedMs:F2}",
            traceId,
            id,
            downloadedSubtitle.FileName,
            snapshot.Format,
            downloadedSubtitle.Content.Length,
            totalStopwatch.Elapsed.TotalMilliseconds);

        return new SubtitleResponse
        {
            Format = snapshot.Format,
            Language = snapshot.ThreeLetterLanguage,
            IsForced = snapshot.IsForced,
            IsHearingImpaired = snapshot.IsHearingImpaired,
            Stream = new MemoryStream(downloadedSubtitle.Content, writable: false)
        };
    }

    private async Task<HashResolutionMetrics> GetOrComputeHashesAsync(
        FileInfo fileInfo,
        string traceId,
        CancellationToken cancellationToken)
    {
        var cacheLookupStopwatch = Stopwatch.StartNew();
        var cached = await _videoHashCacheService.TryGetAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
        cacheLookupStopwatch.Stop();
        if (cached is not null)
        {
            return new HashResolutionMetrics(
                cached,
                CacheHit: true,
                CacheLookupMs: cacheLookupStopwatch.Elapsed.TotalMilliseconds,
                ComputeMs: 0,
                CacheSaveMs: 0);
        }

        var computeStopwatch = Stopwatch.StartNew();
        var computed = await _videoHashCalculator
            .ComputeAsync(fileInfo.FullName, cancellationToken, traceId)
            .ConfigureAwait(false);
        computeStopwatch.Stop();

        var cacheSaveStopwatch = Stopwatch.StartNew();
        await _videoHashCacheService.SaveAsync(computed, cancellationToken).ConfigureAwait(false);
        cacheSaveStopwatch.Stop();
        return new HashResolutionMetrics(
            computed,
            CacheHit: false,
            CacheLookupMs: cacheLookupStopwatch.Elapsed.TotalMilliseconds,
            ComputeMs: computeStopwatch.Elapsed.TotalMilliseconds,
            CacheSaveMs: cacheSaveStopwatch.Elapsed.TotalMilliseconds);
    }

    private RemoteSubtitleInfo CreateRemoteSubtitleInfo(
        SubtitleSearchItemDto item,
        SubtitleSearchRequest request,
        bool isHashMatch)
    {
        var snapshot = CreateSnapshot(item, request);
        _subtitleSnapshotCache[item.Id] = snapshot;

        return new RemoteSubtitleInfo
        {
            Id = item.Id,
            ProviderName = Name,
            Name = BuildDisplayName(item),
            Format = snapshot.Format,
            ThreeLetterISOLanguageName = snapshot.ThreeLetterLanguage,
            Comment = item.ExtraName,
            IsHashMatch = isHashMatch,
            Forced = snapshot.IsForced,
            HearingImpaired = snapshot.IsHearingImpaired
        };
    }

    private static SubtitleSnapshot CreateSnapshot(
        SubtitleSearchItemDto item,
        SubtitleSearchRequest request)
    {
        var displayName = BuildDisplayName(item);
        return new SubtitleSnapshot
        {
            Format = NormalizeFormat(item.Ext),
            ThreeLetterLanguage = ResolveThreeLetterLanguage(
                item.Languages,
                request.Language,
                request.TwoLetterISOLanguageName),
            IsForced = ContainsMarker(displayName, ForcedMarkers),
            IsHearingImpaired = ContainsMarker(displayName, HearingImpairedMarkers)
        };
    }

    private static SubtitleSnapshot CreateFallbackSnapshot(string fileName)
    {
        return new SubtitleSnapshot
        {
            Format = NormalizeFormat(Path.GetExtension(fileName)),
            ThreeLetterLanguage = "und",
            IsForced = ContainsMarker(fileName, ForcedMarkers),
            IsHearingImpaired = ContainsMarker(fileName, HearingImpairedMarkers)
        };
    }

    private static string BuildDisplayName(SubtitleSearchItemDto item)
    {
        if (string.IsNullOrWhiteSpace(item.ExtraName))
        {
            return item.Name;
        }

        return $"{item.Name} {item.ExtraName}";
    }

    private static string NormalizeFormat(string? ext)
    {
        var normalized = ext?.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "srt" : normalized;
    }

    private static bool ContainsMarker(string text, IEnumerable<string> markers)
    {
        var normalized = $" {text.ToLowerInvariant()} ";
        return markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static string ResolveThreeLetterLanguage(
        IEnumerable<string> languages,
        string? requestLanguage,
        string? requestTwoLetterLanguage)
    {
        foreach (var language in languages)
        {
            var resolved = TryMapLanguage(language);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        var requestResolved = TryMapLanguage(requestLanguage);
        if (!string.IsNullOrWhiteSpace(requestResolved))
        {
            return requestResolved;
        }

        var requestTwoLetterResolved = TryMapLanguage(requestTwoLetterLanguage);
        if (!string.IsNullOrWhiteSpace(requestTwoLetterResolved))
        {
            return requestTwoLetterResolved;
        }

        return "und";
    }

    private static string? TryMapLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (LanguageMappings.TryGetValue(normalized, out var mappedLanguage))
        {
            return mappedLanguage;
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            var prefix = normalized[..dashIndex];
            if (LanguageMappings.TryGetValue(prefix, out mappedLanguage))
            {
                return mappedLanguage;
            }

            normalized = prefix;
        }

        if (normalized.Length == 3)
        {
            return normalized;
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized).ThreeLetterISOLanguageName.ToLowerInvariant();
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static string CreateTraceId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }
}
