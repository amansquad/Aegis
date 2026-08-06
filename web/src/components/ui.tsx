"use client";

import { cn } from "@/lib/utils";
import type { AssetCondition, AssetCriticality, AssetStatus } from "@/lib/types";
import { CONDITION_LABEL, STATUS_LABEL } from "@/lib/types";
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from "react";

/* ------------------------------------------------------------------ *
 * Panel
 * ------------------------------------------------------------------ */

/**
 * The one container in this interface.
 *
 * Deliberately singular. A page assembled from same-size cards of icon-plus-heading-plus-text is
 * the lazy scaffold, and it flattens hierarchy: everything looks equally important because
 * everything is in an identical box. Here the panel is a frame for content that carries its own
 * structure, and panels are never nested inside each other.
 */
export function Panel({
  title,
  action,
  children,
  className,
  bodyClassName,
  delay = 0,
}: {
  title?: string;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
  bodyClassName?: string;
  delay?: number;
}) {
  return (
    <section
      className={cn(
        "resolve rounded-[--radius-panel] border border-line bg-surface shadow-[--shadow-panel]",
        className,
      )}
      style={delay ? { animationDelay: `${delay}ms` } : undefined}
    >
      {title && (
        <header className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
          <h2 className="text-[13px] font-semibold tracking-[-0.01em] text-ink">{title}</h2>
          {action}
        </header>
      )}
      <div className={cn(bodyClassName)}>{children}</div>
    </section>
  );
}

/* ------------------------------------------------------------------ *
 * Status vocabulary
 * ------------------------------------------------------------------ */

const CONDITION_TONE: Record<AssetCondition, { dot: string; text: string; bg: string }> = {
  VeryGood: { dot: "bg-nominal", text: "text-nominal", bg: "bg-nominal-dim" },
  Good: { dot: "bg-nominal", text: "text-nominal", bg: "bg-nominal-dim" },
  Fair: { dot: "bg-watch", text: "text-watch", bg: "bg-watch-dim" },
  Poor: { dot: "bg-degraded", text: "text-degraded", bg: "bg-degraded-dim" },
  VeryPoor: { dot: "bg-failed", text: "text-failed", bg: "bg-failed-dim" },
  Unknown: { dot: "bg-ink-faint", text: "text-ink-faint", bg: "bg-raised" },
};

const STATUS_TONE: Record<AssetStatus, { dot: string; text: string }> = {
  Operational: { dot: "bg-nominal", text: "text-ink-muted" },
  UnderMaintenance: { dot: "bg-watch", text: "text-watch" },
  Faulted: { dot: "bg-failed", text: "text-failed" },
  Planned: { dot: "bg-signal", text: "text-signal" },
  Decommissioned: { dot: "bg-ink-faint", text: "text-ink-faint" },
};

/**
 * Status is carried by a dot plus a word, never by colour alone.
 *
 * Around one man in twelve cannot reliably separate the red from the green this interface uses to
 * mean "failed" and "nominal" — and this is a product where that distinction decides what gets a
 * crew sent to it tonight.
 */
export function StatusPill({ status }: { status: AssetStatus }) {
  const tone = STATUS_TONE[status];

  return (
    <span className={cn("inline-flex items-center gap-1.5 text-[12px]", tone.text)}>
      <span aria-hidden className={cn("size-1.5 rounded-full", tone.dot)} />
      {STATUS_LABEL[status]}
    </span>
  );
}

export function ConditionBadge({ condition }: { condition: AssetCondition }) {
  const tone = CONDITION_TONE[condition];

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium",
        tone.bg,
        tone.text,
      )}
    >
      <span aria-hidden className={cn("size-1.5 rounded-full", tone.dot)} />
      {CONDITION_LABEL[condition]}
    </span>
  );
}

const CRITICALITY_TONE: Record<AssetCriticality, string> = {
  Low: "text-ink-faint",
  Medium: "text-ink-muted",
  High: "text-degraded",
  Critical: "text-failed",
};

/**
 * Criticality as a four-step meter.
 *
 * A bar rather than a word because criticality is ordinal and the eye reads relative height far
 * faster than it reads "High" versus "Critical" down a column of two hundred rows.
 */
export function CriticalityMeter({ level }: { level: AssetCriticality }) {
  const filled = { Low: 1, Medium: 2, High: 3, Critical: 4 }[level];

  return (
    <span className="inline-flex items-center gap-2" title={`Criticality: ${level}`}>
      <span aria-hidden className="flex items-end gap-[2px]">
        {[0, 1, 2, 3].map((step) => (
          <span
            key={step}
            className={cn(
              "w-[3px] rounded-[1px] transition-colors",
              step < filled ? CRITICALITY_TONE[level].replace("text-", "bg-") : "bg-line-strong",
            )}
            style={{ height: `${5 + step * 3}px` }}
          />
        ))}
      </span>
      <span className={cn("text-[12px]", CRITICALITY_TONE[level])}>{level}</span>
    </span>
  );
}

