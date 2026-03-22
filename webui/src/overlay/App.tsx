import type { JSX } from 'react';
import { Badge, Button, EmptyState, MetricCard, SectionHeading, StatusBanner, cx } from '../design-system/components';
import { getFileNameFromPath } from '../shared/dom';
import {
  getBatchStatusText,
  getBatchTone,
  getCompatibilityStatusText,
  getCompatibilityTone,
  getItemMetrics,
  getManagedStatusText,
  getManagedStatusTone,
  getPartSelectionSummary,
  getStatusLabel,
  getStatusTitle
} from '../shared/formatters';
import type { MediaPart, OperationResultItem, SubtitleCandidate } from '../shared/types';
import type { OverlayStoreState } from './store';

interface OverlayActions {
  closeOverlay: () => void;
  convertCurrentPart: () => Promise<void>;
  convertGroup: () => Promise<void>;
  deleteEmbeddedSubtitle: (streamIndex: number) => Promise<void>;
  downloadBest: () => Promise<void>;
  downloadCandidate: (candidateId: string) => Promise<void>;
  refresh: () => Promise<void>;
  searchCurrentPart: () => Promise<void>;
  selectPart: (partId: string) => void;
}

function CurrentPartCard({ part }: { part: MediaPart | null }): JSX.Element {
  if (!part) {
    return <EmptyState title="当前没有可展示的分段。" description="先在左侧选择一个可管理分段，再查看当前概览。" />;
  }

  const pipeline = part.Pipeline || '尚未写入流水线';
  const partNumber = part.PartNumber !== null && part.PartNumber !== undefined ? `第 ${part.PartNumber} 段` : '未编号';

  return (
    <article className="st-grid st-gap-4 st-rounded-shell-lg st-border st-border-white/8 st-bg-[linear-gradient(145deg,rgba(243,241,236,0.04),rgba(80,119,154,0.08))] st-p-5">
      <div className="st-flex st-flex-wrap st-items-start st-justify-between st-gap-3">
        <div className="st-grid st-gap-2">
          <span className="st-text-[0.72rem] st-font-bold st-uppercase st-tracking-[0.14em] st-text-shell-text/62">{part.Label}</span>
          <h4 className="st-break-all st-text-lg st-font-semibold st-leading-7 st-text-shell-text">{part.FileName || getFileNameFromPath(part.MediaPath)}</h4>
          <p className="st-text-sm st-leading-7 st-text-shell-text-soft">插件会优先完成纳管与兼容性判断，再决定是否继续搜索字幕与内封。</p>
        </div>
        <div className="st-flex st-flex-wrap st-gap-2">
          <Badge>{(part.Container || 'unknown').toUpperCase()}</Badge>
          <Badge tone={getManagedStatusTone(part)}>{getManagedStatusText(part)}</Badge>
          <Badge tone={getCompatibilityTone(part)}>{getCompatibilityStatusText(part)}</Badge>
          <Badge tone={part.Pipeline ? 'accent' : 'neutral'}>{pipeline}</Badge>
        </div>
      </div>
      <div className="st-grid st-gap-3 md:st-grid-cols-2">
        {[
          ['分段类型', part.PartKind || '未标记'],
          ['分段编号', partNumber],
          ['媒体路径', part.MediaPath || '未返回路径'],
          ['当前流水线', pipeline]
        ].map(([label, value]) => (
          <div key={label} className="st-grid st-gap-1.5 st-rounded-shell-sm st-border st-border-white/8 st-bg-white/4 st-p-3.5">
            <span className="st-text-xs st-text-shell-text/62">{label}</span>
            <strong className="st-break-all st-text-sm st-leading-6 st-text-shell-text">{value}</strong>
          </div>
        ))}
      </div>
      <p className="st-text-sm st-leading-7 st-text-shell-text-soft">当前插件只根据视频自身的 MKV 元数据判断是否已纳管。若分段仍命中高风险硬解规则，建议优先执行 MKV 转换修复。</p>
    </article>
  );
}

