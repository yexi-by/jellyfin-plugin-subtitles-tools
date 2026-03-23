import type { PluginConfiguration } from './types';

export interface JellyfinAjaxResponse {
  json: () => Promise<unknown>;
  ok: boolean;
  status: number;
}

interface JellyfinServerInfo {
  UserId?: string;
}

export interface JellyfinApiClient {
  _serverInfo?: JellyfinServerInfo;
  ajax: (options: {
    contentType?: string;
    data?: string;
    type: 'GET' | 'POST';
    url: string;
  }) => Promise<JellyfinAjaxResponse>;
  getCurrentUserId?: () => string | null;
  getPluginConfiguration: (pluginId: string) => Promise<PluginConfiguration>;
  getUrl: (path: string) => string;
  updatePluginConfiguration: (pluginId: string, config: PluginConfiguration) => Promise<unknown>;
}

export interface JellyfinDashboard {
  hideLoadingMsg: () => void;
  processErrorResponse: (response: { statusText: string }) => void;
  processPluginConfigurationUpdateResult: (result: unknown) => void;
  showLoadingMsg: () => void;
}

function getApiClient(): JellyfinApiClient {
  if (!window.ApiClient) {
    throw new Error('Jellyfin ApiClient 尚未准备完成。');
  }

  return window.ApiClient;
}

function getDashboard(): JellyfinDashboard {
  if (!window.Dashboard) {
    throw new Error('Jellyfin Dashboard 尚未准备完成。');
  }

  return window.Dashboard;
}

export async function requestJson<T>(path: string, method: 'GET' | 'POST', body?: unknown): Promise<T> {
  const apiClient = getApiClient();
  const response = await apiClient.ajax({
    type: method,
    url: apiClient.getUrl(path),
    data: body === undefined ? undefined : JSON.stringify(body),
    contentType: body === undefined ? undefined : 'application/json'
  });

  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    const errorMessage = typeof payload === 'object' && payload !== null
      ? String(
        'Message' in payload && typeof payload.Message === 'string'
          ? payload.Message
          : 'message' in payload && typeof payload.message === 'string'
            ? payload.message
            : `请求失败，状态码 ${response.status}。`
      )
      : `请求失败，状态码 ${response.status}。`;
    throw new Error(errorMessage);
  }

  return payload as T;
}

export function readPluginConfiguration(pluginId: string): Promise<PluginConfiguration> {
  return getApiClient().getPluginConfiguration(pluginId);
}

export async function readItemType(itemId: string): Promise<string | null> {
  const apiClient = getApiClient();
  const userId = apiClient.getCurrentUserId?.() ?? apiClient._serverInfo?.UserId ?? null;
  if (!userId) {
    return null;
  }

  const item = await requestJson<{ Type?: string }>(`Users/${userId}/Items/${itemId}`, 'GET');
  return typeof item.Type === 'string' ? item.Type : null;
}

export function savePluginConfiguration(pluginId: string, configuration: PluginConfiguration): Promise<unknown> {
  return getApiClient().updatePluginConfiguration(pluginId, configuration);
}

export function showLoading(): void {
  getDashboard().showLoadingMsg();
}

export function hideLoading(): void {
  getDashboard().hideLoadingMsg();
}

export function showError(message: string): void {
  getDashboard().processErrorResponse({ statusText: message });
}

export function showConfigurationSaved(result: unknown): void {
  getDashboard().processPluginConfigurationUpdateResult(result);
}
