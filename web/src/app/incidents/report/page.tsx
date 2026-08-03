"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, MapPin, Sparkles } from "lucide-react";
import { api, ApiError } from "@/lib/api";
import { useSession } from "@/lib/store";
import type { ReportIncidentResult } from "@/lib/types";
import { Button, Field, Input, Panel } from "@/components/ui";
import {
  CategoryTag,
  ClassificationTag,
  PossibleDuplicateBanner,
  SafetyRiskBanner,
  SeverityBadge,
} from "@/components/incident-ui";

/**
 * Natural-language incident intake — the platform's headline capability.
 *
 * A dispatcher or call-centre operator types what a caller described, in their own words. The
 * classifier proposes a category, severity, location and asset match, and every one of those
 * proposals is shown before anything is created rather than after — the operator sees exactly
 * what the system inferred and from what, which is what makes it possible to trust or correct it.
 */
export default function ReportIncidentPage() {
  const router = useRouter();
  const token = useSession((state) => state.token);

  const [reportText, setReportText] = useState("");
  const [reporterName, setReporterName] = useState("");
  const [reporterContact, setReporterContact] = useState("");
  const [coords, setCoords] = useState<{ lat: number; lon: number } | null>(null);
  const [locating, setLocating] = useState(false);

  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ReportIncidentResult | null>(null);

  function useMyLocation() {
    if (!("geolocation" in navigator)) return;

    setLocating(true);

    navigator.geolocation.getCurrentPosition(
      (position) => {
        setCoords({ lat: position.coords.latitude, lon: position.coords.longitude });
        setLocating(false);
      },
      () => setLocating(false),
      { timeout: 8000 },
    );
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      const outcome = await api.reportIncident(
        {
          reportText,
          latitude: coords?.lat ?? null,
          longitude: coords?.lon ?? null,
          reporterName: reporterName.trim() || null,
          reporterContact: reporterContact.trim() || null,
        },
        token ?? undefined,
      );

      setResult(outcome);
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not submit the report. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  function reportAnother() {
    setReportText("");
    setReporterName("");
    setReporterContact("");
    setCoords(null);
    setResult(null);
    setError(null);
  }

  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-4">
      <div className="resolve">
        <h1 className="text-xl font-semibold tracking-[-0.02em] text-ink">Report an incident</h1>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Describe the problem in plain language, the way the caller described it. Aegis proposes
          a category, severity and location — you confirm before anything is dispatched.
        </p>
      </div>

      {result ? (
        <Panel delay={40} bodyClassName="flex flex-col gap-4 p-5">
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="text-[12px] text-ink-faint">Reference</p>
              <p className="tabular text-lg font-semibold text-ink">{result.reference}</p>
            </div>
            <ClassificationTag method={result.classifiedBy} confidence={result.confidence} />
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <CategoryTag category={result.category} />
            <SeverityBadge severity={result.severity} />
          </div>

          <p className="text-[13px] leading-relaxed text-ink">{result.summary}</p>

          {result.requiresReview && (
            <div className="rounded-[--radius-control] bg-watch-dim px-3 py-2 text-[13px] text-watch">
              A dispatcher must confirm this classification before it is acted on. If this
              describes danger to people, open it in the queue and dispatch immediately.
            </div>
          )}

          {result.matchedAssetCode && (
            <p className="text-[13px] text-ink-muted">
              Linked to asset <span className="tabular font-medium text-ink">{result.matchedAssetCode}</span>
            </p>
          )}

          {result.possibleDuplicateOf && (
            <PossibleDuplicateBanner reference={result.possibleDuplicateOf} />
          )}

          <div className="mt-1 flex gap-2">
            <Button variant="primary" onClick={() => router.push("/incidents")}>
              View in queue
            </Button>
            <Button onClick={reportAnother}>Report another</Button>
          </div>
        </Panel>
      ) : (
        <Panel delay={40} bodyClassName="p-5">
          <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
            <Field
              label="What was reported"
              hint="Write it as the caller said it. There is no format to follow — Aegis reads free text."
            >
              <textarea
                required
                minLength={10}
                maxLength={8000}
                rows={6}
                value={reportText}
                onChange={(event) => setReportText(event.target.value)}
                placeholder="e.g. Water is gushing up through the pavement outside 14 Northgate Road, it's been going for an hour…"
                className="w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] leading-relaxed text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
              />
            </Field>

            <div className="flex items-center justify-between gap-3 rounded-[--radius-control] border border-line bg-raised px-3 py-2">
              <div className="flex items-center gap-2 text-[13px] text-ink-muted">
                <MapPin size={14} aria-hidden />
                {coords
                  ? `${coords.lat.toFixed(5)}, ${coords.lon.toFixed(5)}`
                  : "No location attached"}
              </div>
              <Button type="button" onClick={useMyLocation} loading={locating}>
                {coords ? "Update location" : "Use my location"}
              </Button>
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Reporter name" hint="Optional. Anonymous reports are accepted.">
                <Input
                  value={reporterName}
                  onChange={(event) => setReporterName(event.target.value)}
                  placeholder="Optional"
                />
              </Field>
              <Field label="Contact" hint="Phone or email, for a call back.">
                <Input
                  value={reporterContact}
                  onChange={(event) => setReporterContact(event.target.value)}
                  placeholder="Optional"
                />
              </Field>
            </div>

            {error && (
              <p role="alert" className="rounded-[--radius-control] bg-failed-dim px-3 py-2 text-[13px] text-failed">
                {error}
              </p>
            )}

            <Button
              type="submit"
              variant="primary"
              loading={submitting}
              disabled={reportText.trim().length < 10}
              className="justify-center py-2.5"
            >
              {submitting ? (
                <>
                  <Loader2 size={14} aria-hidden className="animate-spin" />
                  Classifying report
                </>
              ) : (
                <>
                  <Sparkles size={14} aria-hidden />
                  Submit report
                </>
              )}
            </Button>
          </form>
        </Panel>
      )}
    </div>
  );
}
