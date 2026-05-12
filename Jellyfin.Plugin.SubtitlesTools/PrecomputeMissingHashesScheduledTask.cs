using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 在 Jellyfin 计划任务中提供”历史影片批量处理（转为 MKV 并修复安卓播放兼容）”的手动入口。
/// </summary>
public sealed class PrecomputeMissingHashesScheduledTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly VideoHashBackfillService _videoHashBackfillService;

    /// <summary>
    /// 初始化历史影片批量处理任务。
    /// </summary>
    public PrecomputeMissingHashesScheduledTask(VideoHashBackfillService videoHashBackfillService)
    {
        _videoHashBackfillService = videoHashBackfillService;
    }

    /// <inheritdoc />
    public string Name => "批量处理历史视频（转为 MKV 并修复安卓播放兼容）";

    /// <inheritdoc />
    public string Key => "SubtitlesTools.PrecomputeMissingVideoHashes";

    /// <inheritdoc />
    public string Description => "扫描已入库的电影和剧集，对未处理影片补算文件指纹并转为 MKV，同时继续修复那些仍存在兼容性问题的旧视频。";

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
        return _videoHashBackfillService.ManageUnprocessedVideosAsync(progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }
}
