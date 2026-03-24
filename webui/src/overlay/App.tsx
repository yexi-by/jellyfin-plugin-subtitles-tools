import { useState, type JSX } from 'react';
import {
  Badge,
  Button,
  ConfirmDialog,
  EmptyState,
  IconClose,
  IconTrash,
  MetricCard,
  SectionHeading,
  StatusBanner,
  WriteModeSwitch,
  cx
} from '../design-system/components';
import { getFileNameFromPath } from '../shared/dom';
import {
  getBatchStatusText,
  getBatchSummaryMessage,
  getBatchTone,
  getCompatibilityStatusText,
  getCompatibilityTone,
  getDefaultOverlayStatus,
  getItemMetrics,
  getManagedStatusText,
  getManagedStatusTone,
  getSubtitleSourceScoreText,
  getSubtitleWriteModeLabel
} from '../shared/formatters';
import type { EmbeddedSubtitleTrack, ExternalSubtitleTrack, MediaPart, OperationResultItem, SubtitleCandidate, SubtitleWriteMode } from '../shared/types';
import type { OverlayStoreState } from './store';

interface OverlayActions {
  closeOverlay: () => void;
  convertCurrentPart: () => Promise<void>;
  convertGroup: () => Promise<void>;
  deleteEmbeddedSubtitle: (streamIndex: number) => Promise<void>;
  deleteExternalSubtitle: (filePath: string) => Promise<void>;
  downloadBest: () => Promise<void>;
  downloadCandidate: (candidateId: string) => Promise<void>;
  refresh: () => Promise<void>;
  searchCurrentPart: () => Promise<void>;
  selectPart: (partId: string) => void;
  setSubtitleWriteMode: (mode: SubtitleWriteMode) => void;
}

function CurrentPartCard({
  part,
  writeMode
}: {
  part: MediaPart | null;
  writeMode: SubtitleWriteMode;
}): JSX.Element {
  if (!part) {
    return <EmptyState title="还没有选择文件" description="先选择一个文件，再开始处理。" />;
  }

  const embeddedCount = part.EmbeddedSubtitles?.length ?? 0;
  const externalCount = part.ExternalSubtitles?.length ?? 0;

  return (
    <article className="flex flex-col gap-4 rounded-xl border border-white/5 bg-white/5 p-4">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0 flex-1">
          <h4 className="text-base font-medium text-gray-100">{part.Label || '当前文件'}</h4>
          <p className="mt-1 break-all text-xs text-gray-500">{part.FileName || getFileNameFromPath(part.MediaPath)}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge tone={getManagedStatusTone(part)}>{getManagedStatusText(part)}</Badge>
          <Badge tone={getCompatibilityTone(part)}>{getCompatibilityStatusText(part)}</Badge>
        </div>
      </div>

      <div className="grid gap-3 rounded-lg border border-white/5 bg-black/20 p-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-gray-500">保存方式</span>
          <span className="text-sm font-medium text-gray-200">{getSubtitleWriteModeLabel(writeMode)}</span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-gray-500">已有字幕</span>
          <span className="text-sm font-medium text-gray-200">{embeddedCount + externalCount} 条</span>
        </div>
      </div>

      <details className="rounded-lg border border-white/5 bg-black/20">
        <summary className="cursor-pointer list-none px-3 py-3 text-sm font-medium text-gray-200 [&::-webkit-details-marker]:hidden">
          查看技术信息
        </summary>
        <div className="grid gap-3 border-t border-white/5 px-3 py-3 sm:grid-cols-2">
          <InfoField label="容器" value={(part.Container || '-').toUpperCase()} />
          <InfoField label="风险判断" value={part.RiskVerdict || '等待检查'} />
          <InfoField label="文件路径" value={part.MediaPath || '-'} />
          <InfoField label="字幕轨道数" value={String(embeddedCount + externalCount)} />
        </div>
      </details>
    </article>
  );
}

function InfoField({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-gray-500">{label}</span>
      <span className="break-all text-sm text-gray-300">{value}</span>
    </div>
  );
}

function SubtitleTrackRow({
  action,
  actionLabel,
  badges,
  meta,
  title
}: {
  action?: () => void;
  actionLabel?: string;
  badges: JSX.Element[];
  meta: string;
  title: string;
}): JSX.Element {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-white/5 bg-white/5 p-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="break-all text-sm font-medium text-gray-200 sm:break-normal">{title}</span>
          {badges}
        </div>
        <span className="mt-1 block text-xs text-gray-500">{meta}</span>
      </div>
      {action && actionLabel ? (
        <Button className="w-full sm:w-auto" type="button" variant="danger" onClick={action}>
          <IconTrash size={16} />
          {actionLabel}
        </Button>
      ) : null}
    </div>
  );
}

