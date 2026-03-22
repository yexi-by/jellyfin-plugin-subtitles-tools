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
    <article className="grid gap-4 rounded-shell-lg border border-white/8 bg-[linear-gradient(145deg,rgba(243,241,236,0.04),rgba(80,119,154,0.08))] p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="grid gap-2">
          <span className="text-[0.72rem] font-bold uppercase tracking-[0.14em] text-shell-text/62">{part.Label}</span>
          <h4 className="break-all text-lg font-semibold leading-7 text-shell-text">{part.FileName || getFileNameFromPath(part.MediaPath)}</h4>
          <p className="text-sm leading-7 text-shell-text-soft">插件会优先完成纳管与兼容性判断，再决定是否继续搜索字幕与内封。</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge>{(part.Container || 'unknown').toUpperCase()}</Badge>
          <Badge tone={getManagedStatusTone(part)}>{getManagedStatusText(part)}</Badge>
          <Badge tone={getCompatibilityTone(part)}>{getCompatibilityStatusText(part)}</Badge>
          <Badge tone={part.Pipeline ? 'accent' : 'neutral'}>{pipeline}</Badge>
        </div>
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        {[
          ['分段类型', part.PartKind || '未标记'],
          ['分段编号', partNumber],
          ['媒体路径', part.MediaPath || '未返回路径'],
          ['当前流水线', pipeline]
        ].map(([label, value]) => (
          <div key={label} className="grid gap-1.5 rounded-shell-sm border border-white/8 bg-white/4 p-3.5">
            <span className="text-xs text-shell-text/62">{label}</span>
            <strong className="break-all text-sm leading-6 text-shell-text">{value}</strong>
          </div>
        ))}
      </div>
      <p className="text-sm leading-7 text-shell-text-soft">当前插件只根据视频自身的 MKV 元数据判断是否已纳管。若分段仍命中高风险硬解规则，建议优先执行 MKV 转换修复。</p>
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
    <div className="grid gap-3">
      {tracks.map(track => (
        <article key={track.StreamIndex} className="grid gap-4 rounded-shell-lg border border-white/8 bg-white/4 p-5">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="grid gap-2">
              <span className="text-[0.72rem] font-bold uppercase tracking-[0.14em] text-shell-text/62">字幕流 #{track.StreamIndex}</span>
              <h4 className="text-lg font-semibold text-shell-text">{track.Title || `字幕流 #${track.StreamIndex}`}</h4>
              <p className="text-sm leading-7 text-shell-text-soft">这条字幕轨已经存在于当前分段的 MKV 容器中。</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge tone={track.IsPluginManaged ? 'success' : 'neutral'}>{track.IsPluginManaged ? '插件写入' : '现有轨道'}</Badge>
              <Badge>{track.Language || 'und'}</Badge>
              <Badge tone="accent">{(track.Format || 'srt').toUpperCase()}</Badge>
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="grid gap-1.5 rounded-shell-sm border border-white/8 bg-white/4 p-3.5">
              <span className="text-xs text-shell-text/62">绝对流索引</span>
              <strong className="text-sm text-shell-text">{track.StreamIndex}</strong>
            </div>
            <div className="grid gap-1.5 rounded-shell-sm border border-white/8 bg-white/4 p-3.5">
              <span className="text-xs text-shell-text/62">字幕流序号</span>
              <strong className="text-sm text-shell-text">{track.SubtitleStreamIndex ?? '未返回'}</strong>
            </div>
          </div>
          {track.IsPluginManaged ? (
            <div className="flex flex-wrap gap-3">
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
    <div className="grid gap-3">
      {candidates.map(candidate => {
        const languages = candidate.Languages?.length ? candidate.Languages.join(' / ') : candidate.Language || 'und';
        const fingerprintScore = Number.isFinite(candidate.FingerprintScore) ? candidate.FingerprintScore!.toFixed(2) : '-';

        return (
          <article key={candidate.Id} className="grid gap-4 rounded-shell-lg border border-white/8 bg-white/4 p-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="grid gap-2">
                <span className="text-[0.72rem] font-bold uppercase tracking-[0.14em] text-shell-text/62">候选字幕</span>
                <h4 className="text-lg font-semibold leading-7 text-shell-text">{candidate.DisplayName || candidate.Name || '未命名候选字幕'}</h4>
                <p className="text-sm leading-7 text-shell-text-soft">候选项会先转为 UTF-8 SRT，再内封到当前分段对应的 MKV 文件。</p>
              </div>
              <div className="grid place-items-center rounded-shell-lg bg-[linear-gradient(145deg,rgba(208,108,77,0.18),rgba(80,119,154,0.18))] px-4 py-3 text-center text-shell-text">
                <strong className="text-2xl leading-none">{candidate.Score ?? '-'}</strong>
                <span className="mt-1 text-[0.68rem] font-bold uppercase tracking-[0.12em] text-shell-text/70">匹配分</span>
              </div>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              {[
                ['语言', languages],
                ['格式', (candidate.Format || candidate.Ext || 'srt').toUpperCase()],
                ['指纹分', fingerprintScore],
                ['临时文件', candidate.TemporarySrtFileName || '未返回']
              ].map(([label, value]) => (
                <div key={label} className="grid gap-1.5 rounded-shell-sm border border-white/8 bg-white/4 p-3.5">
                  <span className="text-xs text-shell-text/62">{label}</span>
                  <strong className="break-all text-sm leading-6 text-shell-text">{value}</strong>
                </div>
              ))}
            </div>
            <div className="flex flex-wrap gap-3">
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
    <section className="grid gap-4">
      <SectionHeading title="最近一次整组任务" description="这里保留最近一次整组转换或一键最佳匹配任务的结果，方便快速核对每个分段的处理状态。" />
      <div className="grid gap-3">
        {items.map(item => (
          <article
            key={`${item.PartId ?? 'unknown'}-${item.Status ?? 'unknown'}`}
            className={cx(
              'grid gap-4 rounded-shell-lg border p-5',
              getBatchTone(item.Status) === 'success' && 'border-shell-success/35 bg-[linear-gradient(145deg,rgba(109,177,140,0.18),rgba(243,241,236,0.03))]',
              getBatchTone(item.Status) === 'warning' && 'border-shell-warning/35 bg-[linear-gradient(145deg,rgba(216,170,100,0.18),rgba(243,241,236,0.03))]',
              getBatchTone(item.Status) === 'danger' && 'border-shell-danger/35 bg-[linear-gradient(145deg,rgba(212,122,114,0.18),rgba(243,241,236,0.03))]',
              getBatchTone(item.Status) === 'neutral' && 'border-white/8 bg-white/4'
            )}
          >
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="grid gap-2">
                <strong className="text-base font-semibold text-shell-text">{item.Label || '未命名分段'}</strong>
                <span className="text-sm leading-7 text-shell-text-soft">{item.Message || '未返回处理信息。'}</span>
              </div>
              <Badge tone={getBatchTone(item.Status)}>{getBatchStatusText(item.Status)}</Badge>
            </div>
            <div className="flex flex-wrap gap-2">
              {item.Container ? <Badge>{item.Container.toUpperCase()}</Badge> : null}
              {item.Pipeline ? <Badge tone="accent">{item.Pipeline}</Badge> : null}
              {item.RiskVerdict ? <Badge tone={item.NeedsCompatibilityRepair ? 'danger' : 'warning'}>{item.NeedsCompatibilityRepair ? `${item.RiskVerdict}（需修复）` : item.RiskVerdict}</Badge> : null}
            </div>
            {item.MediaPath ? <div className="break-all text-xs leading-6 text-shell-text-faint">{item.MediaPath}</div> : null}
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
    <div className="flex snap-x snap-mandatory gap-3 overflow-x-auto pb-1 xl:grid xl:gap-3 xl:overflow-x-visible xl:pb-0">
      {parts.map(part => (
        <button
          key={part.Id}
          className={cx(
            'grid min-w-[14rem] shrink-0 snap-start gap-3 rounded-shell-md border border-white/8 bg-white/4 p-4 text-left transition hover:-translate-y-0.5 sm:min-w-[15rem] xl:min-w-0 xl:w-full',
            activePartId === part.Id && 'border-shell-accent/35 bg-[linear-gradient(145deg,rgba(208,108,77,0.16),rgba(80,119,154,0.14))]'
          )}
          type="button"
          onClick={() => onSelect(part.Id)}
        >
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="grid gap-1">
              <strong className="text-sm font-semibold text-shell-text">{part.Label}</strong>
              <span className="break-all text-xs leading-6 text-shell-text-faint">{part.FileName || getFileNameFromPath(part.MediaPath)}</span>
            </div>
            <span className="inline-flex h-8 min-w-8 items-center justify-center rounded-full bg-white/8 px-2 text-xs font-bold text-shell-text/82">#{part.Index}</span>
          </div>
          <div className="flex flex-wrap gap-2">
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
        'fixed inset-0 z-[100000] hidden overflow-y-auto bg-[radial-gradient(circle_at_top_right,rgba(208,108,77,0.18),transparent_30%),radial-gradient(circle_at_top_left,rgba(80,119,154,0.18),transparent_28%),rgba(7,10,15,0.82)] px-3 py-3 backdrop-blur-md sm:px-4 sm:py-4 lg:px-6 lg:py-6',
        state.isOverlayOpen && 'block'
      )}
      onClick={event => {
        if (event.target === event.currentTarget) {
          actions.closeOverlay();
        }
      }}
    >
      <div className="mx-auto grid min-h-[calc(100dvh-1.5rem)] w-full max-w-[1280px] grid-cols-1 rounded-none border border-shell-border bg-[linear-gradient(180deg,rgba(18,23,32,0.98),rgba(15,20,27,0.98))] shadow-shell-strong sm:min-h-[calc(100dvh-2rem)] sm:rounded-shell-xl lg:min-h-[calc(100dvh-3rem)] xl:h-[min(56rem,calc(100dvh-3rem))] xl:grid-rows-[auto_auto_auto_minmax(0,1fr)] xl:overflow-hidden">
        <header className="relative grid gap-5 border-b border-shell-border px-[calc(var(--st-safe-left)+1rem)] py-4 pr-[calc(var(--st-safe-right)+1rem)] pt-[calc(var(--st-safe-top)+1rem)] sm:px-5 sm:py-5 lg:px-8 lg:py-7 xl:grid-cols-[minmax(0,1.2fr)_minmax(16rem,0.8fr)]">
          <div className="absolute right-[-7rem] top-[-8rem] h-72 w-72 rounded-full bg-[radial-gradient(circle,rgba(208,108,77,0.24),transparent_72%)]" />
          <div className="absolute bottom-[-10rem] left-[-7rem] h-80 w-80 rounded-full bg-[radial-gradient(circle,rgba(80,119,154,0.2),transparent_74%)]" />
          <div className="relative grid min-w-0 gap-4 pr-12 sm:pr-14">
            <div className="inline-flex items-center gap-2 text-[0.72rem] font-bold uppercase tracking-[0.18em] text-shell-text/68">Subtitles Tools 控制台</div>
            <h2 className="font-serif text-[clamp(1.7rem,2.6vw,2.9rem)] leading-[1.06] text-shell-text sm:text-[clamp(2rem,2.6vw,2.9rem)]">{state.itemData?.Name || '未命名媒体'}</h2>
            <p className="max-w-[42rem] text-sm leading-7 text-shell-text-soft sm:leading-8">{state.itemData?.ItemType || '媒体项目'} · {state.itemData?.IsMultipart ? '多分段媒体' : '单文件媒体'}。当前弹层用于分段纳管、字幕搜索、字幕内封与整组任务复核。</p>
            <div className="flex flex-wrap gap-2">
              <Badge tone="accent">{state.itemData?.IsMultipart ? '多分段媒体' : '单文件媒体'}</Badge>
              {headerSummary.map(item => <Badge key={item}>{item}</Badge>)}
            </div>
          </div>
          <div className="relative grid content-start justify-items-start gap-3 xl:justify-items-end">
            <button
              aria-label="关闭字幕控制台"
              className="absolute right-0 top-0 inline-flex h-11 w-11 items-center justify-center rounded-full border border-white/12 bg-white/5 text-2xl text-shell-text transition hover:rotate-90 hover:bg-white/9 disabled:cursor-not-allowed disabled:opacity-60 xl:static"
              disabled={state.busy}
              type="button"
              onClick={actions.closeOverlay}
            >
              ×
            </button>
            <div className="hidden w-full rounded-shell-md border border-white/8 bg-white/4 p-4 text-sm leading-7 text-shell-text-soft lg:block xl:max-w-[20rem]">
              这套控制台遵循“先纳管与兼容修复，再搜索和内封”的顺序。若当前分段仍被标记为高风险，建议优先执行 MKV 转换。
            </div>
          </div>
        </header>

        <section className="flex gap-3 overflow-x-auto border-b border-shell-border px-[calc(var(--st-safe-left)+1rem)] py-4 pr-[calc(var(--st-safe-right)+1rem)] sm:px-5 sm:py-5 lg:px-8 xl:grid xl:grid-cols-4 xl:overflow-visible">
          {(state.itemData ? getItemMetrics(state.itemData) : []).map(metric => (
            <div key={metric.label} className="min-w-[10.75rem] shrink-0 xl:min-w-0">
              <MetricCard metric={metric} />
            </div>
          ))}
        </section>

        <section className="grid gap-4 border-b border-shell-border px-[calc(var(--st-safe-left)+1rem)] py-4 pr-[calc(var(--st-safe-right)+1rem)] sm:px-5 sm:py-5 lg:px-8">
          <StatusBanner
            label={getStatusLabel(state.statusTone)}
            message={state.statusMessage || '选择左侧分段后，即可开始搜索、转换和内封操作。'}
            title={getStatusTitle(state.statusTone)}
            tone={state.statusTone}
          />
          <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
            <Button className="col-span-1" disabled={state.busy} type="button" variant="secondary" onClick={() => void actions.refresh()}>刷新分段状态</Button>
            <Button className="col-span-1" disabled={state.busy || !activePart} type="button" variant="tertiary" onClick={() => void actions.searchCurrentPart()}>搜索当前分段</Button>
            <Button className="col-span-2 md:col-span-1" disabled={state.busy || !activePart} type="button" variant="secondary" onClick={() => void actions.convertCurrentPart()}>转换当前分段为 MKV</Button>
            <Button className="col-span-1" disabled={state.busy} type="button" variant="secondary" onClick={() => void actions.convertGroup()}>一键整组转换为 MKV</Button>
            <Button className="col-span-2 md:col-span-2 xl:col-span-1" disabled={state.busy} type="button" variant="primary" onClick={() => void actions.downloadBest()}>一键整组最佳匹配并内封</Button>
          </div>
        </section>

        <div className="grid gap-4 px-[calc(var(--st-safe-left)+1rem)] py-4 pr-[calc(var(--st-safe-right)+1rem)] pb-[calc(var(--st-safe-bottom)+1rem)] sm:px-5 sm:py-5 sm:pb-[calc(var(--st-safe-bottom)+1.25rem)] lg:px-8 lg:py-6 xl:min-h-0 xl:grid-cols-[minmax(18rem,21rem)_minmax(0,1fr)] xl:gap-0 xl:p-0">
          <aside className="grid gap-4 rounded-shell-lg border border-shell-border bg-white/2 p-4 sm:p-5 xl:min-h-0 xl:grid-rows-[auto_minmax(0,1fr)] xl:gap-0 xl:rounded-none xl:border-0 xl:border-r xl:bg-white/2 xl:p-0">
            <div className="grid gap-2 xl:border-b xl:border-white/6 xl:px-6 xl:py-5">
              <h3 className="font-serif text-[1.55rem] leading-tight text-shell-text">分段导航</h3>
              <p className="text-sm leading-7 text-shell-text-soft">左侧显示分段容器、纳管状态与兼容性状态。先选择分段，再执行搜索、转换或单段内封。</p>
            </div>
            <div className="xl:grid xl:content-start xl:gap-3 xl:overflow-y-auto xl:px-6 xl:py-4">
              <PartNavigation activePartId={state.activePartId} onSelect={actions.selectPart} parts={state.itemData?.Parts ?? []} />
            </div>
          </aside>

          <main className="grid content-start gap-5 rounded-shell-lg border border-shell-border bg-white/[0.02] p-4 sm:p-5 xl:min-h-0 xl:overflow-y-auto xl:rounded-none xl:border-0 xl:bg-transparent xl:px-8 xl:py-6">
            <section className="grid gap-4">
              <SectionHeading title="当前分段概览" description="这里聚焦当前分段的纳管状态、兼容性判断、路径与流水线信息，用来决定下一步操作。" />
              <CurrentPartCard part={activePart} />
            </section>

            <section className="grid gap-4">
              <SectionHeading title="当前已内封字幕流" description="区分插件写入轨道与原有轨道。只有插件写入的字幕流会显示删除按钮。" />
              <EmbeddedSubtitleList onDelete={actions.deleteEmbeddedSubtitle} part={activePart} />
            </section>

            <section className="grid gap-4">
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
    <div
      className={cx(
        'fixed z-[99999] translate-y-3 opacity-0 transition [bottom:calc(var(--st-safe-bottom)+1rem)] [right:calc(var(--st-safe-right)+1rem)] sm:[bottom:calc(var(--st-safe-bottom)+1.25rem)] sm:[right:calc(var(--st-safe-right)+1.25rem)] lg:[bottom:calc(var(--st-safe-bottom)+2rem)] lg:[right:calc(var(--st-safe-right)+2rem)]',
        visible && 'translate-y-0 opacity-100'
      )}
    >
      <button
        className="grid min-w-[8.5rem] gap-0.5 rounded-[1.15rem] border border-white/8 bg-[linear-gradient(140deg,rgba(208,108,77,0.94),rgba(142,63,47,0.98))] px-4 py-3 text-left text-[rgb(255,248,244)] shadow-[0_18px_38px_rgba(10,13,20,0.38)] transition hover:-translate-y-0.5 sm:min-w-[9.75rem] sm:gap-1 sm:rounded-[1.25rem] sm:px-5 sm:py-4"
        type="button"
        onClick={onOpen}
      >
        <span className="text-sm font-bold tracking-[0.04em]">字幕控制台</span>
        <span className="hidden text-[0.68rem] font-semibold uppercase tracking-[0.14em] text-[rgba(255,248,244,0.74)] sm:block">搜索 · 转换 · 内封</span>
      </button>
    </div>
  );
}
