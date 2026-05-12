import type {
  BatchMetric,
  ConnectionHealthPayload,
  ConnectionStatusViewModel,
  ItemPartsPayload,
  MediaPart,
  OperationResultItem,
  SubtitleWriteMode,
  UiTone
} from './types';

function readValue(source: Record<string, unknown> | undefined, keys: string[], fallback: string): string {
  if (!source) {
    return fallback;
  }

  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim()) {
      return value.trim();
    }
  }

  return fallback;
}

function readNumberValue(source: Record<string, unknown> | undefined, keys: string[], fallback: string): string {
  if (!source) {
    return fallback;
  }

  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return `${value} 秒`;
    }
  }

  return fallback;
}

function hasCompatibilityRisk(part: MediaPart): boolean {
  return part.NeedsCompatibilityRepair === true
    || part.RiskVerdict?.includes('高风险') === true
    || part.RiskVerdict?.includes('中风险') === true;
}

export function buildIdleConnectionStatus(): ConnectionStatusViewModel {
  return {
    tone: 'idle',
    label: '待检测',
    title: '尚未检测',
    message: '建议先检测字幕源，再保存设置。',
    details: []
  };
}

export function buildLoadingConnectionStatus(): ConnectionStatusViewModel {
  return {
    tone: 'busy',
    label: '检测中',
    title: '正在检测字幕源',
    message: '正在检查内置字幕源和运行环境，请稍候。',
    details: []
  };
}

export function buildSuccessConnectionStatus(payload: ConnectionHealthPayload): ConnectionStatusViewModel {
  const health = (payload.Health ?? {}) as Record<string, unknown>;
  const ffmpeg = (payload.Ffmpeg ?? {}) as Record<string, unknown>;
  const video = (payload.Video ?? {}) as Record<string, unknown>;
  const supportsH264Qsv = video.supportsH264Qsv === true ? '是' : '否';

  return {
    tone: 'success',
    label: '正常',
    title: '字幕源正常',
    message: '可以开始使用字幕搜索和保存功能。',
    details: [
      { label: '插件版本', value: readValue(health, ['Version', 'version'], '-') },
      { label: '字幕源', value: readValue(health, ['ProviderName', 'providerName', 'provider_name'], '-') },
      { label: '上游地址', value: readValue(health, ['ProviderBaseUrl', 'providerBaseUrl', 'provider_base_url'], '-') },
      { label: '搜索缓存', value: readNumberValue(health, ['SearchCacheTtlSeconds', 'searchCacheTtlSeconds', 'search_cache_ttl_seconds'], '-') },
      { label: '字幕缓存', value: readNumberValue(health, ['SubtitleCacheTtlSeconds', 'subtitleCacheTtlSeconds', 'subtitle_cache_ttl_seconds'], '-') },
      { label: 'FFmpeg', value: readValue(ffmpeg, ['ffmpegPath', 'FfmpegPath'], '-') },
      { label: 'FFprobe', value: readValue(ffmpeg, ['ffprobePath', 'FfprobePath'], '-') },
      { label: 'QSV 设备', value: readValue(video, ['qsvRenderDevicePath', 'QsvRenderDevicePath'], '-') },
      { label: 'QSV 加速', value: supportsH264Qsv }
    ]
  };
}

export function buildErrorConnectionStatus(): ConnectionStatusViewModel {
  return {
    tone: 'error',
    label: '失败',
    title: '字幕源检测失败',
    message: '无法完成检测，请检查上游地址、超时和 FFmpeg 配置。',
    details: []
  };
}

export function getManagedStatusText(part: MediaPart): string {
  return part.IsManaged === true ? '已识别' : '待处理';
}

export function getManagedStatusTone(part: MediaPart): Extract<UiTone, 'neutral' | 'success'> {
  return part.IsManaged === true ? 'success' : 'neutral';
}

export function getCompatibilityStatusText(part: MediaPart): string {
  if (!part.RiskVerdict) {
    return '等待检查';
  }

  if (hasCompatibilityRisk(part)) {
    return '建议先优化';
  }

  return '播放正常';
}

export function getCompatibilityTone(part: MediaPart): Extract<UiTone, 'neutral' | 'success' | 'warning' | 'danger'> {
  if (!part.RiskVerdict) {
    return 'neutral';
  }

  if (hasCompatibilityRisk(part)) {
    return 'warning';
  }

  return 'success';
}

