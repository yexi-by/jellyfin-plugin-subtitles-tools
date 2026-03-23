import { createRoot } from 'react-dom/client';
import '../design-system/theme.css';
import { OVERLAY_IDS, PLUGIN_UNIQUE_ID, ROUTE_POLL_MS } from '../shared/constants';
import { getCurrentItemId } from '../shared/dom';
import { extractErrorMessage } from '../shared/errors';
import { getSubtitleWriteModeLabel } from '../shared/formatters';
import { readPluginConfiguration } from '../shared/runtime';
import type { SubtitleWriteMode } from '../shared/types';
import { FloatingButton, OverlayApp } from './App';
import {
  applyBatchResults,
  applyDeleteResultToPart,
  applyOperationResultToPart,
  applyPartsPayload,
  closeOverlay,
  convertGroup,
  convertPart,
  createBatchRefreshValidator,
  createSinglePartRefreshValidator,
  deletePartEmbeddedSubtitle,
  downloadBest,
  downloadCandidate,
  fetchPartsPayload,
  getActivePart,
  getOverlayState,
  openOverlay,
  patchOverlayState,
  refreshCurrentPageState,
  refreshOverlayDataWithRetry,
  searchPart,
  setBatchResults,
  setBusy,
  setSearchResults,
  setStatus,
  setSubtitleWriteMode,
  subscribe
} from './store';

function ensureRoot(id: string): HTMLElement {
  const existing = document.getElementById(id);
  if (existing) {
    return existing as HTMLElement;
  }

  const element = document.createElement('div');
  element.id = id;
  document.body.appendChild(element);
  return element;
}

function normalizeWriteMode(value: string | undefined): SubtitleWriteMode {
  return value === 'sidecar' ? 'sidecar' : 'embedded';
}

