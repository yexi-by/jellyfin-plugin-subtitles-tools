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
    danger: 'st-bg-[linear-gradient(135deg,rgba(212,122,114,0.92),rgba(138,58,54,0.96))] st-text-shell-text st-shadow-[0_12px_24px_rgba(138,58,54,0.22)]',
    primary: 'st-bg-[linear-gradient(135deg,var(--color-shell-accent),var(--color-shell-accent-strong))] st-text-shell-text st-shadow-[0_14px_28px_rgba(142,63,47,0.3)]',
    secondary: 'st-border st-border-shell-border st-bg-white/5 st-text-shell-text',
    tertiary: 'st-border st-border-shell-cool/20 st-bg-[linear-gradient(135deg,rgba(80,119,154,0.22),rgba(51,72,95,0.28))] st-text-shell-text'
  };

  return (
    <button
      {...props}
      className={cx(
        'st-inline-flex st-min-h-12 st-items-center st-justify-center st-rounded-full st-border st-border-transparent st-px-5 st-py-3 st-text-sm st-font-semibold st-transition disabled:st-cursor-not-allowed disabled:st-opacity-60 hover:st--translate-y-0.5 disabled:hover:st-translate-y-0',
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
    accent: 'st-border-shell-accent/35 st-bg-shell-accent/15 st-text-[rgb(255,216,204)]',
    danger: 'st-border-shell-danger/30 st-bg-shell-danger/18 st-text-[rgb(255,215,209)]',
    neutral: 'st-border-shell-border st-bg-white/5 st-text-shell-text/85',
    success: 'st-border-shell-success/30 st-bg-shell-success/15 st-text-[rgb(213,244,225)]',
    warning: 'st-border-shell-warning/30 st-bg-shell-warning/15 st-text-[rgb(255,227,188)]'
  };

  return (
    <span className={cx('st-inline-flex st-items-center st-rounded-full st-border st-px-3 st-py-1.5 st-text-xs st-font-semibold', tones[tone])}>
      {children}
    </span>
  );
}

export function Panel({
  children,
  className
}: HTMLAttributes<HTMLElement>): JSX.Element {
  return (
    <section className={cx('st-grid st-gap-5 st-rounded-shell-lg st-border st-border-shell-border st-bg-shell-panel st-p-6 st-shadow-shell-soft', className)}>
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
    <div className="st-grid st-gap-2">
      {eyebrow ? <div className="st-text-[0.7rem] st-font-bold st-uppercase st-tracking-[0.18em] st-text-shell-text/65">{eyebrow}</div> : null}
      <h2 className="st-font-serif st-text-[clamp(1.4rem,2vw,1.8rem)] st-leading-tight">{title}</h2>
      {description ? <p className="st-max-w-[44rem] st-text-sm st-leading-7 st-text-shell-text-soft">{description}</p> : null}
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
    <label className="st-grid st-gap-3 st-rounded-shell-md st-border st-border-white/8 st-bg-white/4 st-p-4">
      <span className="st-text-sm st-font-semibold st-text-shell-text">{label}</span>
      {children}
      <span className="st-text-[0.82rem] st-leading-6 st-text-shell-text-soft">{description}</span>
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
    busy: 'st-border-shell-cool/35 st-bg-[linear-gradient(145deg,rgba(80,119,154,0.16),rgba(243,241,236,0.03))]',
    error: 'st-border-shell-danger/35 st-bg-[linear-gradient(145deg,rgba(212,122,114,0.18),rgba(243,241,236,0.03))]',
    idle: 'st-border-white/8 st-bg-white/4',
    success: 'st-border-shell-success/35 st-bg-[linear-gradient(145deg,rgba(109,177,140,0.18),rgba(243,241,236,0.03))]'
  };

  return (
    <div className={cx('st-grid st-gap-3 st-rounded-shell-md st-border st-p-4', styles[tone])}>
      <div className="st-flex st-flex-wrap st-items-center st-justify-between st-gap-3">
        <span className="st-rounded-full st-bg-white/8 st-px-3 st-py-1.5 st-text-[0.7rem] st-font-bold st-uppercase st-tracking-[0.14em] st-text-shell-text/82">{label}</span>
        <span className="st-text-sm st-font-semibold st-text-shell-text">{title}</span>
      </div>
      <p className="st-text-sm st-leading-7 st-text-shell-text-soft">{message}</p>
    </div>
  );
}

export function ConnectionMetrics({ metrics }: { metrics: ConnectionMetric[] }): JSX.Element | null {
  if (metrics.length === 0) {
    return null;
  }

  return (
    <div className="st-grid st-gap-3 md:st-grid-cols-2">
      {metrics.map(metric => (
        <div key={metric.label} className="st-grid st-gap-1.5 st-rounded-shell-sm st-border st-border-white/8 st-bg-white/4 st-p-3.5">
          <span className="st-text-xs st-text-shell-text/65">{metric.label}</span>
          <strong className="st-break-all st-text-sm st-leading-6 st-text-shell-text">{metric.value}</strong>
        </div>
      ))}
    </div>
  );
}

export function MetricCard({ metric }: { metric: BatchMetric }): JSX.Element {
  const toneClasses: Record<BatchMetric['tone'], string> = {
    danger: 'st-bg-[linear-gradient(145deg,rgba(212,122,114,0.16),rgba(243,241,236,0.03))]',
    neutral: 'st-bg-white/4',
    success: 'st-bg-[linear-gradient(145deg,rgba(109,177,140,0.14),rgba(243,241,236,0.03))]',
    warning: 'st-bg-[linear-gradient(145deg,rgba(216,170,100,0.14),rgba(243,241,236,0.03))]'
  };

  return (
    <div className={cx('st-grid st-min-h-[7rem] st-gap-2 st-rounded-shell-lg st-border st-border-white/8 st-p-4', toneClasses[metric.tone])}>
      <span className="st-text-[0.7rem] st-font-bold st-uppercase st-tracking-[0.14em] st-text-shell-text/62">{metric.label}</span>
      <strong className="st-text-[clamp(1.8rem,2.2vw,2.4rem)] st-leading-none st-text-shell-text">{metric.value}</strong>
      <span className="st-text-sm st-leading-6 st-text-shell-text-soft">{metric.note}</span>
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
    <div className="st-grid st-gap-3 st-rounded-shell-lg st-border st-border-white/8 st-bg-white/4 st-p-5">
      <strong className="st-text-base st-font-semibold st-text-shell-text">{title}</strong>
      <span className="st-text-sm st-leading-7 st-text-shell-text-soft">{description}</span>
    </div>
  );
}

export { cx };
