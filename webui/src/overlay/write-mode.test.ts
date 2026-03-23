import { describe, expect, it, vi } from 'vitest';
import { normalizeOverlayWriteMode, openOverlayWithConfiguredWriteMode, syncOverlayWriteModeFromConfiguration } from './write-mode';

describe('overlay write mode helpers', () => {
  it('normalizes configured write mode values', () => {
    expect(normalizeOverlayWriteMode('sidecar')).toBe('sidecar');
    expect(normalizeOverlayWriteMode('embedded')).toBe('embedded');
    expect(normalizeOverlayWriteMode('unexpected')).toBe('embedded');
    expect(normalizeOverlayWriteMode(undefined)).toBe('embedded');
  });

  it('syncs the configured write mode into the overlay state', async () => {
    const applyWriteMode = vi.fn();

    await syncOverlayWriteModeFromConfiguration(
      async () => ({ DefaultSubtitleWriteMode: 'sidecar' }),
      applyWriteMode
    );

    expect(applyWriteMode).toHaveBeenCalledWith('sidecar');
  });

  it('opens the overlay even when reading configuration fails', async () => {
    const applyWriteMode = vi.fn();
    const showOverlay = vi.fn();

    await openOverlayWithConfiguredWriteMode(
      async () => {
        throw new Error('read failed');
      },
      applyWriteMode,
      showOverlay
    );

    expect(applyWriteMode).not.toHaveBeenCalled();
    expect(showOverlay).toHaveBeenCalledTimes(1);
  });
});
