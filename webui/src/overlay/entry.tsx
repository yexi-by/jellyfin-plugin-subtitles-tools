import { createRoot } from 'react-dom/client';
import '../design-system/theme.css';
import { OVERLAY_IDS, PLUGIN_UNIQUE_ID, ROUTE_POLL_MS } from '../shared/constants';
import { getCurrentItemId } from '../shared/dom';
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

function getWriteModeMessages(mode: SubtitleWriteMode): { busyMessage: string; successMessage: string; successTitle: string } {
  if (mode === 'sidecar') {
    return {
      busyMessage: '字幕会保存到视频同目录。',
      successMessage: '字幕文件已写到视频同目录。',
      successTitle: '字幕已保存'
    };
  }

  return {
    busyMessage: '字幕会保存到视频文件中。',
    successMessage: '可以直接在播放器里使用。',
    successTitle: '字幕已写入视频'
  };
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
      // 读取配置失败时保留默认模式，不阻塞详情页入口。
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
      throw new Error('当前没有选中的有效文件。');
    }

    setStatus('正在搜索字幕', '正在查找可用字幕结果。', 'busy');
    const payload = await searchPart(snapshot.itemId, activePart);
    const items = payload.Items ?? [];
    setSearchResults(activePart.Id, items);
    applyOperationResultToPart(activePart.Id, payload);
    await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, payload));

    if (items.length > 0) {
      setStatus('已找到字幕', `共找到 ${items.length} 条可用结果。`, 'success');
      return;
    }

    setStatus('没有找到字幕', '可以稍后再试，或先优化当前文件。', 'idle');
  }

  async function convertCurrentPart(): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效文件。');
    }

    setStatus('正在优化当前文件', '这一步可能需要一点时间。', 'busy');
    const result = await convertPart(snapshot.itemId, activePart);
    setBatchResults([]);
    applyOperationResultToPart(activePart.Id, result);
    await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, result));
    setStatus('当前文件已优化', '现在可以继续搜索或保存字幕。', 'success');
  }

  async function convertCurrentGroup(): Promise<void> {
    const snapshot = getOverlayState();
    if (!snapshot.itemId) {
      throw new Error('当前页面没有可管理的媒体详情。');
    }

    setStatus('正在优化整组文件', '请不要关闭当前页面。', 'busy');
    const payload = await convertGroup(snapshot.itemId);
    const items = payload.Items ?? [];
    setBatchResults(items);
    applyBatchResults(items);
    await refreshOverlayDataWithRetry(createBatchRefreshValidator(items));
    setStatus('整组已优化', '全部文件已完成兼容性处理。', 'success');
  }

  async function downloadCurrentCandidate(candidateId: string): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效文件。');
    }

    const candidate = (snapshot.searchResults.get(activePart.Id) ?? []).find(item => item.Id === candidateId);
    if (!candidate) {
      throw new Error('未找到对应的字幕结果。');
    }

    const writeMode = snapshot.subtitleWriteMode;
    const writeModeMessages = getWriteModeMessages(writeMode);
    const busyTitle = writeMode === 'sidecar' ? '正在保存字幕文件' : '正在写入视频';
    setStatus(busyTitle, writeModeMessages.busyMessage, 'busy');
    const result = await downloadCandidate(snapshot.itemId, activePart, candidate, writeMode);
    if (result.Status !== 'embedded' && result.Status !== 'sidecar') {
      throw new Error('保存失败。');
    }

    setBatchResults([]);
    applyOperationResultToPart(activePart.Id, result);
    await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, result));
    setStatus(writeModeMessages.successTitle, writeModeMessages.successMessage, 'success');
  }

  async function removeEmbeddedSubtitle(streamIndex: number): Promise<void> {
    const snapshot = getOverlayState();
    const activePart = getActivePart(snapshot);
    if (!snapshot.itemId || !activePart) {
      throw new Error('当前没有选中的有效文件。');
    }

    const confirmed = window.confirm(`确认删除字幕轨道 #${streamIndex} 吗？`);
    if (!confirmed) {
      return;
    }

    setStatus('正在删除字幕', '请稍候。', 'busy');
    await deletePartEmbeddedSubtitle(snapshot.itemId, activePart, streamIndex);
    applyDeleteResultToPart(activePart.Id, streamIndex);
    await refreshOverlayDataWithRetry(payload => {
      const refreshedPart = payload.Parts.find(part => part.Id === activePart.Id);
      return !(refreshedPart?.EmbeddedSubtitles ?? []).some(track => track.StreamIndex === streamIndex);
    });
    setStatus('字幕已删除', '当前列表已经更新。', 'success');
  }

  async function runDownloadBest(): Promise<void> {
    const snapshot = getOverlayState();
    if (!snapshot.itemId) {
      throw new Error('当前页面没有可管理的媒体详情。');
    }

    setStatus('正在处理整组字幕', '系统会为每个文件自动选择字幕。', 'busy');
    const payload = await downloadBest(snapshot.itemId, snapshot.subtitleWriteMode);
    const items = payload.Items ?? [];
    setBatchResults(items);
    applyBatchResults(items);
    await refreshOverlayDataWithRetry(createBatchRefreshValidator(items));
    setStatus('整组字幕已处理', '请在最近处理结果里查看每个文件的状态。', payload.Status === 'completed' ? 'success' : 'idle');
  }

  async function withBusyState(
    task: () => Promise<void>,
    failureState: {
      message: string;
      title: string;
    }
  ): Promise<void> {
    try {
      patchOverlayState({ busy: true });
      await task();
    } catch {
      setStatus(failureState.title, failureState.message, 'error');
    } finally {
      patchOverlayState({ busy: false });
    }
  }

  function render(): void {
    const snapshot = getOverlayState();
    floatingRoot.render(<FloatingButton onOpen={openOverlay} visible={snapshot.isFabVisible} />);
    overlayRoot.render(
      <OverlayApp
        actions={{
          closeOverlay,
          convertCurrentPart: () => withBusyState(convertCurrentPart, {
            title: '优化失败',
            message: '请检查视频文件和转码环境。'
          }),
          convertGroup: () => withBusyState(convertCurrentGroup, {
            title: '优化失败',
            message: '请检查视频文件和转码环境。'
          }),
          deleteEmbeddedSubtitle: (streamIndex: number) => withBusyState(() => removeEmbeddedSubtitle(streamIndex), {
            title: '删除失败',
            message: '请稍后重试。'
          }),
          downloadBest: () => withBusyState(runDownloadBest, {
            title: '保存失败',
            message: '请稍后重试。'
          }),
          downloadCandidate: (candidateId: string) => withBusyState(() => downloadCurrentCandidate(candidateId), {
            title: '保存失败',
            message: '请稍后重试。'
          }),
          refresh: () => withBusyState(async () => {
            setStatus('正在读取当前状态', '请稍候。', 'busy');
            await refreshOverlayData();
            setStatus('状态已更新', '当前文件信息已刷新。', 'success');
          }, {
            title: '读取媒体失败',
            message: '请刷新页面后重试。'
          }),
          searchCurrentPart: () => withBusyState(searchCurrentPart, {
            title: '搜索失败',
            message: '请检查服务连接后重试。'
          }),
          selectPart: (partId: string) => {
            patchOverlayState({
              activePartId: partId,
              statusTitle: '',
              statusMessage: '',
              statusTone: 'idle'
            });
          },
          setSubtitleWriteMode: (mode: SubtitleWriteMode) => {
            setSubtitleWriteMode(mode);
            setStatus('保存方式已切换', '接下来会按当前方式保存字幕。', 'idle');
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
