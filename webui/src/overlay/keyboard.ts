export const CONFIRM_DIALOG_SELECTOR = '[data-subtitles-tools-confirm-dialog="true"]';

export function shouldCloseOverlayOnEscape(event: KeyboardEvent, root: Document | Element = document): boolean {
  if (event.key !== 'Escape') {
    return false;
  }

  return root.querySelector(CONFIRM_DIALOG_SELECTOR) === null;
}
