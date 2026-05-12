using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Configuration;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 内置字幕源业务入口，负责字幕搜索、结果缓存、字幕下载和健康信息汇总。
/// </summary>
public sealed class SubtitleSourceService
{
    private readonly SubtitleSourceCacheStore _cacheStore;
    private readonly ThunderSubtitleProvider _provider;
    private readonly ILogger<SubtitleSourceService> _logger;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// 初始化内置字幕源服务。
    /// </summary>
    /// <param name="cacheStore">字幕源缓存。</param>
    /// <param name="provider">迅雷字幕源。</param>
    /// <param name="logger">日志器。</param>
    public SubtitleSourceService(
        SubtitleSourceCacheStore cacheStore,
        ThunderSubtitleProvider provider,
        ILogger<SubtitleSourceService> logger)
        : this(cacheStore, provider, logger, GetPluginConfiguration)
    {
    }

    /// <summary>
    /// 使用指定配置读取器初始化内置字幕源服务；该构造器供测试注入配置。
    /// </summary>
    /// <param name="cacheStore">字幕源缓存。</param>
    /// <param name="provider">迅雷字幕源。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="configurationAccessor">配置读取器。</param>
    internal SubtitleSourceService(
        SubtitleSourceCacheStore cacheStore,
        ThunderSubtitleProvider provider,
        ILogger<SubtitleSourceService> logger,
        Func<PluginConfiguration> configurationAccessor)
    {
        _cacheStore = cacheStore;
        _provider = provider;
        _logger = logger;
        _configurationAccessor = configurationAccessor;
    }

