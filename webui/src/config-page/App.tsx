import { startTransition, useEffect, useEffectEvent, useState, type JSX } from 'react';
import {
  Button,
  ConnectionMetrics,
  FieldShell,
  Panel,
  SectionHeading,
  StatusBanner,
  WriteModeSwitch
} from '../design-system/components';
import '../design-system/theme.css';
import { API_ROOT, PLUGIN_UNIQUE_ID } from '../shared/constants';
import { extractErrorMessage } from '../shared/errors';
import {
  buildErrorConnectionStatus,
  buildIdleConnectionStatus,
  buildLoadingConnectionStatus,
  buildSuccessConnectionStatus
} from '../shared/formatters';
import {
  hideLoading,
  readPluginConfiguration,
  requestJson,
  savePluginConfiguration,
  showConfigurationSaved,
  showError,
  showLoading
} from '../shared/runtime';
import type { ConnectionHealthPayload, ConnectionStatusViewModel, PluginConfiguration, SubtitleWriteMode } from '../shared/types';

const defaultConfiguration: PluginConfiguration = {
  AutoPreprocessPathBlacklist: [],
  DefaultSubtitleWriteMode: 'embedded',
  EnableAutoVideoConvertToMkv: true,
  FfmpegExecutablePath: '',
  QsvRenderDevicePath: '/dev/dri/renderD128',
  RequestTimeoutSeconds: 10,
  SearchCacheTtlSeconds: 86400,
  SubtitleCacheTtlSeconds: 604800,
  ThunderBaseUrl: 'https://api-shoulei-ssl.xunlei.com',
  VideoConvertConcurrency: 1
};

function textInputClassName(): string {
  return 'w-full rounded-md border border-white/10 bg-black/20 px-3 py-2 text-sm text-gray-200 outline-none transition focus:border-blue-500/50 focus:ring-1 focus:ring-blue-500/50';
}

function normalizeWriteMode(value: string | undefined): SubtitleWriteMode {
  return value === 'sidecar' ? 'sidecar' : 'embedded';
}

function normalizeBlacklistPaths(paths: string[] | undefined): string[] {
  if (!Array.isArray(paths)) {
    return [];
  }

  const seenPaths = new Set<string>();
  const normalizedPaths: string[] = [];
  for (const path of paths) {
    const normalizedPath = stripTrailingPathSeparators(path.trim());
    if (!normalizedPath) {
      continue;
    }

    const comparisonPath = stripTrailingPathSeparators(normalizedPath.replaceAll('\\', '/')).toLowerCase();
    if (seenPaths.has(comparisonPath)) {
      continue;
    }

    seenPaths.add(comparisonPath);
    normalizedPaths.push(normalizedPath);
  }

  return normalizedPaths;
}

function stripTrailingPathSeparators(value: string): string {
  let normalizedValue = value;
  while (shouldStripTrailingPathSeparator(normalizedValue)) {
    normalizedValue = normalizedValue.slice(0, -1);
  }

  return normalizedValue;
}

function shouldStripTrailingPathSeparator(value: string): boolean {
  if (value.length <= 1) {
    return false;
  }

  if (!value.endsWith('/') && !value.endsWith('\\')) {
    return false;
  }

  return !/^[A-Za-z]:[\\/]$/u.test(value);
}

function parseBlacklistTextarea(value: string): string[] {
  return normalizeBlacklistPaths(value.split(/\r?\n/u));
}

