import type { JellyfinApiClient, JellyfinDashboard } from './runtime';

declare global {
  interface Window {
    ApiClient?: JellyfinApiClient;
    Dashboard?: JellyfinDashboard;
    __subtitlesToolsGlobalLoaded?: boolean;
  }
}

export {};