function EmbeddedSubtitleList({
  onDelete,
  part
}: {
  onDelete: (streamIndex: number) => Promise<void>;
  part: MediaPart | null;
}): JSX.Element {
  const tracks = part?.EmbeddedSubtitles ?? [];
  if (tracks.length === 0) {
    return <EmptyState title="当前还没有内封字幕流。" description="搜索并下载候选字幕后，这里会展示当前分段的字幕轨道列表。" />;
  }

  return (
    <div className="st-grid st-gap-3">
      {tracks.map(track => (
        <article key={track.StreamIndex} className="st-grid st-gap-4 st-rounded-shell-lg st-border st-border-white/8 st-bg-white/4 st-p-5">
          <div className="st-flex st-flex-wrap st-items-start st-justify-between st-gap-3">
            <div className="st-grid st-gap-2">
              <span className="st-text-[0.72rem] st-font-bold st-uppercase st-tracking-[0.14em] st-text-shell-text/62">字幕流 #{track.StreamIndex}</span>
              <h4 className="st-text-lg st-font-semibold st-text-shell-text">{track.Title || `字幕流 #${track.StreamIndex}`}</h4>
              <p className="st-text-sm st-leading-7 st-text-shell-text-soft">这条字幕轨已经存在于当前分段的 MKV 容器中。</p>
            </div>
            <div className="st-flex st-flex-wrap st-gap-2">
              <Badge tone={track.IsPluginManaged ? 'success' : 'neutral'}>{track.IsPluginManaged ? '插件写入' : '现有轨道'}</Badge>
              <Badge>{track.Language || 'und'}</Badge>
              <Badge tone="accent">{(track.Format || 'srt').toUpperCase()}</Badge>
            </div>
          </div>
          <div className="st-grid st-gap-3 md:st-grid-cols-2">
            <div className="st-grid st-gap-1.5 st-rounded-shell-sm st-border st-border-white/8 st-bg-white/4 st-p-3.5">
              <span className="st-text-xs st-text-shell-text/62">绝对流索引</span>
              <strong className="st-text-sm st-text-shell-text">{track.StreamIndex}</strong>
            </div>
            <div className="st-grid st-gap-1.5 st-rounded-shell-sm st-border st-border-white/8 st-bg-white/4 st-p-3.5">
              <span className="st-text-xs st-text-shell-text/62">字幕流序号</span>
              <strong className="st-text-sm st-text-shell-text">{track.SubtitleStreamIndex ?? '未返回'}</strong>
            </div>
          </div>
          {track.IsPluginManaged ? (
            <div className="st-flex st-flex-wrap st-gap-3">
              <Button type="button" variant="danger" onClick={() => void onDelete(track.StreamIndex)}>
                删除这条内封字幕
              </Button>
            </div>
          ) : null}
        </article>
      ))}
    </div>
  );
}

