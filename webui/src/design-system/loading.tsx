import type { HTMLAttributes, ReactNode } from 'react';
import type { JSX } from 'react';
import { cx } from './components';
import { IconLoader } from './icons';

/**
 * Loading 相关组件
 * - Spinner: 旋转加载指示器
 * - Skeleton: 骨架屏占位
 */

interface SpinnerProps {
  className?: string;
  size?: 'sm' | 'md' | 'lg';
}

const spinnerSizes = {
  sm: 14,
  md: 20,
  lg: 28
};

export function Spinner({ className, size = 'md' }: SpinnerProps): JSX.Element {
  return (
    <IconLoader
      className={cx('animate-spin', className)}
      size={spinnerSizes[size]}
    />
  );
}

interface SkeletonProps extends HTMLAttributes<HTMLDivElement> {
  /** 骨架屏宽度，支持 Tailwind 类名如 w-32 */
  width?: string;
  /** 骨架屏高度，支持 Tailwind 类名如 h-4 */
  height?: string;
}

export function Skeleton({ className, width, height, style, ...props }: SkeletonProps): JSX.Element {
  return (
    <div
      className={cx(
        'animate-pulse rounded bg-white/10',
        width,
        height,
        className
      )}
      style={style}
      {...props}
    />
  );
}

/**
 * 用于包装异步加载内容的骨架屏容器
 */
export function SkeletonGroup({ children }: { children: ReactNode }): JSX.Element {
  return <div className="flex flex-col gap-3">{children}</div>;
}
