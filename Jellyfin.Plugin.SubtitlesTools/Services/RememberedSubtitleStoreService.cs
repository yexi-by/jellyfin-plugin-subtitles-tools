using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 负责按用户持久化“已记住字幕”记录，并在读取时屏蔽损坏文件对主流程的影响。
/// </summary>
public sealed class RememberedSubtitleStoreService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<RememberedSubtitleStoreService> _logger;

    /// <summary>
    /// 初始化已记住字幕存储服务。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public RememberedSubtitleStoreService(ILogger<RememberedSubtitleStoreService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 读取指定用户在某个分段上的记住字幕记录。
    /// </summary>
    /// <param name="userId">当前登录用户标识。</param>
    /// <param name="partMediaPath">当前分段媒体文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>找到时返回记录，否则返回空。</returns>
    public async Task<RememberedSubtitleRecord?> GetAsync(
        Guid userId,
        string partMediaPath,
        CancellationToken cancellationToken)
    {
        var records = await ReadUserRecordsAsync(userId, cancellationToken).ConfigureAwait(false);
        records.TryGetValue(NormalizePath(partMediaPath), out var record);
        return record;
    }

    /// <summary>
    /// 批量读取多个分段路径对应的记住字幕记录。
    /// </summary>
    /// <param name="userId">当前登录用户标识。</param>
    /// <param name="partMediaPaths">待读取的分段路径集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>以规范化分段路径为键的记录字典。</returns>
    public async Task<Dictionary<string, RememberedSubtitleRecord>> GetManyAsync(
        Guid userId,
        IEnumerable<string> partMediaPaths,
        CancellationToken cancellationToken)
    {
        var records = await ReadUserRecordsAsync(userId, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, RememberedSubtitleRecord>(StringComparer.Ordinal);
        foreach (var path in partMediaPaths.Select(NormalizePath).Distinct(StringComparer.Ordinal))
        {
            if (records.TryGetValue(path, out var record))
            {
                result[path] = record;
            }
        }

        return result;
    }

    /// <summary>
    /// 写入或更新一条记住字幕记录。
    /// </summary>
    /// <param name="record">待写入的记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步写入操作的任务。</returns>
    public async Task SetAsync(RememberedSubtitleRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadUserRecordsAsync(record.UserId, cancellationToken).ConfigureAwait(false);
            records[NormalizePath(record.PartMediaPath)] = record;
            await WriteUserRecordsAsync(record.UserId, records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 清除指定用户在某个分段上的记住字幕记录。
    /// </summary>
    /// <param name="userId">当前登录用户标识。</param>
    /// <param name="partMediaPath">当前分段媒体文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若确实删除了记录则返回真，否则返回假。</returns>
    public async Task<bool> DeleteAsync(
        Guid userId,
        string partMediaPath,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadUserRecordsAsync(userId, cancellationToken).ConfigureAwait(false);
            var removed = records.Remove(NormalizePath(partMediaPath));
            if (!removed)
            {
                return false;
            }

            await WriteUserRecordsAsync(userId, records, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<Dictionary<string, RememberedSubtitleRecord>> ReadUserRecordsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var file = GetUserFile(userId);
        if (!file.Exists)
        {
            return new Dictionary<string, RememberedSubtitleRecord>(StringComparer.Ordinal);
        }

        try
        {
            await using var stream = file.OpenRead();
            var data = await JsonSerializer.DeserializeAsync<RememberedSubtitleUserData>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (data?.Items is null)
            {
                return new Dictionary<string, RememberedSubtitleRecord>(StringComparer.Ordinal);
            }

            return data.Items
                .Where(item => item.UserId == userId && !string.IsNullOrWhiteSpace(item.PartMediaPath))
                .GroupBy(item => NormalizePath(item.PartMediaPath), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.UpdatedAt).First(),
                    StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "记住字幕文件损坏，已按空记录处理。user_id={UserId} path={Path}", userId, file.FullName);
            return new Dictionary<string, RememberedSubtitleRecord>(StringComparer.Ordinal);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取记住字幕文件失败，已按空记录处理。user_id={UserId} path={Path}", userId, file.FullName);
            return new Dictionary<string, RememberedSubtitleRecord>(StringComparer.Ordinal);
        }
    }

    private async Task WriteUserRecordsAsync(
        Guid userId,
        Dictionary<string, RememberedSubtitleRecord> records,
        CancellationToken cancellationToken)
    {
        var directory = GetStoreDirectory();
        directory.Create();

        var file = GetUserFile(userId);
        if (records.Count == 0)
        {
            if (file.Exists)
            {
                file.Delete();
            }

            return;
        }

        var data = new RememberedSubtitleUserData
        {
            UserId = userId,
            Items = records.Values
                .OrderBy(item => item.PartMediaPath, StringComparer.Ordinal)
                .ToList()
        };

        var tempFile = new FileInfo($"{file.FullName}.tmp");
        await using (var stream = tempFile.Open(FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (file.Exists)
        {
            file.Delete();
        }

        tempFile.MoveTo(file.FullName);
    }

    private static string NormalizePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.GetFullPath(value.Trim());
    }

    private static DirectoryInfo GetStoreDirectory()
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("插件单例尚未初始化。");
        return new DirectoryInfo(Path.Combine(plugin.DataFolderPath, "remembered-subtitles"));
    }

    private static FileInfo GetUserFile(Guid userId)
    {
        return new FileInfo(Path.Combine(GetStoreDirectory().FullName, $"{userId:D}.json"));
    }

    /// <summary>
    /// 释放内部写锁，避免单元测试或宿主退出时留下未释放句柄。
    /// </summary>
    public void Dispose()
    {
        _writeLock.Dispose();
    }

    /// <summary>
    /// 表示单个用户的已记住字幕文件内容。
    /// </summary>
    private sealed class RememberedSubtitleUserData
    {
        /// <summary>
        /// 获取或设置用户标识。
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// 获取或设置记录集合。
        /// </summary>
        public List<RememberedSubtitleRecord> Items { get; set; } = [];
    }
}

/// <summary>
/// 表示插件内部持久化使用的记住字幕记录。
/// </summary>
public sealed class RememberedSubtitleRecord
{
    /// <summary>
    /// 获取或设置用户标识。
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置分段媒体文件完整路径。
    /// </summary>
    public string PartMediaPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置所属 Jellyfin 媒体项标识。
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置分段标识。
    /// </summary>
    public string PartId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置记住的字幕文件名。
    /// </summary>
    public string SubtitleFileName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置三字母语言码。
    /// </summary>
    public string Language { get; set; } = "und";

    /// <summary>
    /// 获取或设置字幕格式。
    /// </summary>
    public string Format { get; set; } = "srt";

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