function CandidateList({
  candidates,
  onDownload
}: {
  candidates: SubtitleCandidate[];
  onDownload: (candidateId: string) => Promise<void>;
}): JSX.Element {
  if (candidates.length === 0) {
    return <EmptyState title="当前还没有搜索结果。" description="点击“搜索当前分段”后，这里会展示候选字幕与匹配分数。" />;
  }

  return (
    <div className="st-grid st-gap-3">
      {candidates.map(candidate => {
        const languages = candidate.Languages?.length ? candidate.Languages.join(' / ') : candidate.Language || 'und';
        const fingerprintScore = Number.isFinite(candidate.FingerprintScore) ? candidate.FingerprintScore!.toFixed(2) : '-';

        return (
          <article key={candidate.Id} className="st-grid st-gap-4 st-rounded-shell-lg st-border st-border-white/8 st-bg-white/4 st-p-5">
            <div className="st-flex st-flex-wrap st-items-start st-justify-between st-gap-4">
              <div className="st-grid st-gap-2">
                <span className="st-text-[0.72rem] st-font-bold st-uppercase st-tracking-[0.14em] st-text-shell-text/62">候选字幕</span>
                <h4 className="st-text-lg st-font-semibold st-leading-7 st-text-shell-text">{candidate.DisplayName || candidate.Name || '未命名候选字幕'}</h4>
                <p className="st-text-sm st-leading-7 st-text-shell-text-soft">候选项会先转为 UTF-8 SRT，再内封到当前分段对应的 MKV 文件。</p>
              </div>
              <div className="st-grid st-place-items-center st-rounded-shell-lg st-bg-[linear-gradient(145deg,rgba(208,108,77,0.18),rgba(80,119,154,0.18))] st-px-4 st-py-3 st-text-center st-text-shell-text">
                <strong className="st-text-2xl st-leading-none">{candidate.Score ?? '-'}</strong>
                <span className="st-mt-1 st-text-[0.68rem] st-font-bold st-uppercase st-tracking-[0.12em] st-text-shell-text/70">匹配分</span>
              </div>
            </div>
            <div className="st-grid st-gap-3 md:st-grid-cols-2">
              {[
                ['语言', languages],
                ['格式', (candidate.Format || candidate.Ext || 'srt').toUpperCase()],
                ['指纹分', fingerprintScore],
                ['临时文件', candidate.TemporarySrtFileName || '未返回']
              ].map(([label, value]) => (
                <div key={label} className="st-grid st-gap-1.5 st-rounded-shell-sm st-border st-border-white/8 st-bg-white/4 st-p-3.5">
                  <span className="st-text-xs st-text-shell-text/62">{label}</span>
                  <strong className="st-break-all st-text-sm st-leading-6 st-text-shell-text">{value}</strong>
                </div>
              ))}
            </div>
            <div className="st-flex st-flex-wrap st-gap-3">
              <Button type="button" variant="primary" onClick={() => void onDownload(candidate.Id)}>
                下载并内封到当前分段
              </Button>
            </div>
          </article>
        );
      })}
    </div>
  );
}

function BatchSummary({ items }: { items: OperationResultItem[] }): JSX.Element | null {
  if (items.length === 0) {
    return null;
  }

  return (
    <section className="st-grid st-gap-4">
      <SectionHeading title="最近一次整组任务" description="这里保留最近一次整组转换或一键最佳匹配任务的结果，方便快速核对每个分段的处理状态。" />
      <div className="st-grid st-gap-3">
        {items.map(item => (
          <article
            key={`${item.PartId ?? 'unknown'}-${item.Status ?? 'unknown'}`}
            className={cx(
              'st-grid st-gap-4 st-rounded-shell-lg st-border st-p-5',
              getBatchTone(item.Status) === 'success' && 'st-border-shell-success/35 st-bg-[linear-gradient(145deg,rgba(109,177,140,0.18),rgba(243,241,236,0.03))]',
              getBatchTone(item.Status) === 'warning' && 'st-border-shell-warning/35 st-bg-[linear-gradient(145deg,rgba(216,170,100,0.18),rgba(243,241,236,0.03))]',
              getBatchTone(item.Status) === 'danger' && 'st-border-shell-danger/35 st-bg-[linear-gradient(145deg,rgba(212,122,114,0.18),rgba(243,241,236,0.03))]',
              getBatchTone(item.Status) === 'neutral' && 'st-border-white/8 st-bg-white/4'
            )}
          >
            <div className="st-flex st-flex-wrap st-items-start st-justify-between st-gap-3">
              <div className="st-grid st-gap-2">
                <strong className="st-text-base st-font-semibold st-text-shell-text">{item.Label || '未命名分段'}</strong>
                <span className="st-text-sm st-leading-7 st-text-shell-text-soft">{item.Message || '未返回处理信息。'}</span>
              </div>
              <Badge tone={getBatchTone(item.Status)}>{getBatchStatusText(item.Status)}</Badge>
            </div>
            <div className="st-flex st-flex-wrap st-gap-2">
              {item.Container ? <Badge>{item.Container.toUpperCase()}</Badge> : null}
              {item.Pipeline ? <Badge tone="accent">{item.Pipeline}</Badge> : null}
              {item.RiskVerdict ? <Badge tone={item.NeedsCompatibilityRepair ? 'danger' : 'warning'}>{item.NeedsCompatibilityRepair ? `${item.RiskVerdict}（需修复）` : item.RiskVerdict}</Badge> : null}
            </div>
            {item.MediaPath ? <div className="st-break-all st-text-xs st-leading-6 st-text-shell-text-faint">{item.MediaPath}</div> : null}
          </article>
        ))}
      </div>
    </section>
  );
}

