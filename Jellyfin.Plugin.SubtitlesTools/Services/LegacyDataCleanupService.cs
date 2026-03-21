using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitlesTools.Services;

/// <summary>
/// 在插件启动时清理旧版遗留数据文件，释放插件数据目录空间。
/// 该服务只处理插件自己的数据目录，不触碰媒体目录。
/// </summary>
public sealed class LegacyDataCleanupService : IHostedService
{
    private readonly ILogger<LegacyDataCleanupService> _logger;

    /// <summary>
    /// 初始化启动清理服务。
    /// </summary>
    public LegacyDataCleanupService(ILogger<LegacyDataCleanupService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath) || !Directory.Exists(dataFolderPath))
        {
            return Task.CompletedTask;
        }

        long deletedBytes = 0;
        deletedBytes += DeleteFileIfExists(Path.Combine(dataFolderPath, "original-video-hash-archive.json"));
        deletedBytes += DeleteDirectoryIfExists(Path.Combine(dataFolderPath, "hash-cache"));
        deletedBytes += DeleteDirectoryIfExists(Path.Combine(dataFolderPath, "temp-subtitle-conversion"));

        _logger.LogInformation(
            "legacy_cleanup_complete data_dir={DataDir} deleted_bytes={DeletedBytes}",
            dataFolderPath,
            deletedBytes);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private long DeleteFileIfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return 0;
        }

        var fileInfo = new FileInfo(filePath);
        var size = fileInfo.Length;
        fileInfo.Delete();
        _logger.LogInformation("legacy_cleanup_deleted_file path={Path} bytes={Bytes}", filePath, size);
        return size;
    }

    private long DeleteDirectoryIfExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var size = GetDirectorySize(new DirectoryInfo(directoryPath));
        Directory.Delete(directoryPath, recursive: true);
        _logger.LogInformation("legacy_cleanup_deleted_directory path={Path} bytes={Bytes}", directoryPath, size);
        return size;
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        long total = 0;
        foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            total += file.Length;
        }

        return total;
    }
}
