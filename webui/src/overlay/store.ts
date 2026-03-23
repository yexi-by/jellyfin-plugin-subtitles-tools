import { API_ROOT, REFRESH_RETRY_ATTEMPTS, REFRESH_RETRY_DELAY_MS } from '../shared/constants';
import { getCurrentItemId, sleep } from '../shared/dom';
import { extractErrorMessage } from '../shared/errors';
import { requestJson } from '../shared/runtime';
import type {
  ItemPartsPayload,
  MediaPart,
  OperationBatchPayload,
  OperationResultItem,
  OverlayViewState,
  SearchOperationPayload,
  SubtitleCandidate,
  SubtitleWriteMode
} from '../shared/types';

export interface OverlayStoreState extends OverlayViewState {
  isFabVisible: boolean;
  isOverlayOpen: boolean;
}

type Listener = () => void;

const initialState: OverlayStoreState = {
  activePartId: null,
  busy: false,
  isFabVisible: false,
  isOverlayOpen: false,
  itemData: null,
  itemId: null,
  subtitleWriteMode: 'embedded',
  lastBatchItems: [],
  lastLocation: '',
  searchResults: new Map<string, SubtitleCandidate[]>(),
  statusMessage: '',
  statusTone: 'idle'
};

const listeners = new Set<Listener>();
let state: OverlayStoreState = initialState;

