import type { ButtonHTMLAttributes, HTMLAttributes, JSX, ReactNode } from 'react';
import type { BatchMetric, ConnectionMetric, UiTone } from '../shared/types';

type ButtonVariant = 'danger' | 'primary' | 'secondary' | 'tertiary';

function cx(...classNames: Array<string | false | null | undefined>): string {
  return classNames.filter(Boolean).join(' ');
}

export function Button({
  children,
  className,
  variant = 'secondary',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }): JSX.Element {
  const variants: Record<ButtonVariant, string> = {
    danger: 'bg-[linear-gradient(135deg,rgba(212,122,114,0.92),rgba(138,58,54,0.96))] text-shell-text shadow-[0_12px_24px_rgba(138,58,54,0.22)]',
    primary: 'bg-[linear-gradient(135deg,var(--color-shell-accent),var(--color-shell-accent-strong))] text-shell-text shadow-[0_14px_28px_rgba(142,63,47,0.3)]',
    secondary: 'border border-shell-border bg-white/5 text-shell-text',
    tertiary: 'border border-shell-cool/20 bg-[linear-gradient(135deg,rgba(80,119,154,0.22),rgba(51,72,95,0.28))] text-shell-text'
  };

  return (
    <button
      {...props}
      className={cx(
        'inline-flex min-h-11 min-w-0 items-center justify-center rounded-[1rem] border border-transparent px-4 py-2.5 text-center text-sm font-semibold leading-6 transition disabled:cursor-not-allowed disabled:opacity-60 hover:-translate-y-0.5 disabled:hover:translate-y-0 sm:min-h-12 sm:rounded-full sm:px-5 sm:py-3',
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
    accent: 'border-shell-accent/35 bg-shell-accent/15 text-[rgb(255,216,204)]',
    danger: 'border-shell-danger/30 bg-shell-danger/18 text-[rgb(255,215,209)]',
    neutral: 'border-shell-border bg-white/5 text-shell-text/85',
    success: 'border-shell-success/30 bg-shell-success/15 text-[rgb(213,244,225)]',
    warning: 'border-shell-warning/30 bg-shell-warning/15 text-[rgb(255,227,188)]'
  };

  return (
    <span className={cx('inline-flex items-center rounded-full border px-3 py-1.5 text-xs font-semibold', tones[tone])}>
      {children}
    </span>
  );
}

export function Panel({
  children,
  className
}: HTMLAttributes<HTMLElement>): JSX.Element {
  return (
    <section className={cx('grid gap-5 rounded-shell-lg border border-shell-border bg-shell-panel p-6 shadow-shell-soft', className)}>
      {children}
    </section>
  );
}

export function SectionHeading({
  description,
  eyebrow,
  title
}: {
  description?: ReactNode;
  eyebrow?: ReactNode;
  title: ReactNode;
}): JSX.Element {
  return (
    <div className="grid gap-1.5 sm:gap-2">
      {eyebrow ? <div className="text-[0.7rem] font-bold uppercase tracking-[0.18em] text-shell-text/65">{eyebrow}</div> : null}
      <h2 className="font-serif text-[clamp(1.25rem,2vw,1.8rem)] leading-tight sm:text-[clamp(1.4rem,2vw,1.8rem)]">{title}</h2>
      {description ? <p className="max-w-[44rem] text-sm leading-6 text-shell-text-soft sm:leading-7">{description}</p> : null}
    </div>
  );
}

export function FieldShell({
  children,
  description,
  label
}: {
  children: ReactNode;
  description: ReactNode;
  label: string;
}): JSX.Element {
  return (
    <label className="grid gap-3 rounded-shell-md border border-white/8 bg-white/4 p-4">
      <span className="text-sm font-semibold text-shell-text">{label}</span>
      {children}
      <span className="text-[0.82rem] leading-6 text-shell-text-soft">{description}</span>
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
    busy: 'border-shell-cool/35 bg-[linear-gradient(145deg,rgba(80,119,154,0.16),rgba(243,241,236,0.03))]',
    error: 'border-shell-danger/35 bg-[linear-gradient(145deg,rgba(212,122,114,0.18),rgba(243,241,236,0.03))]',
    idle: 'border-white/8 bg-white/4',
    success: 'border-shell-success/35 bg-[linear-gradient(145deg,rgba(109,177,140,0.18),rgba(243,241,236,0.03))]'
  };

  return (
    <div className={cx('grid gap-3 rounded-shell-md border p-3.5 sm:p-4', styles[tone])}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <span className="rounded-full bg-white/8 px-3 py-1.5 text-[0.7rem] font-bold uppercase tracking-[0.14em] text-shell-text/82">{label}</span>
        <span className="text-sm font-semibold text-shell-text">{title}</span>
      </div>
      <p className="text-sm leading-6 text-shell-text-soft sm:leading-7">{message}</p>
    </div>
  );
}

export function ConnectionMetrics({ metrics }: { metrics: ConnectionMetric[] }): JSX.Element | null {
  if (metrics.length === 0) {
    return null;
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {metrics.map(metric => (
        <div key={metric.label} className="grid gap-1.5 rounded-shell-sm border border-white/8 bg-white/4 p-3.5">
          <span className="text-xs text-shell-text/65">{metric.label}</span>
          <strong className="break-all text-sm leading-6 text-shell-text">{metric.value}</strong>
        </div>
      ))}
    </div>
  );
}

export function MetricCard({ metric }: { metric: BatchMetric }): JSX.Element {
  const toneClasses: Record<BatchMetric['tone'], string> = {
    danger: 'bg-[linear-gradient(145deg,rgba(212,122,114,0.16),rgba(243,241,236,0.03))]',
    neutral: 'bg-white/4',
    success: 'bg-[linear-gradient(145deg,rgba(109,177,140,0.14),rgba(243,241,236,0.03))]',
    warning: 'bg-[linear-gradient(145deg,rgba(216,170,100,0.14),rgba(243,241,236,0.03))]'
  };

  return (
    <div className={cx('grid min-h-[6rem] gap-1.5 rounded-shell-lg border border-white/8 p-3.5 sm:min-h-[6.5rem] sm:gap-2 sm:p-4 xl:min-h-[7rem]', toneClasses[metric.tone])}>
      <span className="text-[0.7rem] font-bold uppercase tracking-[0.14em] text-shell-text/62">{metric.label}</span>
      <strong className="text-[clamp(1.6rem,2.2vw,2.4rem)] leading-none text-shell-text sm:text-[clamp(1.8rem,2.2vw,2.4rem)]">{metric.value}</strong>
      <span className="text-sm leading-5 text-shell-text-soft sm:leading-6">{metric.note}</span>
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
    <div className="grid gap-3 rounded-shell-lg border border-white/8 bg-white/4 p-4 sm:p-5">
      <strong className="text-base font-semibold text-shell-text">{title}</strong>
      <span className="text-sm leading-6 text-shell-text-soft sm:leading-7">{description}</span>
    </div>
  );
}

export { cx };