export function getBatchTone(status: string | undefined): Extract<UiTone, 'neutral' | 'success' | 'warning' | 'danger'> {
  if (status === 'embedded' || status === 'sidecar' || status === 'converted' || status === 'completed') {
    return 'success';
  }

  if (status === 'partial' || status === 'no_candidates') {
    return 'warning';
  }

  if (status === 'failed') {
    return 'danger';
  }

  return 'neutral';
}

export function getBatchStatusText(status: string | undefined): string {
  const labels: Record<string, string> = {
    completed: '已完成',
    converted: '已优化',
    deleted: '已删除',
    embedded: '写入视频',
    failed: '失败',
    no_candidates: '无结果',
    partial: '部分完成',
    sidecar: '另存字幕'
  };

  return status ? (labels[status] ?? status) : '未知';
}

export function getBatchSummaryMessage(item: OperationResultItem): string {
  if (item.Status === 'embedded') {
    return '字幕已写入视频文件。';
  }

  if (item.Status === 'sidecar') {
    return '字幕文件已保存到视频同目录。';
  }

  if (item.Status === 'converted') {
    return '已完成播放兼容性优化。';
  }

  if (item.Status === 'deleted') {
    return '已删除当前字幕。';
  }

  if (item.Status === 'no_candidates') {
    return '没有找到可用字幕。';
  }

  if (item.Status === 'partial') {
    return '部分文件处理完成，请检查结果。';
  }

  if (item.Status === 'failed') {
    return '处理失败，请稍后重试。';
  }

  return item.Message?.trim() || '处理完成。';
}

export function getItemMetrics(itemData: ItemPartsPayload): BatchMetric[] {
  const parts = itemData.Parts ?? [];
  const pendingCount = parts.filter(part => part.IsManaged !== true).length;
  const subtitleCount = parts.reduce((sum, part) => {
    return sum + (part.EmbeddedSubtitles?.length ?? 0) + (part.ExternalSubtitles?.length ?? 0);
  }, 0);
  const repairCount = parts.filter(part => hasCompatibilityRisk(part)).length;

  return [
    {
      label: '文件数',
      value: String(parts.length),
      tone: 'neutral'
    },
    {
      label: '待处理',
      value: String(pendingCount),
      tone: pendingCount === 0 ? 'success' : 'warning'
    },
    {
      label: '已有字幕',
      value: String(subtitleCount),
      tone: subtitleCount > 0 ? 'success' : 'neutral'
    },
    {
      label: '建议优化',
      value: String(repairCount),
      tone: repairCount === 0 ? 'success' : 'warning'
    }
  ];
}

export function getStatusTitle(tone: Extract<UiTone, 'idle' | 'busy' | 'success' | 'error'>): string {
  const labels = {
    busy: '正在处理',
    error: '操作失败',
    idle: '下一步',
    success: '处理完成'
  };

  return labels[tone];
}

export function getStatusLabel(tone: Extract<UiTone, 'idle' | 'busy' | 'success' | 'error'>): string {
  const labels = {
    busy: '进行中',
    error: '失败',
    idle: '提醒',
    success: '成功'
  };

  return labels[tone];
}

export function getDefaultOverlayStatus(part: MediaPart | null): {
  message: string;
  title: string;
  tone: Extract<UiTone, 'idle'>;
} {
  if (!part) {
    return {
      tone: 'idle',
      title: '还没有选择文件',
      message: '先选择一个文件，再开始处理。'
    };
  }

  if (part.IsManaged !== true) {
    return {
      tone: 'idle',
      title: '当前文件还不能直接保存字幕',
      message: '建议先刷新状态或先优化当前文件。'
    };
  }

  if (hasCompatibilityRisk(part)) {
    return {
      tone: 'idle',
      title: '建议先优化当前文件',
      message: '这样更稳，后续保存字幕也更顺畅。'
    };
  }

  return {
    tone: 'idle',
    title: '下一步',
    message: '可以先搜索字幕，再选择保存方式。'
  };
}

export function getSubtitleWriteModeLabel(mode: SubtitleWriteMode): string {
  return mode === 'sidecar' ? '另存字幕' : '写入视频';
}

export function getSubtitleSourceScoreText(score: number | string | undefined): string {
  if (score === undefined || score === null) {
    return '未提供评分';
  }

  if (typeof score === 'number') {
    return score === 0 ? '未提供评分' : String(score);
  }

  const normalizedScore = score.trim();
  if (!normalizedScore) {
    return '未提供评分';
  }

  const parsedScore = Number(normalizedScore);
  if (Number.isFinite(parsedScore) && parsedScore === 0) {
    return '未提供评分';
  }

  return normalizedScore;
}
