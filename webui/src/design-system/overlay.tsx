import { useEffect, useRef, useState, type ReactNode } from 'react';
import type { JSX } from 'react';
import { Button, cx } from './components';
import { IconInfo } from './icons';

/**
 * Tooltip 组件
 * 鼠标悬停时显示提示信息
 */

interface TooltipProps {
  content: ReactNode;
  children: ReactNode;
  /** 延迟显示时间（毫秒） */
  delay?: number;
  /** 提示框位置 */
  position?: 'top' | 'bottom';
}

export function Tooltip({ content, children, delay = 200, position = 'top' }: TooltipProps): JSX.Element {
  const [visible, setVisible] = useState(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const showTooltip = () => {
    timeoutRef.current = setTimeout(() => setVisible(true), delay);
  };

  const hideTooltip = () => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }
    setVisible(false);
  };

  const positionClasses = position === 'top'
    ? 'bottom-full left-1/2 -translate-x-1/2 mb-2'
    : 'top-full left-1/2 -translate-x-1/2 mt-2';

  return (
    <div
      className="relative inline-flex"
      onMouseEnter={showTooltip}
      onMouseLeave={hideTooltip}
    >
      {children}
      {visible && (
        <div
          className={cx(
            'absolute z-50 max-w-xs rounded-lg border border-white/10 bg-[#2a2a2a] px-3 py-2 text-xs text-gray-200 shadow-lg',
            positionClasses
          )}
          role="tooltip"
        >
          {content}
        </div>
      )}
    </div>
  );
}

/**
 * InfoTooltip - 带信息图标的提示组件
 * 用于表单字段旁的解释性提示
 */
export function InfoTooltip({ content }: { content: ReactNode }): JSX.Element {
  return (
    <Tooltip content={content}>
      <IconInfo className="h-4 w-4 cursor-help text-gray-500 hover:text-gray-400" />
    </Tooltip>
  );
}

/**
 * ConfirmDialog 组件
 * 确认对话框，用于危险操作前的二次确认
 */

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: ReactNode;
  confirmText?: string;
  cancelText?: string;
  tone?: 'danger' | 'warning' | 'neutral';
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmText = '确认',
  cancelText = '取消',
  tone = 'neutral',
  loading = false,
  onConfirm,
  onCancel
}: ConfirmDialogProps): JSX.Element | null {
  // ESC 键关闭
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && open && !loading) {
        e.preventDefault();
        onCancel();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [loading, open, onCancel]);

  if (!open) return null;

  const toneClasses = {
    danger: 'border-red-500/30 bg-red-500/10',
    warning: 'border-yellow-500/30 bg-yellow-500/10',
    neutral: 'border-white/10 bg-white/5'
  };

  return (
    <div
      className="fixed inset-0 z-[200000] flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
      onClick={e => {
        if (!loading && e.target === e.currentTarget) onCancel();
      }}
    >
      <div
        data-subtitles-tools-confirm-dialog="true"
        className={cx(
          'w-full max-w-md rounded-2xl border p-5 shadow-2xl',
          toneClasses[tone]
        )}
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        aria-busy={loading}
      >
        <h3 id="confirm-dialog-title" className="text-lg font-semibold text-gray-100">
          {title}
        </h3>
        <p className="mt-2 text-sm text-gray-300">{message}</p>
        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="tertiary" disabled={loading} onClick={onCancel}>
            {cancelText}
          </Button>
          <Button
            type="button"
            variant={tone === 'danger' ? 'danger' : 'primary'}
            loading={loading}
            onClick={onConfirm}
          >
            {confirmText}
          </Button>
        </div>
      </div>
    </div>
  );
}
