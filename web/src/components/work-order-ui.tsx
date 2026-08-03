"use client";

import { cn } from "@/lib/utils";
import {
  WORK_ORDER_PRIORITY_LABEL,
  WORK_ORDER_STATUS_LABEL,
  type WorkOrderPriority,
  type WorkOrderStatus,
} from "@/lib/types";

/**
 * Work-order-specific status vocabulary, kept separate from the incident and asset badges for the
 * same reason those two are kept apart from each other: the tones here mean something particular
 * to dispatch, not a point on a shared scale.
 */

const PRIORITY_TONE: Record<WorkOrderPriority, { dot: string; text: string; bg: string }> = {
  Low: { dot: "bg-ink-faint", text: "text-ink-faint", bg: "bg-raised" },
  Medium: { dot: "bg-signal", text: "text-signal", bg: "bg-signal-dim" },
  High: { dot: "bg-degraded", text: "text-degraded", bg: "bg-degraded-dim" },
  Critical: { dot: "bg-failed", text: "text-failed", bg: "bg-failed-dim" },
};

export function PriorityBadge({ priority }: { priority: WorkOrderPriority }) {
  const tone = PRIORITY_TONE[priority];

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium",
        tone.bg,
        tone.text,
      )}
    >
      <span aria-hidden className={cn("size-1.5 rounded-full", tone.dot)} />
      {WORK_ORDER_PRIORITY_LABEL[priority]}
    </span>
  );
}

const STATUS_TONE: Record<WorkOrderStatus, string> = {
  Draft: "text-signal",
  Scheduled: "text-ink-muted",
  InProgress: "text-watch",
  Completed: "text-nominal",
  Cancelled: "text-ink-faint",
};

export function WorkOrderStatusPill({ status }: { status: WorkOrderStatus }) {
  return (
    <span className={cn("text-[12px] font-medium", STATUS_TONE[status])}>
      {WORK_ORDER_STATUS_LABEL[status]}
    </span>
  );
}