type DeleteConfirmState = {
  type: 'embedded';
  streamIndex: number;
  title: string;
} | {
  type: 'external';
  filePath: string;
  title: string;
} | null;

function SubtitleList({
  embedded,
  external,
  onDeleteEmbedded,
  onDeleteExternal
}: {
  embedded: EmbeddedSubtitleTrack[] | undefined;
  external: ExternalSubtitleTrack[] | undefined;
  onDeleteEmbedded: (streamIndex: number) => Promise<void>;
  onDeleteExternal: (filePath: string) => Promise<void>;
}): JSX.Element {
  const [deleteConfirm, setDeleteConfirm] = useState<DeleteConfirmState>(null);
  const [isDeleting, setIsDeleting] = useState(false);

  const embeddedTracks = embedded ?? [];
  const externalTracks = external ?? [];
  const hasSubtitles = embeddedTracks.length > 0 || externalTracks.length > 0;

  const handleConfirmDelete = async () => {
    if (!deleteConfirm) return;

    setIsDeleting(true);
    try {
      if (deleteConfirm.type === 'embedded') {
        await onDeleteEmbedded(deleteConfirm.streamIndex);
      } else {
        await onDeleteExternal(deleteConfirm.filePath);
      }
    } finally {
      setIsDeleting(false);
      setDeleteConfirm(null);
    }
  };

  if (!hasSubtitles) {
    return <EmptyState title="当前文件还没有字幕" description="先搜索字幕，再选择保存方式。" />;
  }

  return (
    <>
      <div className="flex flex-col gap-3">
        {embeddedTracks.map(track => (
          <SubtitleTrackRow
            key={`embedded-${track.StreamIndex}`}
            action={track.IsPluginManaged ? () => setDeleteConfirm({
              type: 'embedded',
              streamIndex: track.StreamIndex,
              title: track.Title || `字幕轨道 #${track.StreamIndex}`
            }) : undefined}
            actionLabel={track.IsPluginManaged ? '删除' : undefined}
            badges={[
              <Badge key="kind" tone="neutral">视频内字幕</Badge>,
              ...(track.IsPluginManaged ? [<Badge key="managed" tone="success">由本工具添加</Badge>] : [])
            ]}
            meta={`语言：${track.Language || '未知'} · 格式：${(track.Format || 'srt').toUpperCase()}`}
            title={track.Title || `字幕轨道 #${track.StreamIndex}`}
          />
        ))}
        {externalTracks.map(track => {
          const filePath = track.FilePath;
          return (
            <SubtitleTrackRow
              key={`external-${filePath || track.FileName}`}
              action={filePath ? () => setDeleteConfirm({
                type: 'external',
                filePath,
                title: track.FileName || '外挂字幕'
              }) : undefined}
              actionLabel={filePath ? '删除' : undefined}
              badges={[<Badge key="kind" tone="neutral">外挂字幕</Badge>]}
              meta={`语言：${track.Language || '未知'} · 格式：${(track.Format || 'srt').toUpperCase()}`}
              title={track.FileName || '外挂字幕'}
            />
          );
        })}
      </div>

      <ConfirmDialog
        open={deleteConfirm !== null}
        title="删除字幕"
        message={`确定要删除「${deleteConfirm?.title || ''}」吗？此操作不可撤销。`}
        confirmText="删除"
        tone="danger"
        loading={isDeleting}
        onConfirm={handleConfirmDelete}
        onCancel={() => setDeleteConfirm(null)}
      />
    </>
  );
}