function PartNavigation({
  activePartId,
  onSelect,
  parts
}: {
  activePartId: string | null;
  onSelect: (partId: string) => void;
  parts: MediaPart[];
}): JSX.Element {
  if (parts.length === 0) {
    return <EmptyState title="未识别到可管理分段。" description="当前媒体详情页没有返回可供插件管理的本地分段信息。" />;
  }

  return (
    <div className="st-grid st-gap-3">
      {parts.map(part => (
        <button
          key={part.Id}
          className={cx(
            'st-grid st-gap-3 st-rounded-shell-md st-border st-border-white/8 st-bg-white/4 st-p-4 st-text-left st-transition hover:st--translate-y-0.5',
            activePartId === part.Id && 'st-border-shell-accent/35 st-bg-[linear-gradient(145deg,rgba(208,108,77,0.16),rgba(80,119,154,0.14))]'
          )}
          type="button"
          onClick={() => onSelect(part.Id)}
        >
          <div className="st-flex st-flex-wrap st-items-start st-justify-between st-gap-3">
            <div className="st-grid st-gap-1">
              <strong className="st-text-sm st-font-semibold st-text-shell-text">{part.Label}</strong>
              <span className="st-break-all st-text-xs st-leading-6 st-text-shell-text-faint">{part.FileName || getFileNameFromPath(part.MediaPath)}</span>
            </div>
            <span className="st-inline-flex st-h-8 st-min-w-8 st-items-center st-justify-center st-rounded-full st-bg-white/8 st-px-2 st-text-xs st-font-bold st-text-shell-text/82">#{part.Index}</span>
          </div>
          <div className="st-flex st-flex-wrap st-gap-2">
            <Badge>{(part.Container || 'unknown').toUpperCase()}</Badge>
            <Badge tone={getManagedStatusTone(part)}>{getManagedStatusText(part)}</Badge>
            <Badge tone={getCompatibilityTone(part)}>{getCompatibilityStatusText(part)}</Badge>
          </div>
        </button>
      ))}
    </div>
  );
}

