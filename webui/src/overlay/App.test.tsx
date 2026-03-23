import { fireEvent, render, screen } from '@testing-library/react';
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
        Message: '已完成处理',
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
    statusMessage: '最近一次操作成功。',
    statusTone: 'success',
    subtitleWriteMode: 'sidecar'
  };
}

describe('OverlayApp', () => {
  it('渲染当前媒体概览、外挂信息并分发关键操作事件', () => {
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
    expect(screen.getByText('外挂字幕总数')).toBeInTheDocument();
    expect(screen.getAllByText('movie-part-1.zho.srt')).toHaveLength(2);
    expect(screen.getByText('候选字幕一')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: '下载并写为外挂字幕' }));
    expect(actions.downloadCandidate).toHaveBeenCalledWith('candidate-1');

    fireEvent.click(screen.getAllByRole('button', { name: /内封字幕/ })[0]);
    expect(actions.setSubtitleWriteMode).toHaveBeenCalledWith('embedded');

    fireEvent.click(screen.getByRole('button', { name: /Part 2/ }));
    expect(actions.selectPart).toHaveBeenCalledWith('part-2');

    fireEvent.click(screen.getByRole('button', { name: '关闭字幕控制台' }));
    expect(actions.closeOverlay).toHaveBeenCalled();
});
});
