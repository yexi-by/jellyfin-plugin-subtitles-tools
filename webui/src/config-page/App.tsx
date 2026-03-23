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
  DefaultSubtitleWriteMode: 'embedded',
  EnableAutoVideoConvertToMkv: true,
  FfmpegExecutablePath: '',
  QsvRenderDevicePath: '/dev/dri/renderD128',
  RequestTimeoutSeconds: 10,
  ServiceBaseUrl: 'http://127.0.0.1:8055',
  VideoConvertConcurrency: 1
};

function textInputClassName(): string {
  return 'w-full rounded-md border border-white/10 bg-black/20 px-3 py-2 text-sm text-gray-200 outline-none transition focus:border-blue-500/50 focus:ring-1 focus:ring-blue-500/50';
}

function normalizeWriteMode(value: string | undefined): SubtitleWriteMode {
  return value === 'sidecar' ? 'sidecar' : 'embedded';
}

export function ConfigPageApp(): JSX.Element {
  const [configuration, setConfiguration] = useState<PluginConfiguration>(defaultConfiguration);
  const [busy, setBusy] = useState(false);
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatusViewModel>(buildIdleConnectionStatus());

  const loadConfiguration = useEffectEvent(async () => {
    showLoading();

    try {
      const response = await readPluginConfiguration(PLUGIN_UNIQUE_ID);
      startTransition(() => {
        setConfiguration({
          DefaultSubtitleWriteMode: normalizeWriteMode(response.DefaultSubtitleWriteMode),
          EnableAutoVideoConvertToMkv: response.EnableAutoVideoConvertToMkv !== false,
          FfmpegExecutablePath: response.FfmpegExecutablePath || '',
          QsvRenderDevicePath: response.QsvRenderDevicePath || '/dev/dri/renderD128',
          RequestTimeoutSeconds: response.RequestTimeoutSeconds || 10,
          ServiceBaseUrl: response.ServiceBaseUrl || 'http://127.0.0.1:8055',
          VideoConvertConcurrency: response.VideoConvertConcurrency || 1
        });
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
      const response = await requestJson<ConnectionHealthPayload>(`${API_ROOT}/TestConnection`, 'POST', configuration);
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
      const result = await savePluginConfiguration(PLUGIN_UNIQUE_ID, {
        ...current,
        ...configuration
      });
      showConfigurationSaved(result);
    } catch (error) {
      showError(extractErrorMessage(error));
    } finally {
      setBusy(false);
      hideLoading();
    }
  }

  return (
    <div className="min-h-screen bg-[#121212] text-gray-200">
      <div className="mx-auto max-w-4xl px-4 py-6 sm:px-6 sm:py-10">
        <header className="mb-8 sm:mb-10">
          <h1 className="text-2xl font-semibold text-gray-100 sm:text-3xl">字幕工具设置</h1>
          <p className="mt-2 text-sm text-gray-400">连接字幕服务，设置默认保存方式。</p>
        </header>

        <div className="flex flex-col gap-6">
          <Panel>
            <SectionHeading title="服务连接" description="填写字幕服务地址，并先做一次连接检测。" />
            <div className="grid gap-4 md:grid-cols-2">
              <FieldShell label="服务地址" description="例如：http://127.0.0.1:8055">
                <input
                  className={textInputClassName()}
                  value={configuration.ServiceBaseUrl}
                  onChange={event => setConfiguration(current => ({ ...current, ServiceBaseUrl: event.target.value }))}
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
            </div>

            <div className="mt-2 border-t border-white/5 pt-4">
              <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <span className="text-sm font-medium text-gray-200">连接检测</span>
                <Button className="w-full sm:w-auto" size="sm" disabled={busy} type="button" variant="secondary" onClick={() => void testConnection()}>
                  测试连接
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
                  <span className="text-sm font-medium text-gray-200">自动优化播放兼容性</span>
                  <span className="text-xs text-gray-500">在需要时先处理容易导致播放异常的格式问题。</span>
                </div>
              </label>
            </div>
          </Panel>

          <Panel>
            <SectionHeading title="高级设置" description="不确定时保持默认即可。" />
            <div className="grid gap-4 md:grid-cols-2">
              <FieldShell label="转换并发数">
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
