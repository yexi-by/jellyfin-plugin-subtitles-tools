import { startTransition, useEffect, useEffectEvent, useState, type JSX } from 'react';
import {
  Badge,
  Button,
  ConnectionMetrics,
  FieldShell,
  Panel,
  SectionHeading,
  StatusBanner
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
import type { ConnectionHealthPayload, ConnectionStatusViewModel, PluginConfiguration } from '../shared/types';

const defaultConfiguration: PluginConfiguration = {
  EnableAutoVideoConvertToMkv: true,
  FfmpegExecutablePath: '',
  QsvRenderDevicePath: '/dev/dri/renderD128',
  RequestTimeoutSeconds: 10,
  ServiceBaseUrl: 'http://127.0.0.1:8055',
  VideoConvertConcurrency: 1
};

function textInputClassName(): string {
  return 'w-full rounded-shell-sm border border-white/10 bg-shell-bg-soft px-4 py-3 text-sm text-shell-text outline-none transition placeholder:text-shell-text-faint focus:border-shell-accent/45 focus:ring-2 focus:ring-shell-accent/15';
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
      showError('加载插件配置失败。');
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
      const message = extractErrorMessage(error);
      setConnectionStatus(buildErrorConnectionStatus(message));
      showError(message);
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
    <div className="min-h-screen bg-[radial-gradient(circle_at_top_right,rgba(208,108,77,0.18),transparent_34%),radial-gradient(circle_at_top_left,rgba(80,119,154,0.16),transparent_32%),linear-gradient(180deg,rgba(12,16,23,0.98),rgba(15,20,27,1))] text-shell-text">
      <div className="mx-auto grid max-w-[1320px] gap-6 px-4 py-6 md:px-8 xl:px-10">
        <section className="relative overflow-hidden rounded-shell-xl border border-shell-border bg-[linear-gradient(145deg,rgba(24,31,42,0.96),rgba(18,23,32,0.98))] p-6 shadow-shell-strong md:p-10">
          <div className="absolute right-[-5rem] top-[-6rem] h-64 w-64 rounded-full bg-[radial-gradient(circle,rgba(208,108,77,0.28),transparent_70%)]" />
          <div className="absolute bottom-[-8rem] left-[-6rem] h-72 w-72 rounded-full bg-[radial-gradient(circle,rgba(80,119,154,0.22),transparent_70%)]" />
          <div className="relative grid gap-6 lg:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.95fr)]">
            <div className="grid gap-4">
              <div className="inline-flex items-center gap-2 text-xs font-bold uppercase tracking-[0.18em] text-shell-text/70">Subtitles Tools 控制台</div>
              <h1 className="max-w-[48rem] font-serif text-[clamp(2rem,4vw,3.35rem)] leading-[1.02]">把字幕搜索、纳管修复与内封流程收进一套统一前端工作台。</h1>
              <p className="max-w-[44rem] text-sm leading-8 text-shell-text-soft">
                当前配置页聚焦 Python 服务连接、自动纳管策略、转码执行环境与运行边界说明。保存前先完成连通性检测，可以更快定位 FFmpeg、QSV 和字幕源问题。
              </p>
              <div className="flex flex-wrap gap-2.5">
                <Badge tone="accent">深色影院控制台</Badge>
                <Badge>Intel QSV 优先</Badge>
                <Badge>本地媒体专用</Badge>
                <Badge>适配 Jellyfin 10.11</Badge>
              </div>
            </div>
            <div className="grid gap-4">
              <Panel className="gap-4 bg-white/4">
                <SectionHeading title="处理流程" description="配置完成后，字幕工作流会始终遵循同一条操作顺序。" />
                <ol className="grid gap-3">
                  {[
                    ['01', '先纳管与兼容修复', '先确认容器、编码与 MKV 元数据是否已经处于插件可识别状态。'],
                    ['02', '再搜索与匹配', '依赖 Python 服务按 CID / GCID / 文件名组合查找候选字幕。'],
                    ['03', '统一转为 UTF-8 SRT', '候选字幕会先归一化为兼容性更好的 UTF-8 SRT，再进入内封阶段。'],
                    ['04', '写入 MKV 并清理临时文件', '成功写入后自动清理临时 SRT，减少额外文件残留。']
                  ].map(([index, title, description]) => (
                    <li key={index} className="grid grid-cols-[auto_1fr] gap-3">
                      <span className="inline-flex h-8 w-8 items-center justify-center rounded-full bg-[linear-gradient(135deg,rgba(208,108,77,0.24),rgba(80,119,154,0.22))] text-xs font-bold text-shell-text">{index}</span>
                      <div className="grid gap-1">
                        <strong className="text-sm font-semibold text-shell-text">{title}</strong>
                        <span className="text-[0.84rem] leading-6 text-shell-text-soft">{description}</span>
                      </div>
                    </li>
                  ))}
                </ol>
              </Panel>
            </div>
          </div>
        </section>

        <div className="grid gap-5 xl:grid-cols-12">
          <Panel className="xl:col-span-7">
            <SectionHeading title="服务连接与连通性检测" description="这里定义 Python 服务地址和请求超时。建议先完成环境检测，再保存当前参数。" />
            <div className="grid gap-4 md:grid-cols-2">
              <FieldShell label="Python 服务地址" description={<>示例：<code className="rounded-full bg-shell-cool/20 px-2 py-0.5 text-xs text-shell-text">http://127.0.0.1:8055</code>。插件会用这个地址访问字幕搜索服务。</>}>
                <input className={textInputClassName()} value={configuration.ServiceBaseUrl} onChange={event => setConfiguration(current => ({ ...current, ServiceBaseUrl: event.target.value }))} />
              </FieldShell>
              <FieldShell label="请求超时（秒）" description="建议保持 10 秒。跨机器或低性能 NAS 环境可以适当调高。">
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
            <StatusBanner label={connectionStatus.label} message={connectionStatus.message} title={connectionStatus.title} tone={connectionStatus.tone} />
            <ConnectionMetrics metrics={connectionStatus.details} />
          </Panel>

          <Panel className="xl:col-span-5">
            <SectionHeading title="处理策略" description="这些选项决定插件是偏向自动接管新入库媒体，还是只在你手动触发时执行兼容修复。" />
            <label className="grid gap-3 rounded-shell-md border border-white/8 bg-white/4 p-4">
              <span className="flex items-start gap-3">
                <input
                  checked={configuration.EnableAutoVideoConvertToMkv}
                  className="mt-1 h-4 w-4 accent-[var(--color-shell-accent)]"
                  type="checkbox"
                  onChange={event => setConfiguration(current => ({ ...current, EnableAutoVideoConvertToMkv: event.target.checked }))}
                />
                <span className="text-sm font-semibold leading-6 text-shell-text">新视频入库后自动纳管并修复安卓硬解兼容</span>
              </span>
              <span className="text-[0.82rem] leading-6 text-shell-text-soft">默认开启。新文件若已是 MKV，则会补算 CID / GCID 并写入 MKV 元数据；若命中高风险编码或容器，则优先走 Intel QSV 修复为 H.264 + AAC + MKV。</span>
            </label>
            <FieldShell label="视频转换并发数" description="默认 1。视频转换是重 I/O 操作，网络盘和机械盘不建议设置过高。">
              <input
                className={textInputClassName()}
                max={4}
                min={1}
                type="number"
                value={configuration.VideoConvertConcurrency}
                onChange={event => setConfiguration(current => ({ ...current, VideoConvertConcurrency: Number.parseInt(event.target.value, 10) || 1 }))}
              />
            </FieldShell>
            <ul className="grid gap-2">
              {[
                '历史影片建议通过计划任务统一补齐 CID/GCID 与 MKV 转换，再到详情页做单片精修。',
                '插件当前不管理外挂字幕，所有成功写入的字幕都以内封轨形式存在于 MKV 中。',
                '自动纳管更适合长期稳定提供 QSV 的运行环境。'
              ].map(item => (
                <li key={item} className="grid grid-cols-[auto_1fr] gap-3 text-sm leading-7 text-shell-text-soft">
                  <span className="text-shell-accent">•</span>
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </Panel>

          <Panel className="xl:col-span-7">
            <SectionHeading title="执行环境" description="插件优先探测 Jellyfin 自带 FFmpeg，再回退到系统 PATH。QSV 设备路径用于 Linux 环境的存在性校验与转码执行。" />
            <div className="grid gap-4 md:grid-cols-2">
              <FieldShell label="FFmpeg 可执行文件路径（可选）" description="留空时优先自动探测 Jellyfin 自带 FFmpeg，再回退到系统 PATH。">
                <input
                  className={textInputClassName()}
                  placeholder="/usr/lib/jellyfin-ffmpeg/ffmpeg"
                  value={configuration.FfmpegExecutablePath}
                  onChange={event => setConfiguration(current => ({ ...current, FfmpegExecutablePath: event.target.value }))}
                />
              </FieldShell>
              <FieldShell label="Intel QSV 渲染设备路径" description={<>默认使用 <code className="rounded-full bg-shell-cool/20 px-2 py-0.5 text-xs text-shell-text">/dev/dri/renderD128</code>。当前只支持 Intel QSV，不提供 VAAPI 或 CPU 兜底。</>}>
                <input
                  className={textInputClassName()}
                  placeholder="/dev/dri/renderD128"
                  value={configuration.QsvRenderDevicePath}
                  onChange={event => setConfiguration(current => ({ ...current, QsvRenderDevicePath: event.target.value }))}
                />
              </FieldShell>
            </div>
          </Panel>

          <Panel className="xl:col-span-5">
            <SectionHeading title="使用提示" description="这套插件围绕“媒体自治”和“安卓端硬解兼容性”组织字幕工作流。明确边界，能减少误判和重复处理。" />
            <ul className="grid gap-2">
              {[
                '插件先看 MKV 元数据，再决定是否已纳管，不依赖外挂文件名推断。',
                '搜索和一键内封前，会优先确认视频已经纳管，并对高风险片段先做兼容修复。',
                '如果只是批量补齐旧媒体，优先走计划任务统一处理，再到详情页做单片微调。'
              ].map(item => (
                <li key={item} className="grid grid-cols-[auto_1fr] gap-3 text-sm leading-7 text-shell-text-soft">
                  <span className="text-shell-accent">•</span>
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </Panel>
        </div>

        <div className="sticky bottom-3 flex flex-wrap items-center justify-between gap-4 rounded-shell-lg border border-shell-border-strong bg-[rgba(15,20,27,0.9)] p-4 shadow-shell-soft backdrop-blur-md">
          <div className="grid gap-1">
            <strong className="text-sm font-semibold text-shell-text">建议顺序：先测试连接，再保存配置</strong>
            <span className="text-sm leading-6 text-shell-text-soft">保存后会立即成为插件当前配置，后续任务会按这里的参数执行。</span>
          </div>
          <div className="flex w-full flex-wrap gap-3 sm:w-auto">
            <Button className="flex-1 sm:min-w-40 sm:flex-none" disabled={busy} type="button" variant="secondary" onClick={() => void testConnection()}>
              测试连接
            </Button>
            <Button className="flex-1 sm:min-w-40 sm:flex-none" disabled={busy} type="button" variant="primary" onClick={() => void saveConfigurationValues()}>
              保存
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
