import { beforeEach, describe, expect, it, vi } from 'vitest';

describe('overlay store error recovery', () => {
  beforeEach(() => {
    vi.resetModules();
    window.history.replaceState({}, '', '/web/index.html#/details?id=item-1');
  });

  it('keeps the floating entry visible when loading parts fails', async () => {
    vi.doMock('../shared/dom', () => ({
      getCurrentItemId: () => 'item-1',
      isCurrentDetailsRoute: () => true,
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      readItemType: vi.fn(async () => 'Movie'),
      requestJson: vi.fn(async () => {
        throw new Error('请求失败，状态码 500。');
      })
    }));

    const store = await import('./store');
    await store.refreshCurrentPageState(true);

    expect(store.getOverlayState()).toMatchObject({
      itemId: 'item-1',
      isFabVisible: true,
      statusTitle: '读取媒体失败',
      statusTone: 'error'
    });
  });

  it('applies sidecar write results to the active part', async () => {
    vi.doMock('../shared/dom', () => ({
      getCurrentItemId: () => 'item-1',
      isCurrentDetailsRoute: () => true,
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      readItemType: vi.fn(async () => 'Movie'),
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
      Message: '字幕已保存到视频同目录。',
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

  it('retries loading when the details route is ready before the item id is ready', async () => {
    const getCurrentItemId = vi
      .fn<() => string | null>()
      .mockReturnValueOnce(null)
      .mockReturnValueOnce('item-1');
    const requestJson = vi.fn(async () => ({
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
          Label: '单文件',
          MediaPath: '/media/movie.mkv'
        }
      ]
    }));

    vi.doMock('../shared/dom', () => ({
      getCurrentItemId,
      isCurrentDetailsRoute: () => true,
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      readItemType: vi.fn(async () => 'Movie'),
      requestJson
    }));

    const store = await import('./store');
    await store.refreshCurrentPageState(true);
    expect(store.getOverlayState()).toMatchObject({
      isFabVisible: false,
      itemData: null,
      itemId: null,
      lastLocation: ''
    });

    await store.refreshCurrentPageState(false);
    expect(requestJson).toHaveBeenCalledTimes(1);
    expect(store.getOverlayState()).toMatchObject({
      activePartId: 'part-1',
      isFabVisible: true,
      itemId: 'item-1'
    });
    expect(store.getOverlayState().itemData?.Name).toBe('示例影片');
  });

  it('retries the same details route when the first load fails before the page is fully ready', async () => {
    const requestJson = vi
      .fn()
      .mockRejectedValueOnce(new Error('请求失败，状态码 401。'))
      .mockResolvedValueOnce({
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
            Label: '单文件',
            MediaPath: '/media/movie.mkv'
          }
        ]
      });

    vi.doMock('../shared/dom', () => ({
      getCurrentItemId: () => 'item-1',
      isCurrentDetailsRoute: () => true,
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      readItemType: vi.fn(async () => 'Movie'),
      requestJson
    }));

    const store = await import('./store');
    await store.refreshCurrentPageState(true);
    expect(store.getOverlayState()).toMatchObject({
      isFabVisible: true,
      itemData: null,
      itemId: 'item-1',
      statusTone: 'error'
    });

    await store.refreshCurrentPageState(false);
    expect(requestJson).toHaveBeenCalledTimes(2);
    expect(store.getOverlayState()).toMatchObject({
      activePartId: 'part-1',
      isFabVisible: true,
      itemId: 'item-1',
      statusMessage: '',
      statusTitle: '',
      statusTone: 'idle'
    });
    expect(store.getOverlayState().itemData?.Name).toBe('示例影片');
  });

  it('hides the floating entry on person details pages', async () => {
    const requestJson = vi.fn();

    vi.doMock('../shared/dom', () => ({
      getCurrentItemId: () => 'person-1',
      isCurrentDetailsRoute: () => true,
      sleep: async () => undefined
    }));
    vi.doMock('../shared/runtime', () => ({
      readItemType: vi.fn(async () => 'Person'),
      requestJson
    }));

    const store = await import('./store');
    await store.refreshCurrentPageState(true);

    expect(requestJson).not.toHaveBeenCalled();
    expect(store.getOverlayState()).toMatchObject({
      isFabVisible: false,
      isOverlayOpen: false,
      itemData: null,
      itemId: null
    });
  });
});