export function OverlayApp({
  actions,
  state
}: {
  actions: OverlayActions;
  state: OverlayStoreState;
}): JSX.Element {
  const activePart = state.itemData?.Parts.find(part => part.Id === state.activePartId) ?? null;
  const activeCandidates = activePart ? (state.searchResults.get(activePart.Id) ?? []) : [];
  const headerSummary = getPartSelectionSummary(activePart, activeCandidates.length);

  return (
    <div
      className={cx(
        'st-fixed st-inset-0 st-z-[100000] st-hidden st-bg-[radial-gradient(circle_at_top_right,rgba(208,108,77,0.18),transparent_30%),radial-gradient(circle_at_top_left,rgba(80,119,154,0.18),transparent_28%),rgba(7,10,15,0.82)] st-p-3 st-backdrop-blur-md lg:st-p-6',
        state.isOverlayOpen && 'st-flex st-items-center st-justify-center'
      )}
      onClick={event => {
        if (event.target === event.currentTarget) {
          actions.closeOverlay();
        }
      }}
    >
      <div className="st-grid st-h-full st-w-full st-max-w-[1280px] st-grid-rows-[auto_auto_auto_minmax(0,1fr)] st-overflow-hidden st-rounded-none st-border st-border-shell-border st-bg-[linear-gradient(180deg,rgba(18,23,32,0.98),rgba(15,20,27,0.98))] st-shadow-shell-strong sm:st-max-h-[calc(100vh-24px)] sm:st-rounded-shell-xl lg:st-max-h-[calc(100vh-48px)]">
        <header className="st-relative st-grid st-gap-5 st-border-b st-border-shell-border st-p-4 md:st-grid-cols-[minmax(0,1.2fr)_minmax(16rem,0.8fr)] md:st-px-8 md:st-py-7">
          <div className="st-absolute st-right-[-7rem] st-top-[-8rem] st-h-72 st-w-72 st-rounded-full st-bg-[radial-gradient(circle,rgba(208,108,77,0.24),transparent_72%)]" />
          <div className="st-absolute st-bottom-[-10rem] st-left-[-7rem] st-h-80 st-w-80 st-rounded-full st-bg-[radial-gradient(circle,rgba(80,119,154,0.2),transparent_74%)]" />
          <div className="st-relative st-grid st-gap-4">
            <div className="st-inline-flex st-items-center st-gap-2 st-text-[0.72rem] st-font-bold st-uppercase st-tracking-[0.18em] st-text-shell-text/68">Subtitles Tools 控制台</div>
            <h2 className="st-font-serif st-text-[clamp(2rem,2.6vw,2.9rem)] st-leading-[1.04] st-text-shell-text">{state.itemData?.Name || '未命名媒体'}</h2>
            <p className="st-max-w-[42rem] st-text-sm st-leading-8 st-text-shell-text-soft">{state.itemData?.ItemType || '媒体项目'} · {state.itemData?.IsMultipart ? '多分段媒体' : '单文件媒体'}。当前弹层用于分段纳管、字幕搜索、字幕内封与整组任务复核。</p>
            <div className="st-flex st-flex-wrap st-gap-2">
              <Badge tone="accent">{state.itemData?.IsMultipart ? '多分段媒体' : '单文件媒体'}</Badge>
              {headerSummary.map(item => <Badge key={item}>{item}</Badge>)}
            </div>
          </div>
          <div className="st-relative st-grid st-content-start st-justify-items-end st-gap-3">
            <button
              aria-label="关闭字幕控制台"
              className="st-inline-flex st-h-11 st-w-11 st-items-center st-justify-center st-rounded-full st-border st-border-white/12 st-bg-white/5 st-text-2xl st-text-shell-text st-transition hover:st-rotate-90 hover:st-bg-white/9 disabled:st-cursor-not-allowed disabled:st-opacity-60"
              disabled={state.busy}
              type="button"
              onClick={actions.closeOverlay}
            >
              ×
            </button>
            <div className="st-w-full st-max-w-[20rem] st-rounded-shell-md st-border st-border-white/8 st-bg-white/4 st-p-4 st-text-sm st-leading-7 st-text-shell-text-soft">
              这套控制台遵循“先纳管与兼容修复，再搜索和内封”的顺序。若当前分段仍被标记为高风险，建议优先执行 MKV 转换。
            </div>
          </div>
        </header>

        <section className="st-grid st-gap-3 st-border-b st-border-shell-border st-p-4 md:st-grid-cols-2 xl:st-grid-cols-4 md:st-px-8 md:st-py-5">
          {(state.itemData ? getItemMetrics(state.itemData) : []).map(metric => <MetricCard key={metric.label} metric={metric} />)}
        </section>

        <section className="st-grid st-gap-4 st-border-b st-border-shell-border st-p-4 md:st-px-8 md:st-py-5">
          <StatusBanner
            label={getStatusLabel(state.statusTone)}
            message={state.statusMessage || '选择左侧分段后，即可开始搜索、转换和内封操作。'}
            title={getStatusTitle(state.statusTone)}
            tone={state.statusTone}
          />
          <div className="st-grid st-gap-3 md:st-grid-cols-2 xl:st-grid-cols-5">
            <Button disabled={state.busy} type="button" variant="secondary" onClick={() => void actions.refresh()}>刷新分段状态</Button>
            <Button disabled={state.busy || !activePart} type="button" variant="tertiary" onClick={() => void actions.searchCurrentPart()}>搜索当前分段</Button>
            <Button disabled={state.busy || !activePart} type="button" variant="secondary" onClick={() => void actions.convertCurrentPart()}>转换当前分段为 MKV</Button>
            <Button disabled={state.busy} type="button" variant="secondary" onClick={() => void actions.convertGroup()}>一键整组转换为 MKV</Button>
            <Button disabled={state.busy} type="button" variant="primary" onClick={() => void actions.downloadBest()}>一键整组最佳匹配并内封</Button>
          </div>
        </section>

        <div className="st-grid st-min-h-0 lg:st-grid-cols-[minmax(18rem,21rem)_minmax(0,1fr)]">
          <aside className="st-grid st-min-h-0 st-grid-rows-[auto_minmax(0,1fr)] st-border-b st-border-shell-border st-bg-white/2 lg:st-border-b-0 lg:st-border-r">
            <div className="st-grid st-gap-2 st-border-b st-border-white/6 st-p-4 md:st-px-6 md:st-py-5">
              <h3 className="st-font-serif st-text-[1.55rem] st-leading-tight st-text-shell-text">分段导航</h3>
              <p className="st-text-sm st-leading-7 st-text-shell-text-soft">左侧显示分段容器、纳管状态与兼容性状态。先选择分段，再执行搜索、转换或单段内封。</p>
            </div>
            <div className="st-grid st-content-start st-gap-3 st-overflow-y-auto st-p-4 md:st-px-6 md:st-py-4">
              <PartNavigation activePartId={state.activePartId} onSelect={actions.selectPart} parts={state.itemData?.Parts ?? []} />
            </div>
          </aside>

          <main className="st-grid st-min-h-0 st-content-start st-gap-5 st-overflow-y-auto st-p-4 md:st-px-8 md:st-py-6">
            <section className="st-grid st-gap-4">
              <SectionHeading title="当前分段概览" description="这里聚焦当前分段的纳管状态、兼容性判断、路径与流水线信息，用来决定下一步操作。" />
              <CurrentPartCard part={activePart} />
            </section>

            <section className="st-grid st-gap-4">
              <SectionHeading title="当前已内封字幕流" description="区分插件写入轨道与原有轨道。只有插件写入的字幕流会显示删除按钮。" />
              <EmbeddedSubtitleList onDelete={actions.deleteEmbeddedSubtitle} part={activePart} />
            </section>

            <section className="st-grid st-gap-4">
              <SectionHeading title="搜索结果" description="候选字幕按匹配质量展示。下载时会先转换为 UTF-8 SRT，再内封到当前分段。" />
              <CandidateList candidates={activeCandidates} onDownload={actions.downloadCandidate} />
            </section>

            <BatchSummary items={state.lastBatchItems} />
          </main>
        </div>
      </div>
    </div>
  );
}

export function FloatingButton({
  onOpen,
  visible
}: {
  onOpen: () => void;
  visible: boolean;
}): JSX.Element {
  return (
    <div className={cx('st-fixed st-bottom-6 st-right-6 st-z-[99999] st-translate-y-3 st-opacity-0 st-transition lg:st-bottom-8 lg:st-right-8', visible && 'st-translate-y-0 st-opacity-100')}>
      <button
        className="st-grid st-min-w-[9.75rem] st-gap-1 st-rounded-[1.25rem] st-border st-border-white/8 st-bg-[linear-gradient(140deg,rgba(208,108,77,0.94),rgba(142,63,47,0.98))] st-px-5 st-py-4 st-text-left st-text-[rgb(255,248,244)] st-shadow-[0_18px_38px_rgba(10,13,20,0.38)] st-transition hover:st--translate-y-0.5"
        type="button"
        onClick={onOpen}
      >
        <span className="st-text-sm st-font-bold st-tracking-[0.04em]">字幕控制台</span>
        <span className="st-text-[0.68rem] st-font-semibold st-uppercase st-tracking-[0.14em] st-text-[rgba(255,248,244,0.74)]">搜索 · 转换 · 内封</span>
      </button>
    </div>
  );
}