function notify(): void {
  listeners.forEach(listener => listener());
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function getOverlayState(): OverlayStoreState {
  return state;
}

export function patchOverlayState(update: Partial<OverlayStoreState>): void {
  state = { ...state, ...update };
  notify();
}

export function replaceOverlayState(nextState: OverlayStoreState): void {
  state = nextState;
  notify();
}

export function getActivePart(snapshot: OverlayStoreState = state): MediaPart | null {
  if (!snapshot.itemData || !snapshot.activePartId) {
    return null;
  }

  return snapshot.itemData.Parts.find(part => part.Id === snapshot.activePartId) ?? null;
}

export function setBusy(busy: boolean): void {
  patchOverlayState({ busy });
}

export function setStatus(message: string, tone: OverlayStoreState['statusTone']): void {
  patchOverlayState({
    statusMessage: message,
    statusTone: tone
  });
}

export function setSubtitleWriteMode(mode: SubtitleWriteMode): void {
  patchOverlayState({ subtitleWriteMode: mode });
}

export async function fetchPartsPayload(itemId: string): Promise<ItemPartsPayload> {
  return requestJson<ItemPartsPayload>(`${API_ROOT}/Items/${itemId}/parts`, 'GET');
}

export function applyPartsPayload(itemId: string, payload: ItemPartsPayload): void {
  const isSameItem = state.itemId === itemId;
  const parts = payload.Parts ?? [];
  const nextActivePartId = isSameItem && state.activePartId && parts.some(part => part.Id === state.activePartId)
    ? state.activePartId
    : payload.CurrentPartId ?? parts[0]?.Id ?? null;

  state = {
    ...state,
    activePartId: nextActivePartId,
    itemData: payload,
    itemId,
    lastBatchItems: isSameItem ? state.lastBatchItems : [],
    searchResults: isSameItem ? state.searchResults : new Map<string, SubtitleCandidate[]>(),
    statusMessage: isSameItem ? state.statusMessage : '',
    statusTone: isSameItem ? state.statusTone : 'idle'
  };
  notify();
}

export async function fetchParts(itemId: string, forceReload: boolean): Promise<ItemPartsPayload> {
  if (!forceReload && state.itemData && state.itemId === itemId) {
    return state.itemData;
  }

  const payload = await fetchPartsPayload(itemId);
  applyPartsPayload(itemId, payload);
  return payload;
}

export async function refreshCurrentPageState(forceReload: boolean): Promise<void> {
  const locationSignature = `${window.location.pathname}|${window.location.search}|${window.location.hash}`;
  if (!forceReload && state.lastLocation === locationSignature) {
    return;
  }

  patchOverlayState({ lastLocation: locationSignature });
  const itemId = getCurrentItemId();
  if (!itemId) {
    replaceOverlayState({
      ...initialState,
      lastLocation: locationSignature,
      subtitleWriteMode: state.subtitleWriteMode
    });
    return;
  }

  try {
    await fetchParts(itemId, forceReload);
    patchOverlayState({ isFabVisible: true });
  } catch (error) {
    replaceOverlayState({
      ...initialState,
      isFabVisible: true,
      isOverlayOpen: state.isOverlayOpen,
      itemId,
      lastLocation: locationSignature,
      subtitleWriteMode: state.subtitleWriteMode,
      statusMessage: extractErrorMessage(error),
      statusTone: 'error'
    });
  }
}

export function updateManagedPart(partId: string, updater: (part: MediaPart) => MediaPart): void {
  if (!state.itemData) {
    return;
  }

  patchOverlayState({
    itemData: {
      ...state.itemData,
      Parts: state.itemData.Parts.map(part => {
        if (part.Id !== partId) {
          return part;
        }

        return updater({ ...part });
      })
    }
  });
}

export function applyOperationResultToPart(partId: string | undefined, result: OperationResultItem): void {
  if (!partId) {
    return;
  }

  updateManagedPart(partId, part => {
    if (result.MediaPath) {
      part.MediaPath = result.MediaPath;
      part.FileName = result.MediaPath.split(/[\\/]/).at(-1) ?? part.FileName;
    }

    if (result.Container) {
      part.Container = result.Container;
    }

    if (result.IsManaged === true) {
      part.IsManaged = true;
      if (part.Container === 'mkv') {
        part.ReadIdentityFromMetadata = true;
      }
    }

    if (result.RiskVerdict) {
      part.RiskVerdict = result.RiskVerdict;
    }

    if (typeof result.Pipeline === 'string') {
      part.Pipeline = result.Pipeline;
    }

    if (typeof result.NeedsCompatibilityRepair === 'boolean') {
      part.NeedsCompatibilityRepair = result.NeedsCompatibilityRepair;
    }

    if (result.EmbeddedSubtitle) {
      const nonPluginTracks = (part.EmbeddedSubtitles ?? []).filter(track => track.IsPluginManaged !== true);
      part.EmbeddedSubtitles = [...nonPluginTracks, result.EmbeddedSubtitle];
    }

    if (result.ExternalSubtitle) {
      const nextTrack = result.ExternalSubtitle;
      const remainingTracks = (part.ExternalSubtitles ?? []).filter(track => track.FilePath !== nextTrack.FilePath);
      part.ExternalSubtitles = [...remainingTracks, nextTrack];
    }

    return part;
  });
}

export function applyDeleteResultToPart(partId: string, deletedStreamIndex: number): void {
  updateManagedPart(partId, part => {
    part.EmbeddedSubtitles = (part.EmbeddedSubtitles ?? []).filter(track => track.StreamIndex !== deletedStreamIndex);
    return part;
  });
}

export function applyBatchResults(items: OperationResultItem[]): void {
  items.forEach(item => {
    if (item.IsManaged === true || item.Status === 'converted' || item.Status === 'embedded' || item.Status === 'sidecar') {
      applyOperationResultToPart(item.PartId, item);
    }
  });
}

export function createBatchRefreshValidator(items: OperationResultItem[]): (payload: ItemPartsPayload) => boolean {
  return payload => {
    const successfulItems = items.filter(item => {
      return item.IsManaged === true || item.Status === 'converted' || item.Status === 'embedded' || item.Status === 'sidecar';
    });
    if (successfulItems.length === 0) {
      return true;
    }

    return successfulItems.every(item => {
      const part = payload.Parts.find(payloadPart => payloadPart.Id === item.PartId);
      if (!part) {
        return false;
      }

      if (item.IsManaged === true && part.IsManaged !== true) {
        return false;
      }

      if (item.MediaPath && part.MediaPath !== item.MediaPath) {
        return false;
      }

      if (item.Container && part.Container !== item.Container) {
        return false;
      }

      if (item.RiskVerdict && part.RiskVerdict !== item.RiskVerdict) {
        return false;
      }

      if (typeof item.NeedsCompatibilityRepair === 'boolean' && part.NeedsCompatibilityRepair !== item.NeedsCompatibilityRepair) {
        return false;
      }

      if (typeof item.Pipeline === 'string' && item.Pipeline !== part.Pipeline) {
        return false;
      }

      if (item.Status === 'embedded' && item.EmbeddedSubtitle) {
        return (part.EmbeddedSubtitles ?? []).some(track => {
          return track.Title === item.EmbeddedSubtitle?.Title && track.Language === item.EmbeddedSubtitle?.Language;
        });
      }

      if (item.Status === 'sidecar' && item.ExternalSubtitle) {
        return (part.ExternalSubtitles ?? []).some(track => {
          return track.FilePath === item.ExternalSubtitle?.FilePath;
        });
      }

      return true;
    });
  };
}

export function createSinglePartRefreshValidator(partId: string, expectedResult: OperationResultItem): (payload: ItemPartsPayload) => boolean {
  return payload => {
    const part = payload.Parts.find(candidate => candidate.Id === partId);
    if (!part) {
      return false;
    }

    if (expectedResult.IsManaged === true && part.IsManaged !== true) {
      return false;
    }

    if (expectedResult.MediaPath && part.MediaPath !== expectedResult.MediaPath) {
      return false;
    }

    if (expectedResult.Container && part.Container !== expectedResult.Container) {
      return false;
    }

    if (expectedResult.RiskVerdict && part.RiskVerdict !== expectedResult.RiskVerdict) {
      return false;
    }

    if (typeof expectedResult.NeedsCompatibilityRepair === 'boolean' && part.NeedsCompatibilityRepair !== expectedResult.NeedsCompatibilityRepair) {
      return false;
    }

    if (typeof expectedResult.Pipeline === 'string' && part.Pipeline !== expectedResult.Pipeline) {
      return false;
    }

    if (expectedResult.EmbeddedSubtitle) {
      return (part.EmbeddedSubtitles ?? []).some(track => {
        return track.Title === expectedResult.EmbeddedSubtitle?.Title && track.Language === expectedResult.EmbeddedSubtitle?.Language;
      });
    }

    if (expectedResult.ExternalSubtitle) {
      return (part.ExternalSubtitles ?? []).some(track => {
        return track.FilePath === expectedResult.ExternalSubtitle?.FilePath;
      });
    }

    return true;
  };
}

export async function refreshOverlayDataWithRetry(validatePayload?: (payload: ItemPartsPayload) => boolean): Promise<void> {
  const currentItemId = state.itemId ?? getCurrentItemId();
  if (!currentItemId) {
    throw new Error('当前页面没有可管理的媒体详情。');
  }

  let lastError: unknown = null;
  for (let attempt = 0; attempt < REFRESH_RETRY_ATTEMPTS; attempt += 1) {
    try {
      const payload = await fetchPartsPayload(currentItemId);
      if (!validatePayload || validatePayload(payload)) {
        applyPartsPayload(currentItemId, payload);
        return;
      }
    } catch (error) {
      lastError = error;
    }

    if (attempt < REFRESH_RETRY_ATTEMPTS - 1) {
      await sleep(REFRESH_RETRY_DELAY_MS);
    }
  }

  if (lastError instanceof Error) {
    throw lastError;
  }
}

export async function searchPart(itemId: string, part: MediaPart): Promise<SearchOperationPayload> {
  return requestJson<SearchOperationPayload>(`${API_ROOT}/Items/${itemId}/parts/${part.Id}/search`, 'POST', {});
}

export async function convertPart(itemId: string, part: MediaPart): Promise<OperationResultItem> {
  return requestJson<OperationResultItem>(`${API_ROOT}/Items/${itemId}/parts/${part.Id}/convert`, 'POST', {});
}

export async function convertGroup(itemId: string): Promise<OperationBatchPayload> {
  return requestJson<OperationBatchPayload>(`${API_ROOT}/Items/${itemId}/convert-group`, 'POST', {});
}

export async function downloadCandidate(
  itemId: string,
  part: MediaPart,
  candidate: SubtitleCandidate,
  writeMode: SubtitleWriteMode
): Promise<OperationResultItem> {
  return requestJson<OperationResultItem>(`${API_ROOT}/Items/${itemId}/parts/${part.Id}/download`, 'POST', {
    Ext: candidate.Ext,
    Language: candidate.Language,
    Languages: candidate.Languages,
    Name: candidate.Name,
    SubtitleId: candidate.Id,
    WriteMode: writeMode
  });
}

export async function deletePartEmbeddedSubtitle(itemId: string, part: MediaPart, streamIndex: number): Promise<OperationResultItem> {
  return requestJson<OperationResultItem>(`${API_ROOT}/Items/${itemId}/parts/${part.Id}/delete-embedded-subtitle`, 'POST', {
    StreamIndex: streamIndex
  });
}

export async function downloadBest(itemId: string, writeMode: SubtitleWriteMode): Promise<OperationBatchPayload> {
  return requestJson<OperationBatchPayload>(`${API_ROOT}/Items/${itemId}/download-best`, 'POST', {
    WriteMode: writeMode
  });
}

export function setSearchResults(partId: string, results: SubtitleCandidate[]): void {
  const nextResults = new Map(state.searchResults);
  nextResults.set(partId, results);
  patchOverlayState({
    lastBatchItems: [],
    searchResults: nextResults
  });
}

export function setBatchResults(items: OperationResultItem[]): void {
  patchOverlayState({
    lastBatchItems: items
  });
}

export function openOverlay(): void {
  patchOverlayState({ isOverlayOpen: true });
}

export function closeOverlay(): void {
  patchOverlayState({ isOverlayOpen: false });
}
