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

export function buildIdleConnectionStatus(): ConnectionStatusViewModel {
  return {
    tone: 'idle',
    label: '等待检测',
    title: '保存前先确认整条链路可用',
    message: '这里会显示 Python 服务版本、字幕源、FFmpeg / FFprobe 路径、QSV 设备与 h264_qsv 支持情况。',
    details: []
  };
}

export function buildLoadingConnectionStatus(): ConnectionStatusViewModel {
  return {
    tone: 'busy',
    label: '正在检测',
    title: '正在验证服务端与转码环境',
    message: '正在检查 Python 服务连通性、FFmpeg 可执行文件和 Intel QSV 能力，请稍候。',
    details: []
  };
}

export function buildSuccessConnectionStatus(payload: ConnectionHealthPayload): ConnectionStatusViewModel {
  const health = (payload.Health ?? {}) as Record<string, unknown>;
  const ffmpeg = (payload.Ffmpeg ?? {}) as Record<string, unknown>;
  const video = (payload.Video ?? {}) as Record<string, unknown>;
  const supportsH264Qsv = video.supportsH264Qsv === true ? '支持' : '不支持';

  return {
    tone: 'success',
    label: '检测通过',
    title: '服务链路已经准备就绪',
    message: '当前配置可以正常连接 Python 服务，并拿到字幕源与转码环境信息，可以直接保存。',
    details: [
      { label: '服务版本', value: readValue(health, ['Version', 'version'], '未返回') },
      { label: '字幕源', value: readValue(health, ['ProviderName', 'providerName', 'provider_name'], '未返回') },
      { label: 'FFmpeg', value: readValue(ffmpeg, ['ffmpegPath', 'FfmpegPath'], '未找到') },
      { label: 'FFprobe', value: readValue(ffmpeg, ['ffprobePath', 'FfprobePath'], '未找到') },
      { label: 'QSV 设备', value: readValue(video, ['qsvRenderDevicePath', 'QsvRenderDevicePath'], '未配置') },
      { label: 'h264_qsv', value: supportsH264Qsv }
    ]
  };
}

export function buildErrorConnectionStatus(message: string): ConnectionStatusViewModel {
  return {
    tone: 'error',
    label: '检测失败',
    title: '当前环境还不能直接投入使用',
    message,
    details: []
  };
}

export function getManagedStatusText(part: MediaPart): string {
  if (part.IsManaged !== true) {
    return '未纳管';
  }

  return part.ReadIdentityFromMetadata ? '已纳管（读取 MKV 元数据）' : '已纳管';
}

export function getManagedStatusTone(part: MediaPart): Extract<UiTone, 'neutral' | 'success'> {
  return part.IsManaged === true ? 'success' : 'neutral';
}

export function getCompatibilityStatusText(part: MediaPart): string {
  if (!part.RiskVerdict) {
    return '未评估';
  }

  if (part.NeedsCompatibilityRepair === true) {
    return `${part.RiskVerdict}（需修复）`;
  }

  return part.RiskVerdict;
}

export function getCompatibilityTone(part: MediaPart): Extract<UiTone, 'neutral' | 'success' | 'warning' | 'danger'> {
  if (!part.RiskVerdict) {
    return 'neutral';
  }

  if (part.NeedsCompatibilityRepair === true || part.RiskVerdict.includes('高风险')) {
    return 'danger';
  }

  if (part.RiskVerdict.includes('中风险')) {
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
    converted: '已转换',
    deleted: '已删除',
    embedded: '已内封',
    failed: '失败',
    no_candidates: '无候选',
    partial: '部分完成',
    sidecar: '已写为外挂'
  };

  return status ? (labels[status] ?? status) : '未知状态';
}

export function getItemMetrics(itemData: ItemPartsPayload): BatchMetric[] {
  const parts = itemData.Parts ?? [];
  const managedCount = parts.filter(part => part.IsManaged === true).length;
  const repairCount = parts.filter(part => part.NeedsCompatibilityRepair === true).length;
  const embeddedCount = parts.reduce((sum, part) => sum + (part.EmbeddedSubtitles?.length ?? 0), 0);
  const externalCount = parts.reduce((sum, part) => sum + (part.ExternalSubtitles?.length ?? 0), 0);
  const pluginManagedCount = parts.reduce((sum, part) => {
    return sum + (part.EmbeddedSubtitles?.filter(track => track.IsPluginManaged === true).length ?? 0);
  }, 0);

  return [
    {
      label: '分段总数',
      value: String(parts.length),
      note: itemData.IsMultipart ? '当前媒体已识别为多分段' : '当前媒体为单文件结构',
      tone: 'neutral'
    },
    {
      label: '已纳管分段',
      value: String(managedCount),
      note: managedCount === parts.length && parts.length > 0 ? '全部分段都已纳入插件识别范围' : '仍有分段等待纳管',
      tone: managedCount === parts.length && parts.length > 0 ? 'success' : 'warning'
    },
    {
      label: '待兼容修复',
      value: String(repairCount),
      note: repairCount === 0 ? '当前没有高风险硬解片段' : '建议优先执行 MKV 转换修复',
      tone: repairCount === 0 ? 'success' : 'danger'
    },
    {
      label: '内封字幕总数',
      value: String(embeddedCount),
      note: pluginManagedCount > 0 ? `插件写入 ${pluginManagedCount} 条字幕轨` : '暂未检测到插件写入轨道',
      tone: embeddedCount > 0 ? 'success' : 'neutral'
    },
    {
      label: '外挂字幕总数',
      value: String(externalCount),
      note: externalCount > 0 ? '当前媒体目录中已检测到可被 Jellyfin 识别的外挂字幕' : '当前媒体目录中还没有匹配到外挂字幕文件',
      tone: externalCount > 0 ? 'success' : 'neutral'
    }
  ];
}

export function getStatusTitle(tone: Extract<UiTone, 'idle' | 'busy' | 'success' | 'error'>): string {
  const labels = {
    busy: '正在处理',
    error: '需要处理',
    idle: '准备就绪',
    success: '最近一次操作已完成'
  };

  return labels[tone];
}

export function getStatusLabel(tone: Extract<UiTone, 'idle' | 'busy' | 'success' | 'error'>): string {
  const labels = {
    busy: '处理中',
    error: '异常',
    idle: '待执行',
    success: '已完成'
  };

  return labels[tone];
}

export function getPartSelectionSummary(part: MediaPart | null, activeSearchCount: number, mode?: SubtitleWriteMode): string[] {
  if (!part) {
    return [];
  }

  const summary = [
    `当前选中：${part.Label}`,
    `候选字幕 ${activeSearchCount} 条`
  ];

  if (mode) {
    summary.push(`写入模式：${getSubtitleWriteModeLabel(mode)}`);
  }

  return summary;
}

export function summarizeResultMessage(result: OperationResultItem | null | undefined): string {
  if (!result) {
    return '操作已完成。';
  }

  return result.Message?.trim() || '操作已完成。';
}

export function getSubtitleWriteModeLabel(mode: SubtitleWriteMode): string {
  return mode === 'sidecar' ? '外挂字幕' : '内封字幕';
}