function CandidateList({
  candidates,
  onDownload,
  writeMode
}: {
  candidates: SubtitleCandidate[];
  onDownload: (candidateId: string) => Promise<void>;
  writeMode: SubtitleWriteMode;
}): JSX.Element {
  if (candidates.length === 0) {
    return <EmptyState title="还没有字幕结果" description="点击“搜索当前文件”开始查找。" />;
  }

  const actionLabel = getSubtitleWriteModeLabel(writeMode);

  return (
    <div className="flex flex-col gap-3">
      {candidates.map(candidate => {
        const score = getSubtitleSourceScoreText(candidate.Score);
        const language = candidate.Language || candidate.Languages?.join(', ') || '未知';
        const format = (candidate.Format || candidate.Ext || 'srt').toUpperCase();

        return (
          <div key={candidate.Id} className="flex flex-col gap-3 rounded-lg border border-white/5 bg-white/5 p-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0 flex-1">
              <span className="block break-all text-sm font-medium text-gray-200">
                {candidate.DisplayName || candidate.Name || '未知字幕'}
              </span>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-gray-500">
                <span className="rounded bg-black/20 px-1.5 py-0.5">语言：{language}</span>
                <span className="rounded bg-black/20 px-1.5 py-0.5">格式：{format}</span>
                <span className="rounded bg-black/20 px-1.5 py-0.5">来源评分：{score}</span>
              </div>
            </div>
            <Button className="w-full sm:w-auto" type="button" variant="primary" onClick={() => void onDownload(candidate.Id)}>
              {actionLabel}
            </Button>
          </div>
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
    <section className="flex flex-col gap-3">
      <SectionHeading title="最近处理结果" />
      <div className="flex flex-col gap-2">
        {items.map(item => (
          <div
            key={`${item.PartId ?? 'unknown'}-${item.Status ?? 'unknown'}`}
            className="flex flex-col gap-3 rounded-lg border border-white/5 bg-white/5 p-3 sm:flex-row sm:items-center sm:justify-between"
          >
            <div className="min-w-0 flex-1">
              <span className="block text-sm font-medium text-gray-200">{item.Label || '当前文件'}</span>
              <span className="mt-1 block text-xs text-gray-500">{getBatchSummaryMessage(item)}</span>
            </div>
            <Badge tone={getBatchTone(item.Status)}>{getBatchStatusText(item.Status)}</Badge>
          </div>
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
    return <div className="text-sm text-gray-500">没有可用文件。</div>;
  }

  return (
    <div className="flex gap-2 overflow-x-auto pb-1 lg:flex-col lg:overflow-x-visible lg:pb-0">
      {parts.map(part => (
        <button
          key={part.Id}
          className={cx(
            'flex min-w-[12rem] shrink-0 flex-col gap-2 rounded-lg border p-3 text-left transition-colors lg:min-w-0',
            activePartId === part.Id ? 'border-blue-500/30 bg-blue-500/10' : 'border-white/5 bg-white/5 hover:border-white/10'
          )}
          type="button"
          onClick={() => onSelect(part.Id)}
        >
          <div className="flex items-start justify-between gap-2">
            <span className="line-clamp-2 text-sm font-medium text-gray-200">{part.Label || `文件 ${part.Index}`}</span>
            <Badge tone={getManagedStatusTone(part)}>{getManagedStatusText(part)}</Badge>
          </div>
          <span className="truncate text-xs text-gray-500">{getFileNameFromPath(part.MediaPath)}</span>
          <div className="flex flex-wrap gap-2">
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
  const isMultipart = state.itemData?.IsMultipart === true;
  const metrics = state.itemData ? getItemMetrics(state.itemData) : [];
  const fallbackStatus = getDefaultOverlayStatus(activePart);
  const bannerTitle = state.statusTitle || fallbackStatus.title;
  const bannerMessage = state.statusMessage || fallbackStatus.message;
  const bannerTone = state.statusTitle || state.statusMessage ? state.statusTone : fallbackStatus.tone;
  const typeLabel = state.itemData?.ItemType || '媒体';
  const structureLabel = isMultipart ? '多文件' : '单文件';

  return (
    <div
      className={cx(
        'fixed inset-0 z-[100000] flex items-stretch justify-center bg-black/60 p-2 backdrop-blur-sm transition-opacity sm:items-center sm:p-4 lg:p-6',
        state.isOverlayOpen ? 'opacity-100' : 'pointer-events-none opacity-0'
      )}
      onClick={event => {
        if (event.target === event.currentTarget) {
          actions.closeOverlay();
        }
      }}
    >
      <div className="flex h-[calc(100dvh-1rem)] max-h-[calc(100dvh-1rem)] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-white/10 bg-[var(--color-shell-bg)] shadow-2xl sm:h-[min(100dvh-2rem,56rem)] sm:max-h-[min(100dvh-2rem,56rem)]">
        <header className="flex flex-shrink-0 items-start justify-between gap-3 border-b border-white/5 p-4 sm:p-5">
          <div className="min-w-0 flex-1">
            <h2 className="text-xl font-semibold text-gray-100">{state.itemData?.Name || '字幕处理'}</h2>
            <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-gray-500">
              <span>{typeLabel}</span>
              <span>·</span>
              <span>{structureLabel}</span>
            </div>
          </div>
          <button
            aria-label="关闭"
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-white/5 text-gray-400 transition-colors hover:bg-white/10 hover:text-white"
            type="button"
            onClick={actions.closeOverlay}
          >
            <IconClose size={20} />
          </button>
        </header>

        {isMultipart ? (
          <div className="grid flex-shrink-0 grid-cols-2 gap-3 border-b border-white/5 p-4 sm:grid-cols-4 sm:p-5">
            {metrics.map(metric => (
              <MetricCard key={metric.label} metric={metric} />
            ))}
          </div>
        ) : null}

        <div className="flex flex-shrink-0 flex-col gap-4 border-b border-white/5 p-4 sm:p-5">
          <StatusBanner label={bannerTone === 'idle' ? '提醒' : bannerTone === 'busy' ? '进行中' : bannerTone === 'success' ? '成功' : '失败'} message={bannerMessage} title={bannerTitle} tone={bannerTone} />

          <section className="flex flex-col gap-3">
            <SectionHeading title="当前文件操作" />
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 xl:grid-cols-4">
              <Button className="w-full" disabled={state.busy} type="button" variant="secondary" onClick={() => void actions.refresh()}>
                刷新状态
              </Button>
              <Button className="w-full" disabled={state.busy || !activePart} type="button" variant="primary" onClick={() => void actions.searchCurrentPart()}>
                搜索当前文件
              </Button>
              <Button className="w-full" disabled={state.busy || !activePart} type="button" variant="secondary" onClick={() => void actions.convertCurrentPart()}>
                优化当前文件
              </Button>
              <div className="xl:col-span-1">
                <WriteModeSwitch disabled={state.busy} mode={state.subtitleWriteMode} onChange={actions.setSubtitleWriteMode} />
              </div>
            </div>
          </section>

          {isMultipart ? (
            <section className="rounded-xl border border-white/10 bg-white/[0.03] p-4">
              <SectionHeading title="整组操作" description="一次处理当前媒体的全部文件，耗时会更长。" />
              <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
                <Button className="w-full" disabled={state.busy} type="button" variant="secondary" onClick={() => void actions.convertGroup()}>
                  整组优化
                </Button>
                <Button className="w-full" disabled={state.busy} type="button" variant="primary" onClick={() => void actions.downloadBest()}>
                  整组自动选字幕
                </Button>
              </div>
            </section>
          ) : null}
        </div>

        {isMultipart ? (
          <div className="border-b border-white/5 p-4 lg:hidden">
            <PartNavigation activePartId={state.activePartId} onSelect={actions.selectPart} parts={state.itemData?.Parts ?? []} />
          </div>
        ) : null}

        <div className="flex min-h-0 flex-1 flex-col lg:flex-row">
          {isMultipart ? (
            <aside className="hidden overflow-y-auto border-r border-white/5 bg-[var(--color-shell-bg)] p-4 lg:block lg:w-[18rem] lg:shrink-0">
              <PartNavigation activePartId={state.activePartId} onSelect={actions.selectPart} parts={state.itemData?.Parts ?? []} />
            </aside>
          ) : null}

          <main className="flex-1 overflow-y-auto p-4 sm:p-5">
            <div className="mx-auto flex max-w-4xl flex-col gap-6 sm:gap-8">
              <section className="flex flex-col gap-3">
                <SectionHeading title="当前文件" />
                <CurrentPartCard part={activePart} writeMode={state.subtitleWriteMode} />
              </section>

              <section className="flex flex-col gap-3">
                <SectionHeading title="找到的字幕" />
                <CandidateList candidates={activeCandidates} onDownload={actions.downloadCandidate} writeMode={state.subtitleWriteMode} />
              </section>

              <section className="flex flex-col gap-3">
                <SectionHeading title="已有字幕" />
                <SubtitleList
                  embedded={activePart?.EmbeddedSubtitles}
                  external={activePart?.ExternalSubtitles}
                  onDeleteEmbedded={actions.deleteEmbeddedSubtitle}
                  onDeleteExternal={actions.deleteExternalSubtitle}
                />
              </section>

              <BatchSummary items={state.lastBatchItems} />
            </div>
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
        'fixed z-[99999] transition-all duration-300 [bottom:calc(var(--st-safe-bottom)+1rem)] [right:calc(var(--st-safe-right)+1rem)] sm:[bottom:calc(var(--st-safe-bottom)+1.25rem)] sm:[right:calc(var(--st-safe-right)+1.25rem)]',
        visible ? 'translate-y-0 opacity-100' : 'pointer-events-none translate-y-4 opacity-0'
      )}
    >
      <button
        className="flex min-h-11 items-center gap-2 rounded-full bg-blue-600 px-5 py-3 text-white shadow-lg transition-transform hover:scale-105 hover:bg-blue-500"
        type="button"
        onClick={onOpen}
      >
        <span className="text-sm font-medium">字幕处理</span>
      </button>
    </div>
  );
}
