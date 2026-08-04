"use client";

import { cn } from "@/lib/utils";

/**
 * A plan's status is a smaller vocabulary than an incident's or a work order's: active or not,
 * and due or not. Two independent booleans rather than a combined enum, because "inactive and
 * due" is a real, meaningful state — a plan someone paused while it happened to be overdue — and
 * collapsing the two into one status field would have to either hide that or invent a fifth label
 * for it.
 */

export function DueBadge({ isDue, isActive }: { isDue: boolean; isActive: boolean }) {
  if (!isActive) {
    return (
      <span className="inline-flex items-center gap-1.5 text-[12px] text-ink-faint">
        <span aria-hidden className="size-1.5 rounded-full bg-ink-faint" />
        Inactive
      </span>
    );
  }

  if (isDue) {
    return (
      <span
        className={cn(
          "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium",
          "bg-degraded-dim text-degraded",
        )}
      >
        <span aria-hidden className="size-1.5 rounded-full bg-degraded" />
        Due
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-1.5 text-[12px] text-ink-muted">
      <span aria-hidden className="size-1.5 rounded-full bg-nominal" />
      Scheduled
    </span>
  );
}
