"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { api, ApiError } from "@/lib/api";
import { useSession } from "@/lib/store";
import {
  CATEGORY_LABEL,
  SEVERITY_LABEL,
  type IncidentCategory,
  type IncidentListItem,
  type IncidentSeverity,
} from "@/lib/types";
import { formatCoordinate, relativeAge } from "@/lib/utils";
import { Button, Field, Select } from "@/components/ui";
import {
  CategoryTag,
  ClassificationTag,
  IncidentStatusPill,
  SafetyRiskBanner,
  SeverityBadge,
} from "@/components/incident-ui";

const OPEN_STATUSES = new Set(["Reported", "Triaged", "InProgress"]);

/**
 * Detail and triage actions for one incident, as a side drawer rather than a route.
 *
 * A drawer keeps the queue itself on screen behind it — a dispatcher triaging ten reports in a
 * row does not lose their place in the list, filters and scroll position, between each one. A
 * full navigation would make the ninth triage in a session cost the same context-switch as the
 * first.
 *
 * Shows only what the list endpoint actually returns. There is currently no way to fetch a single
 * incident's full original report text from the API — the list projection deliberately omits it,
 * and there is no get-by-id endpoint — so this does not invent a field the backend cannot supply.
 */
export function IncidentDetailDrawer({
  incident,
  onClose,
  onChanged,
}: {
  incident: IncidentListItem;
  onClose: () => void;
  onChanged: () => void;
}) {
  const token = useSession((state) => state.token);

  const [category, setCategory] = useState<IncidentCategory>(incident.category);
  const [severity, setSeverity] = useState<IncidentSeverity>(incident.severity);
  const [summary, setSummary] = useState(incident.summary);
  const [notes, setNotes] = useState("");

  const [busy, setBusy] = useState<"triage" | "resolve" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const isOpen = OPEN_STATUSES.has(incident.status);

  async function handleTriage() {
    setError(null);
    setBusy("triage");

    try {
      await api.triageIncident(
        incident.id,
        { category, severity, summary: summary.trim() || null, assetId: incident.assetId },
        token ?? undefined,
      );

      onChanged();
      onClose();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not save the triage decision.");
    } finally {
      setBusy(null);
    }
  }

  async function handleResolve() {
    setError(null);
    setBusy("resolve");

    try {
      await api.resolveIncident(incident.id, notes.trim() || null, token ?? undefined);
      onChanged();
      onClose();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not resolve this incident.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="fixed inset-0 z-40 flex justify-end">
      <button
        aria-label="Close"
        onClick={onClose}
        className="absolute inset-0 bg-void/70"
        tabIndex={-1}
      />

      <aside
        role="dialog"
        aria-label={`Incident ${incident.reference}`}
        className="relative flex h-full w-full max-w-md flex-col overflow-y-auto border-l border-line bg-surface shadow-[--shadow-pop]"
      >
        <header className="flex items-start justify-between gap-3 border-b border-line px-5 py-4">
          <div>
            <p className="tabular text-[12px] text-ink-faint">{incident.reference}</p>
            <div className="mt-1 flex items-center gap-2">
              <IncidentStatusPill status={incident.status} />
            </div>
          </div>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-[--radius-control] p-1.5 text-ink-muted hover:bg-raised hover:text-ink"
          >
            <X size={16} aria-hidden />
          </button>
        </header>

        <div className="flex flex-col gap-4 px-5 py-4">
          {incident.publicSafetyRisk && <SafetyRiskBanner />}

          <div>
            <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">Summary</p>
            <p className="mt-1 text-[13px] leading-relaxed text-ink">{incident.summary}</p>
          </div>

          <div className="flex flex-wrap items-center gap-4 text-[12px] text-ink-muted">
            <span className="flex items-center gap-1.5">
              <CategoryTag category={incident.category} />
            </span>
            <SeverityBadge severity={incident.severity} />
            <ClassificationTag method={incident.classifiedBy} confidence={incident.confidence} />
          </div>

          <dl className="grid grid-cols-2 gap-3 text-[12px]">
            <div>
              <dt className="text-ink-faint">Reported</dt>
              <dd className="mt-0.5 text-ink">{relativeAge(incident.reportedOnUtc)}</dd>
            </div>
            <div>
              <dt className="text-ink-faint">Location</dt>
              <dd className="mt-0.5 text-ink">
                {incident.locationHint ?? formatCoordinate(incident.latitude, incident.longitude)}
              </dd>
            </div>
            {incident.assetId && (
              <div className="col-span-2">
                <dt className="text-ink-faint">Linked asset</dt>
                <dd className="tabular mt-0.5 text-ink">{incident.assetId}</dd>
              </div>
            )}
            {incident.resolvedOnUtc && (
              <div className="col-span-2">
                <dt className="text-ink-faint">Resolved</dt>
                <dd className="mt-0.5 text-ink">{relativeAge(incident.resolvedOnUtc)}</dd>
              </div>
            )}
          </dl>

          {isOpen && (
            <>
              <hr className="border-line" />

              <div>
                <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">
                  Confirm classification
                </p>
                <p className="mt-1 text-[12px] text-ink-faint">
                  What the system proposed is shown pre-filled. Correct it if it is wrong — the
                  original proposal is kept on record either way.
                </p>

                <div className="mt-3 flex flex-col gap-3">
                  <Field label="Category">
                    <Select
                      value={category}
                      onChange={(event) => setCategory(event.target.value as IncidentCategory)}
                    >
                      {Object.entries(CATEGORY_LABEL).map(([key, label]) => (
                        <option key={key} value={key}>
                          {label}
                        </option>
                      ))}
                    </Select>
                  </Field>

                  <Field label="Severity">
                    <Select
                      value={severity}
                      onChange={(event) => setSeverity(event.target.value as IncidentSeverity)}
                    >
                      {Object.entries(SEVERITY_LABEL).map(([key, label]) => (
                        <option key={key} value={key}>
                          {label}
                        </option>
                      ))}
                    </Select>
                  </Field>

                  <Field label="Summary">
                    <textarea
                      value={summary}
                      onChange={(event) => setSummary(event.target.value)}
                      rows={3}
                      maxLength={500}
                      className="w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] text-ink focus:border-signal focus:outline-none"
                    />
                  </Field>
                </div>

                <Button
                  variant="primary"
                  className="mt-3 w-full justify-center"
                  loading={busy === "triage"}
                  onClick={handleTriage}
                >
                  Confirm triage
                </Button>
              </div>

              <hr className="border-line" />

              <div>
                <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">
                  Resolve
                </p>

                <Field label="What was done" hint="Optional.">
                  <textarea
                    value={notes}
                    onChange={(event) => setNotes(event.target.value)}
                    rows={2}
                    maxLength={2000}
                    placeholder="e.g. Clamp fitted, main repressurised"
                    className="mt-1.5 w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
                  />
                </Field>

                <Button
                  className="mt-3 w-full justify-center"
                  loading={busy === "resolve"}
                  onClick={handleResolve}
                >
                  Mark resolved
                </Button>
              </div>
            </>
          )}

          {error && (
            <p role="alert" className="rounded-[--radius-control] bg-failed-dim px-3 py-2 text-[13px] text-failed">
              {error}
            </p>
          )}
        </div>
      </aside>
    </div>
  );
}
