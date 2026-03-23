import type { PluginConfiguration, SubtitleWriteMode } from '../shared/types';

type LoadConfiguration = () => Promise<Pick<PluginConfiguration, 'DefaultSubtitleWriteMode'>>;
type ApplyWriteMode = (mode: SubtitleWriteMode) => void;
type ShowOverlay = () => void;

export function normalizeOverlayWriteMode(value: string | undefined): SubtitleWriteMode {
  return value === 'sidecar' ? 'sidecar' : 'embedded';
}

export async function syncOverlayWriteModeFromConfiguration(
  loadConfiguration: LoadConfiguration,
  applyWriteMode: ApplyWriteMode
): Promise<void> {
  try {
    const configuration = await loadConfiguration();
    applyWriteMode(normalizeOverlayWriteMode(configuration.DefaultSubtitleWriteMode));
  } catch {
    // 读取配置失败时保留当前模式，避免阻塞详情页入口。
  }
}

export async function openOverlayWithConfiguredWriteMode(
  loadConfiguration: LoadConfiguration,
  applyWriteMode: ApplyWriteMode,
  showOverlay: ShowOverlay
): Promise<void> {
  try {
    await syncOverlayWriteModeFromConfiguration(loadConfiguration, applyWriteMode);
  } finally {
    showOverlay();
  }
}