if (!window.__subtitlesToolsGlobalLoaded) {
  window.__subtitlesToolsGlobalLoaded = true;

  const floatingRoot = createRoot(ensureRoot(OVERLAY_IDS.floatingRoot));
  const overlayRoot = createRoot(ensureRoot(OVERLAY_IDS.overlayRoot));
  let hasLoadedDefaultWriteMode = false;

  async function syncDefaultWriteMode(): Promise<void> {
    if (hasLoadedDefaultWriteMode) {
      return;
    }

    hasLoadedDefaultWriteMode = true;
    try {
      const configuration = await readPluginConfiguration(PLUGIN_UNIQUE_ID);
      setSubtitleWriteMode(normalizeWriteMode(configuration.DefaultSubtitleWriteMode));
    } catch {
      // 配置读取失败时保留默认内封模式，不阻塞详情页控制台。
    }
  }

  async function refreshOverlayData(): Promise<void> {
    const currentItemId = getOverlayState().itemId ?? getCurrentItemId();
    if (!currentItemId) {
      throw new Error('当前页面没有可管理的媒体详情。');
    }

    const payload = await fetchPartsPayload(currentItemId);
    applyPartsPayload(currentItemId, payload);
  }

  async function searchCurrentPart(): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效分段。');
    }

    setStatus(`正在为 ${activePart.Label} 搜索字幕候选…`, 'busy');
    const payload = await searchPart(snapshot.itemId, activePart);
    setSearchResults(activePart.Id, payload.Items ?? []);
    applyOperationResultToPart(activePart.Id, payload);
    await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, payload));
    setStatus(`已找到 ${(payload.Items ?? []).length} 条字幕候选。`, 'success');
  }

  async function convertCurrentPart(): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效分段。');
    }

    setStatus(`正在把 ${activePart.Label} 转换为 MKV…`, 'busy');
    const result = await convertPart(snapshot.itemId, activePart);
    setBatchResults([]);
    applyOperationResultToPart(activePart.Id, result);
    await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, result));
    setStatus(result.Message || '当前分段转换完成。', 'success');
  }

  async function convertCurrentGroup(): Promise<void> {
    const snapshot = getOverlayState();
    if (!snapshot.itemId) {
      throw new Error('当前页面没有可管理的媒体详情。');
    }

    setStatus('正在按顺序转换整组分段为 MKV…', 'busy');
    const payload = await convertGroup(snapshot.itemId);
    const items = payload.Items ?? [];
    setBatchResults(items);
    applyBatchResults(items);
    await refreshOverlayDataWithRetry(createBatchRefreshValidator(items));
    setStatus(payload.Message || '整组转换完成。', payload.Status === 'completed' ? 'success' : 'idle');
  }

  async function downloadCurrentCandidate(candidateId: string): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效分段。');
    }

    const candidate = (snapshot.searchResults.get(activePart.Id) ?? []).find(item => item.Id === candidateId);
    if (!candidate) {
      throw new Error('未找到对应的字幕候选。');
    }

    const writeMode = snapshot.subtitleWriteMode;
    const writeModeLabel = getSubtitleWriteModeLabel(writeMode);
    setStatus(`正在下载并写入 ${candidate.DisplayName || candidate.Name || '当前候选字幕'}，模式：${writeModeLabel}…`, 'busy');
    const result = await downloadCandidate(snapshot.itemId, activePart, candidate, writeMode);
    if (result.Status !== 'embedded' && result.Status !== 'sidecar') {
      throw new Error(result.Message || '字幕写入失败。');
    }

    setBatchResults([]);
    applyOperationResultToPart(activePart.Id, result);
    await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, result));
    setStatus(result.Message || `字幕已按${writeModeLabel}模式写入当前分段。`, 'success');
  }

  async function removeEmbeddedSubtitle(streamIndex: number): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效分段。');
    }

    const confirmed = window.confirm(`确认删除内封字幕流 #${streamIndex} 吗？当前只允许删除插件写入的字幕流。`);
    if (!confirmed) {
      setStatus('已取消删除。', 'idle');
      return;
    }

    setStatus(`正在删除内封字幕流 #${streamIndex}…`, 'busy');
    const result = await deletePartEmbeddedSubtitle(snapshot.itemId, activePart, streamIndex);
    applyDeleteResultToPart(activePart.Id, streamIndex);
    await refreshOverlayDataWithRetry(payload => {
      const refreshedPart = payload.Parts.find(part => part.Id === activePart.Id);
      return !(refreshedPart?.EmbeddedSubtitles ?? []).some(track => track.StreamIndex === streamIndex);
    });
    setStatus(result.Message || `已删除内封字幕流 #${streamIndex}。`, 'success');
  }

  async function runDownloadBest(): Promise<void> {
    const snapshot = getOverlayState();
    if (!snapshot.itemId) {
      throw new Error('当前页面没有可管理的媒体详情。');
    }

    const writeModeLabel = getSubtitleWriteModeLabel(snapshot.subtitleWriteMode);
    setStatus(`正在为整组分段搜索最佳字幕并写入${writeModeLabel}…`, 'busy');
    const payload = await downloadBest(snapshot.itemId, snapshot.subtitleWriteMode);
    const items = payload.Items ?? [];
    setBatchResults(items);
    applyBatchResults(items);
    await refreshOverlayDataWithRetry(createBatchRefreshValidator(items));
    setStatus(payload.Message || `整组字幕${writeModeLabel}写入完成。`, payload.Status === 'completed' ? 'success' : 'idle');
  }

  async function withBusyState(task: () => Promise<void>): Promise<void> {
    try {
      setBusy(true);
      await task();
    } catch (error) {
      setStatus(extractErrorMessage(error), 'error');
    } finally {
      setBusy(false);
    }
  }

  function render(): void {
    const snapshot = getOverlayState();
    floatingRoot.render(<FloatingButton onOpen={openOverlay} visible={snapshot.isFabVisible} />);
    overlayRoot.render(
      <OverlayApp
        actions={{
          closeOverlay,
          convertCurrentPart: () => withBusyState(convertCurrentPart),
          convertGroup: () => withBusyState(convertCurrentGroup),
          deleteEmbeddedSubtitle: (streamIndex: number) => withBusyState(() => removeEmbeddedSubtitle(streamIndex)),
          downloadBest: () => withBusyState(runDownloadBest),
          downloadCandidate: (candidateId: string) => withBusyState(() => downloadCurrentCandidate(candidateId)),
          refresh: () => withBusyState(async () => {
            setStatus('正在刷新分段状态…', 'busy');
            await refreshOverlayData();
            setStatus('分段状态已刷新。', 'success');
          }),
          searchCurrentPart: () => withBusyState(searchCurrentPart),
          selectPart: (partId: string) => {
            patchOverlayState({
              activePartId: partId,
              statusMessage: '',
              statusTone: 'idle'
            });
          },
          setSubtitleWriteMode: (mode: SubtitleWriteMode) => {
            setSubtitleWriteMode(mode);
            setStatus(`当前写入模式已切换为${getSubtitleWriteModeLabel(mode)}。`, 'idle');
          }
        }}
        state={snapshot}
      />
    );
  }

  subscribe(render);
  render();

  window.addEventListener('hashchange', () => {
    void refreshCurrentPageState(true);
  });
  window.addEventListener('popstate', () => {
    void refreshCurrentPageState(true);
  });
  window.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
      closeOverlay();
    }
  });

  window.setInterval(() => {
    void refreshCurrentPageState(false);
  }, ROUTE_POLL_MS);

  void syncDefaultWriteMode();
  void refreshCurrentPageState(true);
}
