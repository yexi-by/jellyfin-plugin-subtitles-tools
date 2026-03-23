import { beforeEach, describe, expect, it, vi } from 'vitest';

describe('overlay store error recovery', () => {
  beforeEach(() => {
    vi.resetModules();
    window.history.replaceState({}, '', '/web/index.html#/details?id=item-1');
  });

  it('keeps the floating entry visible when loading parts fails', async () => {
    vi.doMock('../shared/dom', () => ({
      getCurrentItemId: () => 'item-1',
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      requestJson: vi.fn(async () => {
        throw new Error('请求失败，状态码 500。');
      })
    }));

    const store = await import('./store');
    await store.refreshCurrentPageState(true);

    expect(store.getOverlayState()).toMatchObject({
      itemId: 'item-1',
      isFabVisible: true,
      statusTone: 'error'
    });
  });

  it('applies sidecar write results to the active part', async () => {
    vi.doMock('../shared/dom', () => ({
      getCurrentItemId: () => 'item-1',
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      requestJson: vi.fn()
    }));

    const store = await import('./store');
    store.applyPartsPayload('item-1', {
      CurrentPartId: 'part-1',
      IsMultipart: false,
      ItemType: 'Movie',
      Name: '示例影片',
      Parts: [
        {
          EmbeddedSubtitles: [],
          ExternalSubtitles: [],
          Id: 'part-1',
          Index: 1,
          IsManaged: true,
          Label: 'Part 1',
          MediaPath: '/media/movie.mkv'
        }
      ]
    });

    store.applyOperationResultToPart('part-1', {
      ExternalSubtitle: {
        FileName: 'movie.zho.srt',
        FilePath: '/media/movie.zho.srt',
        Format: 'srt',
        Language: 'zho'
      },
      Message: '字幕已转为 UTF-8 SRT 并写成外挂字幕。',
      Status: 'sidecar',
      WriteMode: 'sidecar'
    });

    expect(store.getOverlayState().itemData?.Parts[0].ExternalSubtitles).toEqual([
      {
        FileName: 'movie.zho.srt',
        FilePath: '/media/movie.zho.srt',
        Format: 'srt',
        Language: 'zho'
      }
    ]);
  });
});
