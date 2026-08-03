"use client";

import { AlertTriangle, Bot, User } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  CATEGORY_LABEL,
  INCIDENT_STATUS_LABEL,
  SEVERITY_LABEL,
  type ClassificationMethod,
  type IncidentCategory,
  type IncidentSeverity,
  type IncidentStatus,
} from "@/lib/types";

/**
 * Incident-specific status vocabulary.
 *
 * Kept separate from the asset badges in `ui.tsx` rather than made generic over both, because the
 * two status sets carry different meanings that happen to share a shape: an asset's "Faulted" and
 * an incident's "Critical" are not points on the same scale, and a shared component would either
 * force one to borrow the other's colour semantics or grow a branch per caller — both worse than
 * two small, honest components.
 */

const SEVERITY_TONE: Record<IncidentSeverity, { dot: string; text: string; bg: string }> = {
  Low: { dot: "bg-ink-faint", text: "text-ink-faint", bg: "bg-raised" },
  Moderate: { dot: "bg-signal", text: "text-signal", bg: "bg-signal-dim" },
  High: { dot: "bg-degraded", text: "text-degraded", bg: "bg-degraded-dim" },
  Critical: { dot: "bg-failed", text: "text-failed", bg: "bg-failed-dim" },
};

export function SeverityBadge({ severity }: { severity: IncidentSeverity }) {
  const tone = SEVERITY_TONE[severity];

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-medium",
        tone.bg,
        tone.text,
      )}
    >
      <span aria-hidden className={cn("size-1.5 rounded-full", tone.dot)} />
      {SEVERITY_LABEL[severity]}
    </span>
  );
}

const STATUS_TONE: Record<IncidentStatus, string> = {
  Reported: "text-signal",
  Triaged: "text-ink-muted",
  InProgress: "text-watch",
  Resolved: "text-nominal",
  Closed: "text-ink-faint",
  Duplicate: "text-ink-faint",
  Rejected: "text-ink-faint",
};

export function IncidentStatusPill({ status }: { status: IncidentStatus }) {
  return (
    <span className={cn("text-[12px] font-medium", STATUS_TONE[status])}>
      {INCIDENT_STATUS_LABEL[status]}
    </span>
  );
}

export function CategoryTag({ category }: { category: IncidentCategory }) {
  return <span className="text-[13px] text-ink-muted">{CATEGORY_LABEL[category]}</span>;
}

/**
 * Names who — or what — produced a classification, and how sure it was.
 *
 * This is a trust signal, not decoration. A dispatcher acting on "Model, 94%" is making a
 * different judgement from acting on "Needs your review", and the two must never look similar
 * enough to blur together at a glance.
 */
export function ClassificationTag({
  method,
  confidence,
}: {
  method: ClassificationMethod;
  confidence: number | null;
}) {
  if (method === "Manual") {
    return (
      <span className="inline-flex items-center gap-1.5 text-[12px] text-ink-muted">
        <User size={12} aria-hidden />
        Confirmed
      </span>
    );
  }

  const pct = confidence !== null ? Math.round(confidence * 100) : null;
  const Icon = method === "Model" ? Bot : AlertTriangle;

  return (
    <span
      className="inline-flex items-center gap-1.5 text-[12px] text-ink-faint"
      title={
        method === "Heuristic"
          ? "Classified by keyword matching, not a language model. Always requires review."
          : "Classified by a language model."
      }
    >
      <Icon size={12} aria-hidden />
      {method === "Model" ? "AI" : "Rule-based"}
      {pct !== null && <span className="tabular">{pct}%</span>}
    </span>
  );
}

/**
 * A banner for reports describing danger to people.
 *
 * Deliberately louder than every other piece of status chrome in this interface — a filled block
 * of colour rather than a dot-plus-word — because this is the one signal that must be impossible
 * to triage past without noticing.
 */
export function SafetyRiskBanner({ className }: { className?: string }) {
  return (
    <div
      role="alert"
      className={cn(
        "flex items-center gap-2 rounded-[--radius-control] bg-failed-dim px-3 py-2 text-[13px] font-medium text-failed",
        className,
      )}
    >
      <AlertTriangle size={15} aria-hidden className="shrink-0" />
      This report describes possible danger to people. Confirm and dispatch urgently.
    </div>
  );
}

/**
 * A banner surfacing a likely duplicate, with the decision left to the dispatcher.
 *
 * Never auto-merged: two similar reports minutes apart are usually the same problem called in
 * twice, but occasionally are not, and losing a real second incident is worse than showing one
 * unnecessary banner.
 */
export function PossibleDuplicateBanner({ reference }: { reference: string }) {
  return (
    <div className="flex items-center gap-2 rounded-[--radius-control] bg-watch-dim px-3 py-2 text-[13px] text-watch">
      <AlertTriangle size={15} aria-hidden className="shrink-0" />
      Possible duplicate of <span className="tabular font-medium">{reference}</span> — review
      before dispatching a second crew.
    </div>
  );
}
