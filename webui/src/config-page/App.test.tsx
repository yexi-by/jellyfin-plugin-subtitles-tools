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
      AutoPreprocessPathBlacklist: ['/media/archive', '/media/private'],
      DefaultSubtitleWriteMode: 'embedded',
      EnableAutoVideoConvertToMkv: false,
      FfmpegExecutablePath: '/usr/bin/ffmpeg',
      QsvRenderDevicePath: '/dev/dri/renderD129',
      RequestTimeoutSeconds: 20,
      SearchCacheTtlSeconds: 3600,
      SubtitleCacheTtlSeconds: 7200,
      ThunderBaseUrl: 'https://subtitle-source.test',
      VideoConvertConcurrency: 2
    });
  });

  it('加载配置后显示表单，并在字幕源检测成功后渲染结构化状态', async () => {
    requestJson.mockResolvedValue({
      Ffmpeg: {
        ffmpegPath: '/usr/bin/ffmpeg',
        ffprobePath: '/usr/bin/ffprobe'
      },
      Health: {
        ProviderName: 'ZiMuKu',
        ProviderBaseUrl: 'https://subtitle-source.test',
        SearchCacheTtlSeconds: 3600,
        SubtitleCacheTtlSeconds: 7200,
        Version: '1.2.3'
      },
      Video: {
        qsvRenderDevicePath: '/dev/dri/renderD129',
        supportsH264Qsv: true
      }
    });

    render(<ConfigPageApp />);

    expect(await screen.findByDisplayValue('https://subtitle-source.test')).toBeInTheDocument();
    expect(screen.getByDisplayValue('20')).toBeInTheDocument();
    expect(screen.getByDisplayValue('3600')).toBeInTheDocument();
    expect(screen.getByDisplayValue('7200')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('每行一个媒体目录路径')).toHaveValue('/media/archive\n/media/private');

    fireEvent.click(screen.getByRole('button', { name: '检测字幕源' }));

    expect(await screen.findByText('字幕源正常')).toBeInTheDocument();
    expect(screen.getByText('插件版本')).toBeInTheDocument();
    expect(screen.getByText('ZiMuKu')).toBeInTheDocument();
    expect(screen.getByText('https://subtitle-source.test')).toBeInTheDocument();
    expect(screen.getByText('3600 秒')).toBeInTheDocument();
    expect(requestJson).toHaveBeenCalledWith(
      'Jellyfin.Plugin.SubtitlesTools/TestConnection',
      'POST',
      expect.objectContaining({
        AutoPreprocessPathBlacklist: ['/media/archive', '/media/private'],
        RequestTimeoutSeconds: 20,
        SearchCacheTtlSeconds: 3600,
        SubtitleCacheTtlSeconds: 7200,
        ThunderBaseUrl: 'https://subtitle-source.test'
      })
    );
  });

  it('保存时会读取当前配置并提交合并后的参数', async () => {
    savePluginConfiguration.mockResolvedValue({ ok: true });

    render(<ConfigPageApp />);

    const thunderInput = await screen.findByDisplayValue('https://subtitle-source.test');
    fireEvent.change(thunderInput, {
      target: { value: 'https://subtitle-source.local' }
    });
    fireEvent.change(screen.getByDisplayValue('3600'), {
      target: { value: '1800' }
    });
    fireEvent.change(screen.getByPlaceholderText('每行一个媒体目录路径'), {
      target: { value: '/media/archive/\n\n/media/new-skip' }
    });
    fireEvent.click(screen.getByRole('button', { name: '保存设置' }));

    await waitFor(() => {
      expect(savePluginConfiguration).toHaveBeenCalledWith(
        PLUGIN_UNIQUE_ID,
        expect.objectContaining({
          AutoPreprocessPathBlacklist: ['/media/archive', '/media/new-skip'],
          SearchCacheTtlSeconds: 1800,
          ThunderBaseUrl: 'https://subtitle-source.local'
        })
      );
    });
    expect(showConfigurationSaved).toHaveBeenCalled();
  });
});
