import { describe, expect, it } from 'vitest';
import { CONFIRM_DIALOG_SELECTOR, shouldCloseOverlayOnEscape } from './keyboard';

describe('overlay keyboard policy', () => {
  it('closes the overlay on Escape when no confirm dialog is open', () => {
    const event = new KeyboardEvent('keydown', { key: 'Escape' });

    expect(shouldCloseOverlayOnEscape(event)).toBe(true);
  });

  it('keeps the overlay open on Escape when a confirm dialog is open', () => {
    const dialog = document.createElement('div');
    dialog.setAttribute('data-subtitles-tools-confirm-dialog', 'true');
    document.body.appendChild(dialog);

    try {
      const event = new KeyboardEvent('keydown', { key: 'Escape' });
      expect(shouldCloseOverlayOnEscape(event)).toBe(false);
    } finally {
      document.body.removeChild(dialog);
    }
  });

  it('ignores non-Escape keys', () => {
    const event = new KeyboardEvent('keydown', { key: 'Enter' });

    expect(shouldCloseOverlayOnEscape(event)).toBe(false);
    expect(document.querySelector(CONFIRM_DIALOG_SELECTOR)).toBeNull();
  });
});
