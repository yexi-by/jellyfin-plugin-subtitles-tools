import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { OverlayApp } from './App';
import type { OverlayStoreState } from './store';

function buildState(): OverlayStoreState {
  return {
    activePartId: 'part-1',
    busy: false,
    isFabVisible: true,
    isOverlayOpen: true,
    itemData: {
      CurrentPartId: 'part-1',
      IsMultipart: true,
      ItemType: 'Movie',
      Name: '示例影片',
      Parts: [
        {
          Container: 'mkv',
          EmbeddedSubtitles: [
            {
              Format: 'srt',
              IsPluginManaged: true,
              Language: 'zh-CN',
              StreamIndex: 3,
              SubtitleStreamIndex: 0,
              Title: '简体中文字幕'
            }
          ],
          ExternalSubtitles: [
            {
              FileName: 'movie-part-1.zho.srt',
              FilePath: '/media/movie-part-1.zho.srt',
              Format: 'srt',
              Language: 'zho'
            }
          ],
          FileName: 'movie-part-1.mkv',
          Id: 'part-1',
          Index: 1,
          IsManaged: true,
          Label: 'Part 1',
          MediaPath: '/media/movie-part-1.mkv',
          NeedsCompatibilityRepair: false,
          Pipeline: 'metadata-ready',
          RiskVerdict: '低风险'
        },
        {
          Container: 'mp4',
          EmbeddedSubtitles: [],
          ExternalSubtitles: [],
          FileName: 'movie-part-2.mp4',
          Id: 'part-2',
          Index: 2,
          IsManaged: false,
          Label: 'Part 2',
          MediaPath: '/media/movie-part-2.mp4',
          NeedsCompatibilityRepair: true,
          Pipeline: '',
          RiskVerdict: '高风险'
        }
      ]
    },
    itemId: 'item-1',
    lastBatchItems: [
      {
        Label: 'Part 1',
        Message: '已保存外挂字幕。',
        PartId: 'part-1',
        Status: 'sidecar',
        WriteMode: 'sidecar'
      }
    ],
    lastLocation: '/web/index.html#!/details?id=item-1',
    searchResults: new Map([
      [
        'part-1',
        [
          {
            DisplayName: '候选字幕一',
            Id: 'candidate-1',
            Language: 'zh-CN',
            Score: 98,
            SidecarFileName: 'movie-part-1.zho.srt'
          }
        ]
      ]
    ]),
    statusTitle: '状态已更新',
    statusMessage: '当前文件信息已刷新。',
    statusTone: 'success',
    subtitleWriteMode: 'sidecar'
  };
}

describe('OverlayApp', () => {
  it('渲染当前媒体概览、整组操作和关键交互入口', () => {
    const actions = {
      closeOverlay: vi.fn(),
      convertCurrentPart: vi.fn(async () => undefined),
      convertGroup: vi.fn(async () => undefined),
      deleteEmbeddedSubtitle: vi.fn(async () => undefined),
      downloadBest: vi.fn(async () => undefined),
      downloadCandidate: vi.fn(async () => undefined),
      refresh: vi.fn(async () => undefined),
      searchCurrentPart: vi.fn(async () => undefined),
      selectPart: vi.fn(),
      setSubtitleWriteMode: vi.fn()
    };

    render(<OverlayApp actions={actions} state={buildState()} />);

    expect(screen.getByText('示例影片')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '找到的字幕' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '已有字幕' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '整组操作' })).toBeInTheDocument();
    expect(screen.getByText('外挂字幕')).toBeInTheDocument();
    expect(screen.getByText('视频内字幕')).toBeInTheDocument();
    expect(screen.getByText('由本工具添加')).toBeInTheDocument();
    expect(screen.getByText('movie-part-1.zho.srt')).toBeInTheDocument();
    expect(screen.getByText('候选字幕一')).toBeInTheDocument();

    const candidateRow = screen.getByText('候选字幕一').closest('div.rounded-lg');
    expect(candidateRow).not.toBeNull();
    fireEvent.click(within(candidateRow as HTMLElement).getByRole('button', { name: '另存字幕' }));
    expect(actions.downloadCandidate).toHaveBeenCalledWith('candidate-1');

    fireEvent.click(screen.getAllByRole('button', { name: '写入视频' })[0]);
    expect(actions.setSubtitleWriteMode).toHaveBeenCalledWith('embedded');

    fireEvent.click(screen.getByRole('button', { name: '整组优化' }));
    expect(actions.convertGroup).toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: '整组自动选字幕' }));
    expect(actions.downloadBest).toHaveBeenCalled();

    fireEvent.click(screen.getAllByRole('button', { name: /Part 2/ })[0]);
    expect(actions.selectPart).toHaveBeenCalledWith('part-2');

    fireEvent.click(screen.getByRole('button', { name: '关闭' }));
    expect(actions.closeOverlay).toHaveBeenCalled();
  });
});