/* ------------------------------------------------------------------ *
 * Controls
 * ------------------------------------------------------------------ */

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost";
  loading?: boolean;
};

export function Button({
  variant = "secondary",
  loading,
  className,
  children,
  disabled,
  ...props
}: ButtonProps) {
  return (
    <button
      {...props}
      disabled={disabled || loading}
      // aria-busy rather than swapping the label for a spinner: a screen reader user is told the
      // control is working without the accessible name changing under them mid-action.
      aria-busy={loading || undefined}
      className={cn(
        "inline-flex items-center justify-center gap-2 rounded-[--radius-control] px-3.5 py-2.5",
        "text-[13px] font-medium transition-all duration-150",
        "disabled:cursor-not-allowed disabled:opacity-45",
        variant === "primary" &&
          "bg-signal text-void hover:brightness-110 active:brightness-95 shadow-[--shadow-panel]",
        variant === "secondary" &&
          "border border-line-strong bg-raised text-ink hover:border-ink-faint hover:bg-overlay",
        variant === "ghost" && "text-ink-muted hover:bg-raised hover:text-ink",
        className,
      )}
    >
      {loading && (
        <span
          aria-hidden
          className="size-3.5 animate-spin rounded-full border-[1.5px] border-current border-t-transparent"
        />
      )}
      {children}
    </button>
  );
}

export function Field({
  label,
  hint,
  error,
  children,
}: {
  label: string;
  hint?: string;
  error?: string;
  children: ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-[12px] font-medium text-ink-muted">{label}</span>
      {children}
      {/* The error replaces the hint rather than stacking beneath it, so the control never
          shifts vertically as the user types and the message never competes with the hint. */}
      {error ? (
        <span role="alert" className="mt-1.5 block text-[12px] text-failed">
          {error}
        </span>
      ) : hint ? (
        <span className="mt-1.5 block text-[12px] text-ink-faint">{hint}</span>
      ) : null}
    </label>
  );
}

export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      className={cn(
        "w-full rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2",
        "text-[13px] text-ink placeholder:text-ink-faint",
        "transition-colors focus:border-signal focus:outline-none focus-visible:outline-none",
        "disabled:opacity-50",
        className,
      )}
    />
  );
}

export function Select({ className, children, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      {...props}
      className={cn(
        "w-full appearance-none rounded-[--radius-control] border border-line-strong bg-raised",
        "bg-[length:14px] bg-[right_0.6rem_center] bg-no-repeat py-2 pl-3 pr-8",
        "text-[13px] text-ink transition-colors focus:border-signal focus:outline-none",
        className,
      )}
      style={{
        backgroundImage:
          "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16' fill='none' stroke='%2371809a' stroke-width='1.5'%3E%3Cpath d='M4 6l4 4 4-4'/%3E%3C/svg%3E\")",
      }}
    >
      {children}
    </select>
  );
}

/* ------------------------------------------------------------------ *
 * States
 * ------------------------------------------------------------------ */

/**
 * A skeleton shaped like the content it replaces.
 *
 * Sized to the real row so the layout does not jump when data lands. A spinner in the middle of an
 * empty panel tells the user only that something is happening; this tells them what is coming.
 */
export function RowSkeleton({ count = 8 }: { count?: number }) {
  return (
    <div className="divide-y divide-line" aria-hidden>
      {Array.from({ length: count }, (_, index) => (
        <div key={index} className="flex items-center gap-4 px-4 py-3">
          <div className="h-3.5 w-28 animate-pulse rounded bg-raised" />
          <div className="h-3.5 w-48 animate-pulse rounded bg-raised" />
          <div className="ml-auto h-3.5 w-20 animate-pulse rounded bg-raised" />
        </div>
      ))}
    </div>
  );
}

/**
 * An empty state that says what to do next.
 *
 * "No results" is a dead end. Every empty state here names the reason and offers the action that
 * resolves it, because an empty list is most often a filter the user forgot they set.
 */
export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-6 py-16 text-center">
      <p className="text-[14px] font-medium text-ink">{title}</p>
      <p className="max-w-sm text-[13px] leading-relaxed text-ink-muted">{description}</p>
      {action && <div className="mt-3">{action}</div>}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-6 py-16 text-center">
      <p className="text-[14px] font-medium text-failed">Could not load this</p>
      <p className="max-w-sm text-[13px] leading-relaxed text-ink-muted">{message}</p>
      {onRetry && (
        <Button className="mt-3" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  );
}
