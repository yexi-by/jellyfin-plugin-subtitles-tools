using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 在 Jellyfin 计划任务中提供“补算缺失视频哈希”的手动入口。
/// </summary>
public sealed class PrecomputeMissingHashesScheduledTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly VideoHashBackfillService _videoHashBackfillService;

    /// <summary>
    /// 初始化缺失视频哈希回填任务。
    /// </summary>
    /// <param name="videoHashBackfillService">缺失视频哈希回填服务。</param>
    public PrecomputeMissingHashesScheduledTask(VideoHashBackfillService videoHashBackfillService)
    {
        _videoHashBackfillService = videoHashBackfillService;
    }

    /// <inheritdoc />
    public string Name => "预计算缺失的视频哈希";

    /// <inheritdoc />
    public string Key => "SubtitlesTools.PrecomputeMissingVideoHashes";

    /// <inheritdoc />
    public string Description => "扫描 Jellyfin 已入库的电影和剧集，为尚未缓存 GCID/CID 的本地视频文件补算哈希。";

    /// <inheritdoc />
    public string Category => "Subtitles Tools";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return _videoHashBackfillService.PrecomputeMissingHashesAsync(progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }
}
