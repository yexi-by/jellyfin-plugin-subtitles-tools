using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 计算迅雷所需的 CID 和 GCID。
/// </summary>
public sealed class VideoHashCalculator
{
    private const int CidSmallFileThreshold = 0xF000;
    private const int CidSegmentSize = 0x5000;
    private const int InitialPieceSize = 0x40000;
    private const int MaxPieceSize = 0x200000;
    private const int MaxPieceCount = 0x200;
    private readonly ILogger<VideoHashCalculator> _logger;

    /// <summary>
    /// 初始化哈希计算器。
    /// </summary>
    /// <param name="logger">日志器。</param>
    public VideoHashCalculator(ILogger<VideoHashCalculator>? logger = null)
    {
        _logger = logger ?? NullLogger<VideoHashCalculator>.Instance;
    }

    /// <summary>
    /// 为给定视频文件计算 CID 和 GCID。
    /// </summary>
    /// <param name="mediaPath">视频文件完整路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="traceId">链路追踪标识。</param>
    /// <returns>包含 CID 与 GCID 的结果模型。</returns>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "这里只对调用前已验证存在的本地媒体文件执行只读哈希计算，不会拼接或派生新的外部路径。")]
    public async Task<VideoHashResult> ComputeAsync(
        string mediaPath,
        CancellationToken cancellationToken,
        string? traceId = null)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            throw new ArgumentException("媒体文件路径不能为空。", nameof(mediaPath));
        }

        var fileInfo = new FileInfo(mediaPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("媒体文件不存在。", mediaPath);
        }

        var totalStopwatch = Stopwatch.StartNew();
        var cidStopwatch = Stopwatch.StartNew();
        var cid = await ComputeCidAsync(fileInfo, cancellationToken).ConfigureAwait(false);
        cidStopwatch.Stop();

        var gcidStopwatch = Stopwatch.StartNew();
        var gcid = await ComputeGcidAsync(fileInfo, cancellationToken).ConfigureAwait(false);
        gcidStopwatch.Stop();
        totalStopwatch.Stop();

        _logger.LogInformation(
            "trace={TraceId} client_hash_complete path={MediaPath} file_size={FileSize} cid_ms={CidMs:F2} gcid_ms={GcidMs:F2} total_ms={TotalMs:F2}",
            NormalizeTraceId(traceId),
            fileInfo.FullName,
            fileInfo.Length,
            cidStopwatch.Elapsed.TotalMilliseconds,
            gcidStopwatch.Elapsed.TotalMilliseconds,
            totalStopwatch.Elapsed.TotalMilliseconds);

        return new VideoHashResult
        {
            MediaPath = fileInfo.FullName,
            FileSize = fileInfo.Length,
            LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
            Cid = cid,
            Gcid = gcid
        };
    }

    private static async Task<string> ComputeCidAsync(FileInfo fileInfo, CancellationToken cancellationToken)
    {
        // 迅雷接口协议本身依赖 SHA1，这里是为了兼容其既有算法，不是用于安全场景。
#pragma warning disable CA5350
        using var sha1 = SHA1.Create();
#pragma warning restore CA5350
        await using var stream = OpenRead(fileInfo.FullName);

        if (fileInfo.Length < CidSmallFileThreshold)
        {
            await UpdateHashFromStreamAsync(sha1, stream, (int)fileInfo.Length, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(FinalizeHash(sha1));
        }

        await UpdateHashFromStreamAsync(sha1, stream, CidSegmentSize, cancellationToken).ConfigureAwait(false);
        stream.Seek(fileInfo.Length / 3, SeekOrigin.Begin);
        await UpdateHashFromStreamAsync(sha1, stream, CidSegmentSize, cancellationToken).ConfigureAwait(false);
        stream.Seek(fileInfo.Length - CidSegmentSize, SeekOrigin.Begin);
        await UpdateHashFromStreamAsync(sha1, stream, CidSegmentSize, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(FinalizeHash(sha1));
    }

    private static async Task<string> ComputeGcidAsync(FileInfo fileInfo, CancellationToken cancellationToken)
    {
        var pieceSize = DeterminePieceSize(fileInfo.Length);
        // 迅雷接口协议本身依赖 SHA1，这里是为了兼容其既有算法，不是用于安全场景。
#pragma warning disable CA5350
        using var outerSha1 = SHA1.Create();
#pragma warning restore CA5350
        await using var stream = OpenRead(fileInfo.FullName);
        var buffer = new byte[pieceSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await ReadAtLeastAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            var pieceHash = HashPiece(buffer, bytesRead);
            outerSha1.TransformBlock(pieceHash, 0, pieceHash.Length, pieceHash, 0);
        }

        return Convert.ToHexString(FinalizeHash(outerSha1));
    }

    private static FileStream OpenRead(string mediaPath)
    {
        return new FileStream(
            mediaPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1024 * 128,
            useAsync: true);
    }

    private static int DeterminePieceSize(long fileSize)
    {
        var pieceSize = InitialPieceSize;
        while ((fileSize / pieceSize) > MaxPieceCount && pieceSize < MaxPieceSize)
        {
            pieceSize <<= 1;
        }

        return pieceSize;
    }

    private static async Task UpdateHashFromStreamAsync(
        HashAlgorithm hashAlgorithm,
        Stream stream,
        int bytesToRead,
        CancellationToken cancellationToken)
    {
        var remaining = bytesToRead;
        var buffer = new byte[Math.Min(bytesToRead, 81920)];

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentRead = Math.Min(remaining, buffer.Length);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, currentRead), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            hashAlgorithm.TransformBlock(buffer, 0, bytesRead, buffer, 0);
            remaining -= bytesRead;
        }
    }

    private static async Task<int> ReadAtLeastAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }

    private static byte[] HashPiece(byte[] buffer, int bytesRead)
    {
        // 迅雷接口协议本身依赖 SHA1，这里是为了兼容其既有算法，不是用于安全场景。
#pragma warning disable CA5350
        using var sha1 = SHA1.Create();
#pragma warning restore CA5350
        return sha1.ComputeHash(buffer, 0, bytesRead);
    }

    private static byte[] FinalizeHash(HashAlgorithm hashAlgorithm)
    {
        hashAlgorithm.TransformFinalBlock([], 0, 0);
        return hashAlgorithm.Hash ?? [];
    }

    private static string NormalizeTraceId(string? traceId)
    {
        return string.IsNullOrWhiteSpace(traceId) ? "-" : traceId;
    }
}
