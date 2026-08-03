"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo, useState } from "react";
import { Plus, Search, X } from "lucide-react";
import { api } from "@/lib/api";
import { useSession } from "@/lib/store";
import {
  CATEGORY_LABEL,
  INCIDENT_STATUS_LABEL,
  SEVERITY_LABEL,
  type IncidentCategory,
  type IncidentFilters,
  type IncidentListItem,
  type IncidentSeverity,
  type IncidentStatus,
} from "@/lib/types";
import { relativeAge } from "@/lib/utils";
import { Button, EmptyState, ErrorState, Panel, RowSkeleton, Select } from "@/components/ui";
import {
  CategoryTag,
  ClassificationTag,
  IncidentStatusPill,
  SeverityBadge,
} from "@/components/incident-ui";
import { IncidentDetailDrawer } from "@/components/incident-detail-drawer";

const PAGE_SIZE = 25;

type QuickFilter = "all" | "awaitingTriage" | "open" | "safetyRisk";

export default function IncidentsPage() {
  const token = useSession((state) => state.token);
  const queryClient = useQueryClient();

  const [quickFilter, setQuickFilter] = useState<QuickFilter>("awaitingTriage");
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState<IncidentCategory | "">("");
  const [severity, setSeverity] = useState<IncidentSeverity | "">("");
  const [status, setStatus] = useState<IncidentStatus | "">("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const filters: IncidentFilters = useMemo(
    () => ({
      searchTerm: search.trim() || undefined,
      category: category || undefined,
      severity: severity || undefined,
      status: status || undefined,
      awaitingTriageOnly: quickFilter === "awaitingTriage" || undefined,
      openOnly: quickFilter === "open" || undefined,
      safetyRiskOnly: quickFilter === "safetyRisk" || undefined,
      page,
      pageSize: PAGE_SIZE,
      sortBy: "reportedOnUtc",
      sortDirection: "Descending",
    }),
    [search, category, severity, status, quickFilter, page],
  );

  const { data, isPending, isFetching, error, refetch } = useQuery({
    queryKey: ["incidents", filters],
    queryFn: () => api.listIncidents(filters, token ?? undefined),
    placeholderData: keepPreviousData,
  });

  const items = data?.items ?? [];
  const filtered = Boolean(search || category || severity || status);
  const selected = items.find((i) => i.id === selectedId) ?? null;

  function reset() {
    setSearch("");
    setCategory("");
    setSeverity("");
    setStatus("");
    setPage(1);
  }

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["incidents"] });
  }

  return (
    <div className="mx-auto flex max-w-[1400px] flex-col gap-4">
      <div className="resolve flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-[-0.02em] text-ink">Incidents</h1>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            {data ? `${data.totalCount.toLocaleString()} incidents` : "Loading"}
            {filtered && data ? " matching your filters" : ""}
          </p>
        </div>

        <Link href="/incidents/report">
          <Button variant="primary">
            <Plus size={14} aria-hidden />
            Report incident
          </Button>
        </Link>
      </div>

      <div
        role="tablist"
        aria-label="Quick filter"
        className="resolve flex flex-wrap gap-2"
        style={{ animationDelay: "20ms" }}
      >
        {(
          [
            ["awaitingTriage", "Awaiting triage"],
            ["open", "Open"],
            ["safetyRisk", "Safety risk"],
            ["all", "All"],
          ] as const
        ).map(([value, label]) => (
          <button
            key={value}
            role="tab"
            aria-selected={quickFilter === value}
            onClick={() => {
              setQuickFilter(value);
              setPage(1);
            }}
            className={`rounded-full border px-3 py-1.5 text-[12px] font-medium transition-colors ${
              quickFilter === value
                ? "border-signal bg-signal-dim text-signal"
                : "border-line bg-surface text-ink-muted hover:border-line-strong hover:text-ink"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      <Panel delay={40} bodyClassName="flex flex-wrap items-end gap-3 p-3">
        <div className="relative min-w-[220px] flex-1">
          <Search
            size={14}
            aria-hidden
            className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-faint"
          />
          <input
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
            placeholder="Search by reference or summary"
            aria-label="Search incidents"
            className="w-full rounded-[--radius-control] border border-line-strong bg-raised py-2 pl-9 pr-3 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
          />
        </div>

        <FilterSelect
          label="Category"
          value={category}
          onChange={(value) => {
            setCategory(value as IncidentCategory | "");
            setPage(1);
          }}
          options={Object.entries(CATEGORY_LABEL)}
        />
        <FilterSelect
          label="Severity"
          value={severity}
          onChange={(value) => {
            setSeverity(value as IncidentSeverity | "");
            setPage(1);
          }}
          options={Object.entries(SEVERITY_LABEL)}
        />
        <FilterSelect
          label="Status"
          value={status}
          onChange={(value) => {
            setStatus(value as IncidentStatus | "");
            setPage(1);
          }}
          options={Object.entries(INCIDENT_STATUS_LABEL)}
        />

        {filtered && (
          <Button variant="ghost" onClick={reset}>
            <X size={13} aria-hidden />
            Clear
          </Button>
        )}
      </Panel>

      {error ? (
        <ErrorState message={(error as Error).message} onRetry={() => refetch()} />
      ) : (
        <Panel delay={80} className="overflow-hidden">
          {isPending ? (
            <RowSkeleton count={10} />
          ) : items.length === 0 ? (
            <EmptyState
              title={filtered || quickFilter !== "all" ? "No incidents match this view" : "No incidents reported"}
              description={
                filtered || quickFilter !== "all"
                  ? "Nothing was excluded by a mistake here — try 'All' or clear the filters."
                  : "Reports submitted through intake will appear here for triage."
              }
              action={
                filtered ? (
                  <Button onClick={reset}>Clear filters</Button>
                ) : (
                  <Link href="/incidents/report">
                    <Button variant="primary">Report an incident</Button>
                  </Link>
                )
              }
            />
          ) : (
            <>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[900px] border-collapse text-left">
                  <thead>
                    <tr className="border-b border-line text-[11px] uppercase tracking-wider text-ink-faint">
                      <th scope="col" className="px-4 py-2.5 font-medium">Reference</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Report</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Category</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Severity</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Status</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Classified</th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">Reported</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {items.map((incident) => (
                      <IncidentRow
                        key={incident.id}
                        incident={incident}
                        onOpen={() => setSelectedId(incident.id)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>

              {data && data.totalPages > 1 && (
                <div className="flex items-center justify-between gap-3 border-t border-line px-4 py-3">
                  <p className="tabular text-[12px] text-ink-faint">
                    {(data.page - 1) * data.pageSize + 1}–
                    {Math.min(data.page * data.pageSize, data.totalCount)} of{" "}
                    {data.totalCount.toLocaleString()}
                    {isFetching && <span className="ml-2 text-ink-faint">updating…</span>}
                  </p>
                  <div className="flex gap-2">
                    <Button
                      onClick={() => setPage((current) => Math.max(current - 1, 1))}
                      disabled={!data.hasPreviousPage}
                    >
                      Previous
                    </Button>
                    <Button
                      onClick={() => setPage((current) => current + 1)}
                      disabled={!data.hasNextPage}
                    >
                      Next
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </Panel>
      )}

      {selected && (
        <IncidentDetailDrawer
          incident={selected}
          onClose={() => setSelectedId(null)}
          onChanged={invalidate}
        />
      )}
    </div>
  );
}

function IncidentRow({
  incident,
  onOpen,
}: {
  incident: IncidentListItem;
  onOpen: () => void;
}) {
  return (
    <tr
      onClick={onOpen}
      className="cursor-pointer transition-colors hover:bg-raised"
    >
      <td className="tabular whitespace-nowrap px-4 py-3 text-[12px] text-ink-muted">
        {incident.reference}
      </td>
      <td className="px-4 py-3">
        <span className="line-clamp-1 max-w-md text-[13px] text-ink">{incident.summary}</span>
        {incident.locationHint && (
          <span className="mt-0.5 block text-[11px] text-ink-faint">{incident.locationHint}</span>
        )}
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <CategoryTag category={incident.category} />
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <SeverityBadge severity={incident.severity} />
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <IncidentStatusPill status={incident.status} />
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <ClassificationTag method={incident.classifiedBy} confidence={incident.confidence} />
      </td>
      <td className="tabular whitespace-nowrap px-4 py-3 text-right text-[12px] text-ink-faint">
        {relativeAge(incident.reportedOnUtc)}
      </td>
    </tr>
  );
}

function FilterSelect({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: [string, string][];
}) {
  return (
    <label className="min-w-[140px]">
      <span className="mb-1.5 block text-[11px] font-medium text-ink-faint">{label}</span>
      <Select value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">Any</option>
        {options.map(([key, text]) => (
          <option key={key} value={key}>
            {text}
          </option>
        ))}
      </Select>
    </label>
  );
}