    /// <summary>
    /// 检查内置字幕源配置和运行参数。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>内置字幕源健康检查结果。</returns>
    public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken cancellationToken, string? traceId = null)
    {
        var configuration = GetNormalizedConfiguration();
        return CheckHealthAsync(configuration, cancellationToken, traceId);
    }

    /// <summary>
    /// 使用表单候选配置检查内置字幕源配置和运行参数。
    /// </summary>
    /// <param name="configuration">候选配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>内置字幕源健康检查结果。</returns>
    internal Task<ServiceHealthResult> CheckHealthAsync(
        PluginConfiguration configuration,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedConfiguration = NormalizeConfiguration(configuration);
        _logger.LogInformation(
            "trace={TraceId} subtitle_source_health provider={ProviderName} base_url={BaseUrl} timeout_s={TimeoutSeconds} search_cache_ttl_s={SearchCacheTtlSeconds} subtitle_cache_ttl_s={SubtitleCacheTtlSeconds}",
            NormalizeTraceId(traceId),
            ThunderSubtitleProvider.ProviderName,
            normalizedConfiguration.ThunderBaseUrl,
            normalizedConfiguration.RequestTimeoutSeconds,
            normalizedConfiguration.SearchCacheTtlSeconds,
            normalizedConfiguration.SubtitleCacheTtlSeconds);

        return Task.FromResult(new ServiceHealthResult
        {
            TimeoutSeconds = normalizedConfiguration.RequestTimeoutSeconds,
            Health = new HealthResponseDto
            {
                Status = "ok",
                Version = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(Plugin).Assembly.GetName().Version?.ToString()
                    ?? "0.0.0.0",
                ProviderName = ThunderSubtitleProvider.ProviderName,
                ProviderBaseUrl = normalizedConfiguration.ThunderBaseUrl,
                ProviderAvailable = true,
                SearchCacheTtlSeconds = normalizedConfiguration.SearchCacheTtlSeconds,
                SubtitleCacheTtlSeconds = normalizedConfiguration.SubtitleCacheTtlSeconds
            }
        });
    }

    /// <summary>
    /// 搜索字幕候选并返回规范化结果。
    /// </summary>
    /// <param name="request">字幕搜索请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>字幕候选列表。</returns>
    public async Task<SubtitleSearchResponseDto> SearchAsync(
        SubtitleSearchRequestDto request,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Gcid is null && normalizedRequest.Name is null)
        {
            throw new ArgumentException("GCID 与文件名至少需要提供一个。", nameof(request));
        }

        var configuration = GetNormalizedConfiguration();
        var traceLabel = NormalizeTraceId(traceId);
        var totalStopwatch = Stopwatch.StartNew();
        var cacheKey = _cacheStore.BuildSearchCacheKey(normalizedRequest.Gcid, normalizedRequest.Cid, normalizedRequest.Name);
        var cachedEntry = await _cacheStore.GetSearchEntryAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cachedEntry is not null)
        {
            totalStopwatch.Stop();
            _logger.LogInformation(
                "trace={TraceId} subtitle_search_cache_hit matched_by={MatchedBy} confidence={Confidence} items={ItemCount} total_ms={ElapsedMs:F2}",
                traceLabel,
                cachedEntry.MatchedBy,
                cachedEntry.Confidence,
                cachedEntry.Items.Count,
                totalStopwatch.Elapsed.TotalMilliseconds);
            return BuildSearchResponse(cachedEntry);
        }

        var matchedBy = "gcid";
        var confidence = "high";
        var providerItems = new List<ProviderSubtitle>();

        if (normalizedRequest.Gcid is not null)
        {
            providerItems = await _provider.SearchByGcidAsync(normalizedRequest.Gcid, cancellationToken, traceLabel).ConfigureAwait(false);
        }

        if (providerItems.Count == 0 && normalizedRequest.Name is not null)
        {
            matchedBy = "name";
            confidence = normalizedRequest.Gcid is null ? "high" : "fallback";
            providerItems = await _provider.SearchByNameAsync(normalizedRequest.Name, cancellationToken, traceLabel).ConfigureAwait(false);
        }

        var normalizedItems = NormalizeItems(providerItems, configuration);
        var searchEntry = new SearchCacheEntry
        {
            MatchedBy = matchedBy,
            Confidence = confidence,
            ExpiresAt = DateTime.UtcNow.AddSeconds(configuration.SearchCacheTtlSeconds),
            Items = normalizedItems.Select(item => item.Item).ToList()
        };

        await _cacheStore.SetSearchEntryAsync(cacheKey, searchEntry, cancellationToken).ConfigureAwait(false);
        foreach (var item in normalizedItems)
        {
            await _cacheStore.SetSubtitleMetadataAsync(item.Metadata, cancellationToken).ConfigureAwait(false);
        }

        totalStopwatch.Stop();
        _logger.LogInformation(
            "trace={TraceId} subtitle_search_complete matched_by={MatchedBy} confidence={Confidence} items={ItemCount} total_ms={ElapsedMs:F2}",
            traceLabel,
            matchedBy,
            confidence,
            searchEntry.Items.Count,
            totalStopwatch.Elapsed.TotalMilliseconds);
        return BuildSearchResponse(searchEntry);
    }

    /// <summary>
    /// 通过缓存中的字幕标识下载字幕内容。
    /// </summary>
    /// <param name="subtitleId">搜索阶段生成的稳定字幕标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>字幕下载结果。</returns>
    public async Task<DownloadedSubtitle> DownloadSubtitleAsync(
        string subtitleId,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        if (string.IsNullOrWhiteSpace(subtitleId))
        {
            throw new ArgumentException("字幕标识不能为空。", nameof(subtitleId));
        }

        var traceLabel = NormalizeTraceId(traceId);
        var totalStopwatch = Stopwatch.StartNew();
        var metadata = await _cacheStore.GetSubtitleMetadataAsync(subtitleId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            _logger.LogWarning("trace={TraceId} subtitle_download_metadata_miss subtitle_id={SubtitleId}", traceLabel, subtitleId);
            throw new SubtitleNotFoundException("字幕不存在或缓存已过期。");
        }

        var cachedDownload = await _cacheStore.GetSubtitleContentAsync(metadata, cancellationToken).ConfigureAwait(false);
        if (cachedDownload is not null)
        {
            totalStopwatch.Stop();
            _logger.LogInformation(
                "trace={TraceId} subtitle_download_cache_hit subtitle_id={SubtitleId} bytes={ByteCount} total_ms={ElapsedMs:F2}",
                traceLabel,
                subtitleId,
                cachedDownload.Content.Length,
                totalStopwatch.Elapsed.TotalMilliseconds);
            return cachedDownload;
        }

        var downloaded = await _provider
            .DownloadSubtitleAsync(metadata.Url, metadata.Name, metadata.Ext, cancellationToken, traceLabel)
            .ConfigureAwait(false);
        await _cacheStore.SetSubtitleContentAsync(metadata, downloaded, cancellationToken).ConfigureAwait(false);
        totalStopwatch.Stop();
        _logger.LogInformation(
            "trace={TraceId} subtitle_download_complete subtitle_id={SubtitleId} bytes={ByteCount} total_ms={ElapsedMs:F2}",
            traceLabel,
            subtitleId,
            downloaded.Content.Length,
            totalStopwatch.Elapsed.TotalMilliseconds);
        return downloaded;
    }

    private static SubtitleSearchRequestDto NormalizeRequest(SubtitleSearchRequestDto request)
    {
        return new SubtitleSearchRequestDto
        {
            Gcid = NormalizeHash(request.Gcid),
            Cid = NormalizeHash(request.Cid),
            Name = NormalizeText(request.Name)
        };
    }

    private static List<NormalizedSubtitleItem> NormalizeItems(
        List<ProviderSubtitle> providerItems,
        PluginConfiguration configuration)
    {
        var bestItemsByUrl = new Dictionary<string, ProviderSubtitle>(StringComparer.Ordinal);
        foreach (var item in providerItems)
        {
            if (!bestItemsByUrl.TryGetValue(item.Url, out var currentBest) || CompareRank(item, currentBest) > 0)
            {
                bestItemsByUrl[item.Url] = item;
            }
        }

        return bestItemsByUrl.Values
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.FingerprintScore)
            .ThenByDescending(item => item.Languages.Count)
            .ThenByDescending(item => item.DurationMilliseconds)
            .Select(item =>
            {
                var subtitleId = BuildSubtitleId(item);
                return new NormalizedSubtitleItem
                {
                    Item = new SearchCacheItem
                    {
                        Id = subtitleId,
                        Name = item.Name,
                        Ext = item.Ext,
                        Languages = item.Languages,
                        DurationMilliseconds = item.DurationMilliseconds,
                        Source = item.Source,
                        Score = item.Score,
                        FingerprintScore = item.FingerprintScore,
                        ExtraName = item.ExtraName
                    },
                    Metadata = new CachedSubtitleMetadata
                    {
                        SubtitleId = subtitleId,
                        Provider = item.Provider,
                        Url = item.Url,
                        Name = item.Name,
                        Ext = item.Ext,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(configuration.SubtitleCacheTtlSeconds)
                    }
                };
            })
            .ToList();
    }

    private static int CompareRank(ProviderSubtitle left, ProviderSubtitle right)
    {
        var scoreComparison = left.Score.CompareTo(right.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var fingerprintComparison = left.FingerprintScore.CompareTo(right.FingerprintScore);
        if (fingerprintComparison != 0)
        {
            return fingerprintComparison;
        }

        var languageComparison = left.Languages.Count.CompareTo(right.Languages.Count);
        if (languageComparison != 0)
        {
            return languageComparison;
        }

        return left.DurationMilliseconds.CompareTo(right.DurationMilliseconds);
    }

    private static SubtitleSearchResponseDto BuildSearchResponse(SearchCacheEntry entry)
    {
        return new SubtitleSearchResponseDto
        {
            MatchedBy = entry.MatchedBy,
            Confidence = entry.Confidence,
            Items = entry.Items.Select(item => new SubtitleSearchItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Ext = item.Ext,
                Languages = item.Languages,
                DurationMilliseconds = item.DurationMilliseconds,
                Source = item.Source,
                Score = item.Score,
                FingerprintScore = item.FingerprintScore,
                ExtraName = item.ExtraName
            }).ToList()
        };
    }

    private static string BuildSubtitleId(ProviderSubtitle item)
    {
        var raw = $"{item.Provider}|{item.Url}|{item.Name}|{item.Ext}";
        // 字幕标识需要与上游元数据稳定绑定；这里不是安全用途。
#pragma warning disable CA5350
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
#pragma warning restore CA5350
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? NormalizeHash(string? value)
    {
        return NormalizeText(value)?.ToUpperInvariant();
    }

    private static PluginConfiguration GetPluginConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private PluginConfiguration GetNormalizedConfiguration()
    {
        return NormalizeConfiguration(_configurationAccessor());
    }

    private static PluginConfiguration NormalizeConfiguration(PluginConfiguration configuration)
    {
        return new PluginConfiguration
        {
            ThunderBaseUrl = PluginConfiguration.NormalizeThunderBaseUrl(configuration.ThunderBaseUrl),
            RequestTimeoutSeconds = PluginConfiguration.NormalizeTimeoutSeconds(configuration.RequestTimeoutSeconds),
            SearchCacheTtlSeconds = PluginConfiguration.NormalizeSearchCacheTtlSeconds(configuration.SearchCacheTtlSeconds),
            SubtitleCacheTtlSeconds = PluginConfiguration.NormalizeSubtitleCacheTtlSeconds(configuration.SubtitleCacheTtlSeconds)
        };
    }

    private static string NormalizeTraceId(string? traceId)
    {
        return string.IsNullOrWhiteSpace(traceId) ? "-" : traceId;
    }

    /// <summary>
    /// 规范化后的字幕项与对应下载元数据。
    /// </summary>
    private sealed class NormalizedSubtitleItem
    {
        public SearchCacheItem Item { get; set; } = new();

        public CachedSubtitleMetadata Metadata { get; set; } = new();
    }
}
