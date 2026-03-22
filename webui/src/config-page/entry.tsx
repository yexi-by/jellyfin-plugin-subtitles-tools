import { createRoot, type Root } from 'react-dom/client';
import { ConfigPageApp } from './App';

const roots = new WeakMap<Element, Root>();
const initializedViews = new WeakSet<Element>();

function renderConfigPage(root: Root): void {
  root.render(<ConfigPageApp />);
}

function ensureHost(view: Element): HTMLElement {
  const existingHost = view.querySelector<HTMLElement>('#SubtitlesToolsConfigReactRoot');
  if (existingHost) {
    return existingHost;
  }

  const host = document.createElement('div');
  host.id = 'SubtitlesToolsConfigReactRoot';
  view.appendChild(host);
  return host;
}

export default function subtitlesToolsConfigController(view: Element): void {
  let root = roots.get(view);
  if (!root) {
    root = createRoot(ensureHost(view));
    roots.set(view, root);
  }

  if (!initializedViews.has(view)) {
    const currentRoot = root;
    view.addEventListener('viewshow', () => {
      renderConfigPage(currentRoot);
    });
    initializedViews.add(view);
  }

  renderConfigPage(root);
}
