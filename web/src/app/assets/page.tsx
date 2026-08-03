"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import dynamic from "next/dynamic";
import { useMemo, useState } from "react";
import { List, MapIcon, Search } from "lucide-react";
import { api } from "@/lib/api";
import { useSession } from "@/lib/store";
import {
  CONDITION_LABEL,
  STATUS_LABEL,
  TYPE_LABEL,
  type AssetCondition,
  type AssetFilters,
  type AssetStatus,
  type AssetType,
} from "@/lib/types";
import { formatCoordinate, relativeAge } from "@/lib/utils";
import {
  Button,
  ConditionBadge,
  CriticalityMeter,
  EmptyState,
  ErrorState,
  Panel,
  RowSkeleton,
  Select,
  StatusPill,
} from "@/components/ui";

// Leaflet touches `window` at module scope, so it cannot be server-rendered. The placeholder is
// sized to the map so switching views does not collapse the layout for a frame.
const AssetMap = dynamic(() => import("@/components/asset-map"), {
  ssr: false,
  loading: () => (
    <div className="flex h-full items-center justify-center bg-raised text-[13px] text-ink-faint">
      Loading map…
    </div>
  ),
});

const PAGE_SIZE = 25;

export default function AssetsPage() {
  const token = useSession((state) => state.token);

  const [view, setView] = useState<"table" | "map">("table");
  const [search, setSearch] = useState("");
  const [type, setType] = useState<AssetType | "">("");
  const [status, setStatus] = useState<AssetStatus | "">("");
  const [condition, setCondition] = useState<AssetCondition | "">("");
  const [page, setPage] = useState(1);

  const filters: AssetFilters = useMemo(
    () => ({
      searchTerm: search.trim() || undefined,
      type: type || undefined,
      status: status || undefined,
      condition: condition || undefined,
      // The map needs the whole visible set at once; a paged map would show a quarter of the
      // estate and give no clue that the rest exists.
      page: view === "map" ? 1 : page,
      pageSize: view === "map" ? 100 : PAGE_SIZE,
      sortBy: "createdOnUtc",
      sortDirection: "Descending",
    }),
    [search, type, status, condition, page, view],
  );

  const { data, isPending, isFetching, error, refetch } = useQuery({
    queryKey: ["assets", filters],
    queryFn: () => api.listAssets(filters, token ?? undefined),
    // Keeps the previous page on screen while the next one loads, so paging does not flash a
    // skeleton over content the user was already reading.
    placeholderData: keepPreviousData,
  });

  const items = data?.items ?? [];
  const filtered = Boolean(search || type || status || condition);

  function reset() {
    setSearch("");
    setType("");
    setStatus("");
    setCondition("");
    setPage(1);
  }

  return (
    <div className="mx-auto flex max-w-[1400px] flex-col gap-4">
      <div className="resolve flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-[-0.02em] text-ink">Asset registry</h1>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            {data ? `${data.totalCount.toLocaleString()} assets` : "Loading"}
            {filtered && data ? " matching your filters" : ""}
          </p>
        </div>

        <div
          role="tablist"
          aria-label="View"
          className="flex rounded-[--radius-control] border border-line bg-surface p-0.5"
        >
          {(
            [
              ["table", "Table", List],
              ["map", "Map", MapIcon],
            ] as const
          ).map(([value, label, Icon]) => (
            <button
              key={value}
              role="tab"
              aria-selected={view === value}
              onClick={() => setView(value)}
              className={`flex items-center gap-1.5 rounded-[5px] px-3 py-1.5 text-[12px] font-medium transition-colors ${
                view === value ? "bg-raised text-ink" : "text-ink-muted hover:text-ink"
              }`}
            >
              <Icon size={14} aria-hidden />
              {label}
            </button>
          ))}
        </div>
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
            placeholder="Search by name or asset code"
            aria-label="Search assets"
            className="w-full rounded-[--radius-control] border border-line-strong bg-raised py-2 pl-9 pr-3 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
          />
        </div>

        <FilterSelect
          label="Type"
          value={type}
          onChange={(value) => {
            setType(value as AssetType | "");
            setPage(1);
          }}
          options={Object.entries(TYPE_LABEL)}
        />
        <FilterSelect
          label="Status"
          value={status}
          onChange={(value) => {
            setStatus(value as AssetStatus | "");
            setPage(1);
          }}
          options={Object.entries(STATUS_LABEL)}
        />
        <FilterSelect
          label="Condition"
          value={condition}
          onChange={(value) => {
            setCondition(value as AssetCondition | "");
            setPage(1);
          }}
          options={Object.entries(CONDITION_LABEL)}
        />

        {filtered && (
          <Button variant="ghost" onClick={reset}>
            Clear
          </Button>
        )}
      </Panel>

      {error ? (
        <ErrorState message={(error as Error).message} onRetry={() => refetch()} />
      ) : view === "map" ? (
        <Panel delay={80} className="overflow-hidden" bodyClassName="h-[calc(100dvh-320px)] min-h-[420px]">
          {isPending ? (
            <div className="flex h-full items-center justify-center text-[13px] text-ink-faint">
              Loading map…
            </div>
          ) : (
            <AssetMap assets={items} />
          )}
        </Panel>
      ) : (
        <Panel delay={80} className="overflow-hidden">
          {isPending ? (
            <RowSkeleton count={10} />
          ) : items.length === 0 ? (
            <EmptyState
              title={filtered ? "No assets match these filters" : "The registry is empty"}
              description={
                filtered
                  ? "Every asset was excluded by at least one filter. Clearing them will show the full estate."
                  : "Register your first asset to start tracking condition and maintenance."
              }
              action={filtered ? <Button onClick={reset}>Clear filters</Button> : undefined}
            />
          ) : (
            <>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[860px] border-collapse text-left">
                  <thead>
                    <tr className="border-b border-line text-[11px] uppercase tracking-wider text-ink-faint">
                      <th scope="col" className="px-4 py-2.5 font-medium">Code</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Asset</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Type</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Status</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Condition</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Criticality</th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">Inspected</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {items.map((asset) => (
                      <tr key={asset.id} className="transition-colors hover:bg-raised">
                        <td className="tabular whitespace-nowrap px-4 py-3 text-[12px] text-ink-muted">
                          {asset.code}
                        </td>
                        <td className="px-4 py-3">
                          <span className="block text-[13px] text-ink">{asset.name}</span>
                          <span className="tabular block text-[11px] text-ink-faint">
                            {formatCoordinate(asset.latitude, asset.longitude)}
                          </span>
                        </td>
                        <td className="whitespace-nowrap px-4 py-3 text-[13px] text-ink-muted">
                          {TYPE_LABEL[asset.type]}
                        </td>
                        <td className="whitespace-nowrap px-4 py-3">
                          <StatusPill status={asset.status} />
                        </td>
                        <td className="whitespace-nowrap px-4 py-3">
                          <ConditionBadge condition={asset.condition} />
                        </td>
                        <td className="whitespace-nowrap px-4 py-3">
                          <CriticalityMeter level={asset.criticality} />
                        </td>
                        <td className="tabular whitespace-nowrap px-4 py-3 text-right text-[12px] text-ink-faint">
                          {relativeAge(asset.lastInspectedOnUtc)}
                        </td>
                      </tr>
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
    </div>
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
    <label className="min-w-[136px]">
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
