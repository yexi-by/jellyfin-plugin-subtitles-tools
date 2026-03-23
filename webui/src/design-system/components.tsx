import type { ButtonHTMLAttributes, HTMLAttributes, JSX, ReactNode } from 'react';
import type { BatchMetric, ConnectionMetric, SubtitleWriteMode, UiTone } from '../shared/types';

type ButtonVariant = 'danger' | 'primary' | 'secondary' | 'tertiary';
type ButtonSize = 'md' | 'sm';

function cx(...classNames: Array<string | false | null | undefined>): string {
  return classNames.filter(Boolean).join(' ');
}

export function Button({
  children,
  className,
  size = 'md',
  variant = 'secondary',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { size?: ButtonSize; variant?: ButtonVariant }): JSX.Element {
  const variants: Record<ButtonVariant, string> = {
    danger: 'border border-red-500/20 bg-red-500/10 text-red-400 hover:bg-red-500/20',
    primary: 'border border-transparent bg-blue-600 text-white hover:bg-blue-500',
    secondary: 'border border-white/10 bg-white/5 text-gray-200 hover:bg-white/10',
    tertiary: 'border border-transparent bg-transparent text-gray-400 hover:bg-white/5 hover:text-gray-200'
  };
  const sizes: Record<ButtonSize, string> = {
    md: 'min-h-11 px-4 py-2 text-sm',
    sm: 'min-h-11 px-3 py-2 text-sm'
  };

  return (
    <button
      {...props}
      className={cx(
        'inline-flex min-w-0 items-center justify-center rounded-lg font-medium whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        sizes[size],
        variants[variant],
        className
      )}
    >
      {children}
    </button>
  );
}

export function Badge({
  children,
  tone = 'neutral'
}: {
  children: ReactNode;
  tone?: Extract<UiTone, 'accent' | 'danger' | 'neutral' | 'success' | 'warning'>;
}): JSX.Element {
  const tones: Record<typeof tone, string> = {
    accent: 'border-blue-500/20 bg-blue-500/10 text-blue-400',
    danger: 'border-red-500/20 bg-red-500/10 text-red-400',
    neutral: 'border-white/10 bg-white/5 text-gray-300',
    success: 'border-green-500/20 bg-green-500/10 text-green-400',
    warning: 'border-yellow-500/20 bg-yellow-500/10 text-yellow-400'
  };

  return (
    <span className={cx('inline-flex items-center rounded-md border px-2 py-0.5 text-xs font-medium', tones[tone])}>
      {children}
    </span>
  );
}

export function Panel({
  children,
  className
}: HTMLAttributes<HTMLElement>): JSX.Element {
  return (
    <section className={cx('flex flex-col gap-4 rounded-xl border border-white/10 bg-[#1e1e1e] p-5 shadow-sm', className)}>
      {children}
    </section>
  );
}

export function SectionHeading({
  description,
  title
}: {
  description?: ReactNode;
  title: ReactNode;
}): JSX.Element {
  return (
    <div className="flex flex-col gap-1">
      <h2 className="text-lg font-medium text-gray-100">{title}</h2>
      {description ? <p className="text-sm text-gray-400">{description}</p> : null}
    </div>
  );
}

export function FieldShell({
  children,
  description,
  label
}: {
  children: ReactNode;
  description?: ReactNode;
  label: string;
}): JSX.Element {
  return (
    <label className="flex flex-col gap-2">
      <span className="text-sm font-medium text-gray-200">{label}</span>
      {children}
      {description ? <span className="text-xs text-gray-500">{description}</span> : null}
    </label>
  );
}

export function StatusBanner({
  label,
  message,
  title,
  tone
}: {
  label: string;
  message: string;
  title: string;
  tone: Extract<UiTone, 'busy' | 'error' | 'idle' | 'success'>;
}): JSX.Element {
  const styles = {
    busy: 'border-blue-500/20 bg-blue-500/5 text-blue-200',
    error: 'border-red-500/20 bg-red-500/5 text-red-200',
    idle: 'border-white/10 bg-white/5 text-gray-300',
    success: 'border-green-500/20 bg-green-500/5 text-green-200'
  };

  return (
    <div className={cx('flex flex-col gap-1.5 rounded-lg border p-4', styles[tone])}>
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium">{title}</span>
        <span className="rounded bg-black/20 px-1.5 py-0.5 text-[10px] uppercase tracking-wider opacity-80">{label}</span>
      </div>
      <p className="text-sm opacity-80">{message}</p>
    </div>
  );
}

export function ConnectionMetrics({ metrics }: { metrics: ConnectionMetric[] }): JSX.Element | null {
  if (metrics.length === 0) {
    return null;
  }

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
      {metrics.map(metric => (
        <div key={metric.label} className="flex flex-col gap-1 rounded-lg border border-white/5 bg-white/5 p-3">
          <span className="text-xs text-gray-400">{metric.label}</span>
          <strong className="truncate text-sm font-medium text-gray-200">{metric.value}</strong>
        </div>
      ))}
    </div>
  );
}

export function MetricCard({ metric }: { metric: BatchMetric }): JSX.Element {
  const toneClasses: Record<BatchMetric['tone'], string> = {
    danger: 'border-red-500/20 bg-red-500/5',
    neutral: 'border-white/10 bg-white/5',
    success: 'border-green-500/20 bg-green-500/5',
    warning: 'border-yellow-500/20 bg-yellow-500/5'
  };

  return (
    <div className={cx('flex h-full flex-col gap-1 rounded-xl border p-4', toneClasses[metric.tone])}>
      <span className="text-xs text-gray-400">{metric.label}</span>
      <strong className="text-2xl font-semibold text-gray-100">{metric.value}</strong>
      {metric.note ? <span className="text-xs leading-5 text-gray-500">{metric.note}</span> : null}
    </div>
  );
}

export function EmptyState({
  description,
  title
}: {
  description: ReactNode;
  title: ReactNode;
}): JSX.Element {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-white/20 p-8 text-center">
      <strong className="text-sm font-medium text-gray-300">{title}</strong>
      <span className="text-sm text-gray-500">{description}</span>
    </div>
  );
}

export function WriteModeSwitch({
  disabled = false,
  mode,
  onChange
}: {
  disabled?: boolean;
  mode: SubtitleWriteMode;
  onChange: (mode: SubtitleWriteMode) => void;
}): JSX.Element {
  return (
    <div className="flex w-full overflow-hidden rounded-lg border border-white/10 bg-black/20 p-1">
      <button
        className={cx(
          'min-h-11 flex-1 rounded-md px-3 py-2 text-sm font-medium transition-colors',
          mode === 'embedded' ? 'bg-white/10 text-white shadow-sm' : 'text-gray-400 hover:text-gray-200',
          disabled && 'cursor-not-allowed opacity-50'
        )}
        disabled={disabled}
        type="button"
        onClick={() => onChange('embedded')}
      >
        写入视频
      </button>
      <button
        className={cx(
          'min-h-11 flex-1 rounded-md px-3 py-2 text-sm font-medium transition-colors',
          mode === 'sidecar' ? 'bg-white/10 text-white shadow-sm' : 'text-gray-400 hover:text-gray-200',
          disabled && 'cursor-not-allowed opacity-50'
        )}
        disabled={disabled}
        type="button"
        onClick={() => onChange('sidecar')}
      >
        另存字幕
      </button>
    </div>
  );
}

export { cx };
