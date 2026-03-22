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
});