export function ConfigPageApp(): JSX.Element {
  const [configuration, setConfiguration] = useState<PluginConfiguration>(defaultConfiguration);
  const [blacklistText, setBlacklistText] = useState('');
  const [busy, setBusy] = useState(false);
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatusViewModel>(buildIdleConnectionStatus());

  const loadConfiguration = useEffectEvent(async () => {
    showLoading();

    try {
      const response = await readPluginConfiguration(PLUGIN_UNIQUE_ID);
      const autoPreprocessPathBlacklist = normalizeBlacklistPaths(response.AutoPreprocessPathBlacklist);
      startTransition(() => {
        setConfiguration({
          AutoPreprocessPathBlacklist: autoPreprocessPathBlacklist,
          DefaultSubtitleWriteMode: normalizeWriteMode(response.DefaultSubtitleWriteMode),
          EnableAutoVideoConvertToMkv: response.EnableAutoVideoConvertToMkv !== false,
          FfmpegExecutablePath: response.FfmpegExecutablePath || '',
          QsvRenderDevicePath: response.QsvRenderDevicePath || '/dev/dri/renderD128',
          RequestTimeoutSeconds: response.RequestTimeoutSeconds || 10,
          SearchCacheTtlSeconds: response.SearchCacheTtlSeconds || 86400,
          SubtitleCacheTtlSeconds: response.SubtitleCacheTtlSeconds || 604800,
          ThunderBaseUrl: response.ThunderBaseUrl || 'https://api-shoulei-ssl.xunlei.com',
          VideoConvertConcurrency: response.VideoConvertConcurrency || 1
        });
        setBlacklistText(autoPreprocessPathBlacklist.join('\n'));
        setConnectionStatus(buildIdleConnectionStatus());
      });
    } catch {
      showError('加载配置失败');
    } finally {
      hideLoading();
    }
  });

  useEffect(() => {
    void loadConfiguration();
  }, []);

  async function testConnection(): Promise<void> {
    setBusy(true);
    setConnectionStatus(buildLoadingConnectionStatus());
    showLoading();

    try {
      const response = await requestJson<ConnectionHealthPayload>(`${API_ROOT}/TestConnection`, 'POST', buildConfigurationForSubmit());
      setConnectionStatus(buildSuccessConnectionStatus(response));
    } catch (error) {
      setConnectionStatus(buildErrorConnectionStatus());
      showError(extractErrorMessage(error));
    } finally {
      setBusy(false);
      hideLoading();
    }
  }

  async function saveConfigurationValues(): Promise<void> {
    setBusy(true);
    showLoading();

    try {
      const current = await readPluginConfiguration(PLUGIN_UNIQUE_ID);
      const normalizedConfiguration = buildConfigurationForSubmit();
      const result = await savePluginConfiguration(PLUGIN_UNIQUE_ID, {
        ...current,
        ...normalizedConfiguration
      });
      showConfigurationSaved(result);
      setConfiguration(normalizedConfiguration);
      setBlacklistText(normalizedConfiguration.AutoPreprocessPathBlacklist.join('\n'));
    } catch (error) {
      showError(extractErrorMessage(error));
    } finally {
      setBusy(false);
      hideLoading();
    }
  }

  function updateBlacklistText(value: string): void {
    setBlacklistText(value);
    setConfiguration(current => ({
      ...current,
      AutoPreprocessPathBlacklist: parseBlacklistTextarea(value)
    }));
  }

  function buildConfigurationForSubmit(): PluginConfiguration {
    return {
      ...configuration,
      AutoPreprocessPathBlacklist: parseBlacklistTextarea(blacklistText)
    };
  }

  return (
    <div className="min-h-screen bg-[var(--color-shell-bg)] text-gray-200">
      <div className="mx-auto max-w-4xl px-4 py-6 sm:px-6 sm:py-10">
        <header className="mb-8 sm:mb-10">
          <h1 className="text-2xl font-semibold text-gray-100 sm:text-3xl">字幕工具设置</h1>
          <p className="mt-2 text-sm text-gray-400">检测内置字幕源，设置默认保存方式。</p>
        </header>

        <div className="flex flex-col gap-6">
          <Panel>
            <SectionHeading title="字幕源检测" description="检查内置迅雷字幕源、FFmpeg 与视频兼容性。" />
            <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <span className="text-sm font-medium text-gray-200">运行检测</span>
              <Button className="w-full sm:w-auto" size="sm" disabled={busy} type="button" variant="secondary" onClick={() => void testConnection()}>
                检测字幕源
              </Button>
            </div>
            <StatusBanner
              label={connectionStatus.label}
              message={connectionStatus.message}
              title={connectionStatus.title}
              tone={connectionStatus.tone}
            />
            <div className="mt-3">
              <ConnectionMetrics metrics={connectionStatus.details} />
            </div>
          </Panel>

          <Panel>
            <SectionHeading title="常规设置" description="设置字幕默认保存方式和自动处理行为。" />
            <div className="grid gap-6">
              <FieldShell
                label="默认保存方式"
                description="写入视频会把字幕保存进视频文件；另存字幕会在同目录生成字幕文件。"
              >
                <div className="w-full max-w-xs">
                  <WriteModeSwitch
                    mode={configuration.DefaultSubtitleWriteMode}
                    onChange={mode => setConfiguration(current => ({ ...current, DefaultSubtitleWriteMode: mode }))}
                  />
                </div>
              </FieldShell>

              <label className="flex items-start gap-3">
                <input
                  checked={configuration.EnableAutoVideoConvertToMkv}
                  className="mt-1 h-4 w-4 rounded border-gray-600 bg-black/20 text-blue-500 focus:ring-blue-500/50"
                  type="checkbox"
                  onChange={event => setConfiguration(current => ({ ...current, EnableAutoVideoConvertToMkv: event.target.checked }))}
                />
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium text-gray-200">新视频入库时自动转换</span>
                  <span className="text-xs text-gray-500">开启后，新视频进入媒体库时会自动计算文件指纹；如果需要，还会转为 MKV 或修复播放兼容性。关闭后，只在你手动处理字幕时才会执行这些步骤。</span>
                </div>
              </label>

              <FieldShell
                label="自动转换路径黑名单"
                description="每行填写一个媒体目录。新视频位于这些目录内时，会跳过入库自动转换；手动处理和字幕操作不受影响。"
              >
                <textarea
                  className={`${textInputClassName()} min-h-28 resize-y leading-6`}
                  placeholder="每行一个媒体目录路径"
                  value={blacklistText}
                  onChange={event => updateBlacklistText(event.target.value)}
                />
              </FieldShell>
            </div>
          </Panel>

          <Panel>
            <SectionHeading title="高级设置" description="不确定时保持默认即可。" />
            <div className="grid gap-4 md:grid-cols-2">
              <FieldShell label="迅雷接口地址">
                <input
                  className={textInputClassName()}
                  value={configuration.ThunderBaseUrl}
                  onChange={event => setConfiguration(current => ({ ...current, ThunderBaseUrl: event.target.value }))}
                />
              </FieldShell>
              <FieldShell label="请求超时（秒）">
                <input
                  className={textInputClassName()}
                  max={120}
                  min={1}
                  type="number"
                  value={configuration.RequestTimeoutSeconds}
                  onChange={event => setConfiguration(current => ({ ...current, RequestTimeoutSeconds: Number.parseInt(event.target.value, 10) || 10 }))}
                />
              </FieldShell>
              <FieldShell label="搜索缓存（秒）">
                <input
                  className={textInputClassName()}
                  max={604800}
                  min={60}
                  type="number"
                  value={configuration.SearchCacheTtlSeconds}
                  onChange={event => setConfiguration(current => ({ ...current, SearchCacheTtlSeconds: Number.parseInt(event.target.value, 10) || 86400 }))}
                />
              </FieldShell>
              <FieldShell label="字幕缓存（秒）">
                <input
                  className={textInputClassName()}
                  max={2592000}
                  min={60}
                  type="number"
                  value={configuration.SubtitleCacheTtlSeconds}
                  onChange={event => setConfiguration(current => ({ ...current, SubtitleCacheTtlSeconds: Number.parseInt(event.target.value, 10) || 604800 }))}
                />
              </FieldShell>
              <FieldShell label="同时转换数">
                <input
                  className={textInputClassName()}
                  max={4}
                  min={1}
                  type="number"
                  value={configuration.VideoConvertConcurrency}
                  onChange={event => setConfiguration(current => ({ ...current, VideoConvertConcurrency: Number.parseInt(event.target.value, 10) || 1 }))}
                />
              </FieldShell>
              <FieldShell label="FFmpeg 路径（留空自动探测）">
                <input
                  className={textInputClassName()}
                  placeholder="/usr/lib/jellyfin-ffmpeg/ffmpeg"
                  value={configuration.FfmpegExecutablePath}
                  onChange={event => setConfiguration(current => ({ ...current, FfmpegExecutablePath: event.target.value }))}
                />
              </FieldShell>
              <FieldShell label="QSV 设备路径">
                <input
                  className={textInputClassName()}
                  placeholder="/dev/dri/renderD128"
                  value={configuration.QsvRenderDevicePath}
                  onChange={event => setConfiguration(current => ({ ...current, QsvRenderDevicePath: event.target.value }))}
                />
              </FieldShell>
            </div>
          </Panel>
        </div>

        <div className="mt-8 flex justify-stretch gap-3 sm:justify-end">
          <Button className="w-full sm:w-auto" disabled={busy} type="button" variant="primary" onClick={() => void saveConfigurationValues()}>
            保存设置
          </Button>
        </div>
      </div>
    </div>
  );
}
