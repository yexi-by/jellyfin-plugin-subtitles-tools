import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ConfigPageApp } from './App';
import { PLUGIN_UNIQUE_ID } from '../shared/constants';
import * as runtime from '../shared/runtime';

vi.mock('../shared/runtime', async () => {
  const actual = await vi.importActual<typeof runtime>('../shared/runtime');
  return {
    ...actual,
    hideLoading: vi.fn(),
    readPluginConfiguration: vi.fn(),
    requestJson: vi.fn(),
    savePluginConfiguration: vi.fn(),
    showConfigurationSaved: vi.fn(),
    showError: vi.fn(),
    showLoading: vi.fn()
  };
});

const readPluginConfiguration = vi.mocked(runtime.readPluginConfiguration);
const requestJson = vi.mocked(runtime.requestJson);
const savePluginConfiguration = vi.mocked(runtime.savePluginConfiguration);
const showConfigurationSaved = vi.mocked(runtime.showConfigurationSaved);

describe('ConfigPageApp', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    readPluginConfiguration.mockResolvedValue({
      EnableAutoVideoConvertToMkv: false,
      FfmpegExecutablePath: '/usr/bin/ffmpeg',
      QsvRenderDevicePath: '/dev/dri/renderD129',
      RequestTimeoutSeconds: 20,
      ServiceBaseUrl: 'http://service.test:8055',
      VideoConvertConcurrency: 2
    });
  });

  it('加载配置后展示表单值，并在连接检测成功后渲染结构化状态', async () => {
    requestJson.mockResolvedValue({
      Ffmpeg: {
        ffmpegPath: '/usr/bin/ffmpeg',
        ffprobePath: '/usr/bin/ffprobe'
      },
      Health: {
        ProviderName: 'ZiMuKu',
        Version: '1.2.3'
      },
      Video: {
        qsvRenderDevicePath: '/dev/dri/renderD129',
        supportsH264Qsv: true
      }
    });

    render(<ConfigPageApp />);

    expect(await screen.findByDisplayValue('http://service.test:8055')).toBeInTheDocument();
    expect(screen.getByDisplayValue('20')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: '测试连接' }));

    expect(await screen.findByText('服务链路已经准备就绪')).toBeInTheDocument();
    expect(screen.getByText('服务版本')).toBeInTheDocument();
    expect(screen.getByText('ZiMuKu')).toBeInTheDocument();
    expect(requestJson).toHaveBeenCalledWith(
      'Jellyfin.Plugin.SubtitlesTools/TestConnection',
      'POST',
      expect.objectContaining({
        RequestTimeoutSeconds: 20,
        ServiceBaseUrl: 'http://service.test:8055'
      })
    );
  });

  it('保存时会读取当前配置并提交合并后的参数', async () => {
    savePluginConfiguration.mockResolvedValue({ ok: true });

    render(<ConfigPageApp />);

    const serviceInput = await screen.findByDisplayValue('http://service.test:8055');
    fireEvent.change(serviceInput, {
      target: { value: 'http://localhost:9000' }
    });
    fireEvent.click(screen.getByRole('button', { name: '保存' }));

    await waitFor(() => {
      expect(savePluginConfiguration).toHaveBeenCalledWith(
        PLUGIN_UNIQUE_ID,
        expect.objectContaining({
          ServiceBaseUrl: 'http://localhost:9000'
        })
      );
    });
    expect(showConfigurationSaved).toHaveBeenCalled();
  });
});
