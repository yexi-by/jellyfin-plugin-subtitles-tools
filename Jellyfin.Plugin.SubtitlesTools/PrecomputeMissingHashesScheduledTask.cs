using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitlesTools.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.SubtitlesTools;

/// <summary>
/// 在 Jellyfin 计划任务中提供“一键算 CID/GCID 并转换为 MKV”的手动入口。
/// </summary>
public sealed class PrecomputeMissingHashesScheduledTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly VideoHashBackfillService _videoHashBackfillService;

    /// <summary>
    /// 初始化历史影片一键纳管任务。
    /// </summary>
    public PrecomputeMissingHashesScheduledTask(VideoHashBackfillService videoHashBackfillService)
    {
        _videoHashBackfillService = videoHashBackfillService;
    }

    /// <inheritdoc />
    public string Name => "一键算 CID/GCID 并转换为 MKV";

    /// <inheritdoc />
    public string Key => "SubtitlesTools.PrecomputeMissingVideoHashes";

    /// <inheritdoc />
    public string Description => "扫描已入库的电影和剧集，对未纳管影片补算 CID/GCID，并统一转换或纳管为 MKV。";

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
