"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Plus, Search, X } from "lucide-react";
import { api, ApiError } from "@/lib/api";
import { useSession } from "@/lib/store";
import type {
  Asset,
  CreateMaintenancePlanInput,
  MaintenancePlanFilters,
  MaintenancePlanListItem,
  WorkOrderPriority,
} from "@/lib/types";
import { WORK_ORDER_PRIORITY_LABEL } from "@/lib/types";
import { Button, EmptyState, ErrorState, Field, Input, Panel, RowSkeleton, Select } from "@/components/ui";
import { DueBadge } from "@/components/maintenance-ui";
import { useDialogA11y } from "@/lib/use-dialog-a11y";

const PAGE_SIZE = 25;

type QuickFilter = "due" | "active" | "all";

/**
 * Says where a due date sits relative to now. Self-contained rather than composed from
 * `relativeAge`, which is written for the past and reads "just now" for any future timestamp — a
 * plan due in five days is not "due just now".
 */
function dueDateLabel(nextDueOnUtc: string): string {
  const days = Math.round((new Date(nextDueOnUtc).getTime() - Date.now()) / 86_400_000);

  if (days > 1) return `Due in ${days}d`;
  if (days === 1) return "Due tomorrow";
  if (days === 0) return "Due today";
  if (days === -1) return "Overdue by 1d";

  return `Overdue by ${-days}d`;
}

export default function MaintenancePage() {
  const token = useSession((state) => state.token);
  const hasPermission = useSession((state) => state.hasPermission);
  const queryClient = useQueryClient();

  const [quickFilter, setQuickFilter] = useState<QuickFilter>("due");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [generatingFor, setGeneratingFor] = useState<MaintenancePlanListItem | null>(null);

  const filters: MaintenancePlanFilters = useMemo(
    () => ({
      searchTerm: search.trim() || undefined,
      dueOnly: quickFilter === "due" || undefined,
      activeOnly: quickFilter === "active" || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    [search, quickFilter, page],
  );

  const { data, isPending, isFetching, error, refetch } = useQuery({
    queryKey: ["maintenance-plans", filters],
    queryFn: () => api.listMaintenancePlans(filters, token ?? undefined),
    placeholderData: keepPreviousData,
  });

  const items = data?.items ?? [];
  const filtered = Boolean(search);

  function reset() {
    setSearch("");
    setPage(1);
  }

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["maintenance-plans"] });
    queryClient.invalidateQueries({ queryKey: ["work-orders"] });
  }

  return (
    <div className="mx-auto flex max-w-[1400px] flex-col gap-4">
      <div className="resolve flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-[-0.02em] text-ink">Maintenance</h1>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            {data ? `${data.totalCount.toLocaleString()} plans` : "Loading"}
            {filtered && data ? " matching your search" : ""}
          </p>
        </div>

        {hasPermission("maintenance.schedule") && (
          <Button variant="primary" onClick={() => setCreating(true)}>
            <Plus size={14} aria-hidden />
            New plan
          </Button>
        )}
      </div>

      <div
        role="tablist"
        aria-label="Quick filter"
        className="resolve flex flex-wrap gap-2"
        style={{ animationDelay: "20ms" }}
      >
        {(
          [
            ["due", "Due"],
            ["active", "Active"],
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
            placeholder="Search by reference or title"
            aria-label="Search maintenance plans"
            className="w-full rounded-[--radius-control] border border-line-strong bg-raised py-2 pl-9 pr-3 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
          />
        </div>

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
              title={filtered || quickFilter !== "all" ? "No plans match this view" : "No maintenance plans yet"}
              description={
                filtered || quickFilter !== "all"
                  ? "Nothing was excluded by a mistake here — try 'All' or clear the search."
                  : "A recurring schedule for an asset will appear here once one exists."
              }
              action={
                filtered ? (
                  <Button onClick={reset}>Clear search</Button>
                ) : hasPermission("maintenance.schedule") ? (
                  <Button variant="primary" onClick={() => setCreating(true)}>
                    Create a plan
                  </Button>
                ) : undefined
              }
            />
          ) : (
            <>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[900px] border-collapse text-left">
                  <thead>
                    <tr className="border-b border-line text-[11px] uppercase tracking-wider text-ink-faint">
                      <th scope="col" className="px-4 py-2.5 font-medium">Reference</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Title</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Frequency</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Status</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Next due</th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {items.map((plan) => (
                      <PlanRow
                        key={plan.id}
                        plan={plan}
                        canSchedule={hasPermission("maintenance.schedule")}
                        onGenerate={() => setGeneratingFor(plan)}
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

      {creating && (
        <CreatePlanModal
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false);
            invalidate();
          }}
        />
      )}

      {generatingFor && (
        <GenerateWorkOrderModal
          plan={generatingFor}
          onClose={() => setGeneratingFor(null)}
          onGenerated={() => {
            setGeneratingFor(null);
            invalidate();
          }}
        />
      )}
    </div>
  );
}

function PlanRow({
  plan,
  canSchedule,
  onGenerate,
}: {
  plan: MaintenancePlanListItem;
  canSchedule: boolean;
  onGenerate: () => void;
}) {
  return (
    <tr className="transition-colors hover:bg-raised">
      <td className="tabular whitespace-nowrap px-4 py-3 text-[12px] text-ink-muted">
        {plan.reference}
      </td>
      <td className="px-4 py-3">
        <span className="line-clamp-1 max-w-md text-[13px] text-ink">{plan.title}</span>
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-[13px] text-ink-muted">
        Every {plan.frequencyDays}d
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <DueBadge isDue={plan.isDue} isActive={plan.isActive} />
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-[12px] text-ink-faint">
        {plan.isActive ? dueDateLabel(plan.nextDueOnUtc) : "—"}
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-right">
        {canSchedule && plan.isActive && (
          <Button onClick={onGenerate}>Generate work order</Button>
        )}
      </td>
    </tr>
  );
}

function GenerateWorkOrderModal({
  plan,
  onClose,
  onGenerated,
}: {
  plan: MaintenancePlanListItem;
  onClose: () => void;
  onGenerated: () => void;
}) {
  const token = useSession((state) => state.token);

  const [priority, setPriority] = useState<WorkOrderPriority>("Medium");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dialogRef = useDialogA11y<HTMLDivElement>(onClose);

  async function handleGenerate() {
    setError(null);
    setBusy(true);

    try {
      await api.generateWorkOrderFromPlan(plan.id, priority, token ?? undefined);
      onGenerated();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not generate a work order.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center p-4">
      <button
        aria-label="Close"
        onClick={onClose}
        className="absolute inset-0 bg-void/70"
        tabIndex={-1}
      />

      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label="Generate work order"
        tabIndex={-1}
        className="relative flex w-full max-w-md flex-col gap-4 rounded-[--radius-panel] border border-line bg-surface p-5 shadow-[--shadow-pop] focus:outline-none"
      >
        <div className="flex items-start justify-between gap-3">
          <h2 className="text-[15px] font-semibold tracking-[-0.01em] text-ink">
            Generate work order
          </h2>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-[--radius-control] p-2 text-ink-muted hover:bg-raised hover:text-ink"
          >
            <X size={16} aria-hidden />
          </button>
        </div>

        <p className="text-[13px] text-ink-muted">
          Dispatches <span className="text-ink">{plan.title}</span> as a work order. How urgently
          this occurrence needs doing is a dispatch decision — it is not copied from the plan.
        </p>

        <Field label="Priority">
          <Select value={priority} onChange={(event) => setPriority(event.target.value as WorkOrderPriority)}>
            {Object.entries(WORK_ORDER_PRIORITY_LABEL).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </Select>
        </Field>

        {error && (
          <p role="alert" className="rounded-[--radius-control] bg-failed-dim px-3 py-2 text-[13px] text-failed">
            {error}
          </p>
        )}

        <Button variant="primary" className="w-full justify-center" loading={busy} onClick={handleGenerate}>
          Generate
        </Button>
      </div>
    </div>
  );
}

function CreatePlanModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: () => void;
}) {
  const token = useSession((state) => state.token);

  const [assetSearch, setAssetSearch] = useState("");
  const [selectedAsset, setSelectedAsset] = useState<Asset | null>(null);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [frequencyDays, setFrequencyDays] = useState(90);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dialogRef = useDialogA11y<HTMLDivElement>(onClose);

  const { data: assetResults } = useQuery({
    queryKey: ["asset-search", assetSearch],
    queryFn: () =>
      api.listAssets(
        { searchTerm: assetSearch.trim() || undefined, pageSize: 6, excludeDecommissioned: true },
        token ?? undefined,
      ),
    enabled: !selectedAsset && assetSearch.trim().length > 0,
  });

  async function handleCreate() {
    setError(null);

    if (!selectedAsset) {
      setError("Choose an asset first.");
      return;
    }

    setBusy(true);

    try {
      const input: CreateMaintenancePlanInput = {
        assetId: selectedAsset.id,
        title: title.trim(),
        description: description.trim() || null,
        frequencyDays,
      };

      await api.createMaintenancePlan(input, token ?? undefined);
      onCreated();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not create this plan.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center p-4">
      <button
        aria-label="Close"
        onClick={onClose}
        className="absolute inset-0 bg-void/70"
        tabIndex={-1}
      />

      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label="Create maintenance plan"
        tabIndex={-1}
        className="relative flex w-full max-w-md flex-col gap-4 rounded-[--radius-panel] border border-line bg-surface p-5 shadow-[--shadow-pop] focus:outline-none"
      >
        <div className="flex items-start justify-between gap-3">
          <h2 className="text-[15px] font-semibold tracking-[-0.01em] text-ink">
            New maintenance plan
          </h2>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-[--radius-control] p-2 text-ink-muted hover:bg-raised hover:text-ink"
          >
            <X size={16} aria-hidden />
          </button>
        </div>

        <Field label="Asset" hint={selectedAsset ? undefined : "Search by code or name."}>
          {selectedAsset ? (
            <div className="flex items-center justify-between rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px]">
              <span className="text-ink">
                {selectedAsset.name} <span className="tabular text-ink-faint">{selectedAsset.code}</span>
              </span>
              <button
                onClick={() => {
                  setSelectedAsset(null);
                  setAssetSearch("");
                }}
                className="text-ink-faint hover:text-ink"
                aria-label="Change asset"
              >
                <X size={14} aria-hidden />
              </button>
            </div>
          ) : (
            <div className="relative">
              <Input
                value={assetSearch}
                onChange={(event) => setAssetSearch(event.target.value)}
                placeholder="e.g. HYD-NW-0042"
              />
              {assetResults && assetResults.items.length > 0 && (
                <ul className="absolute z-10 mt-1 w-full overflow-hidden rounded-[--radius-control] border border-line-strong bg-surface shadow-[--shadow-pop]">
                  {assetResults.items.map((asset) => (
                    <li key={asset.id}>
                      <button
                        onClick={() => setSelectedAsset(asset)}
                        className="flex w-full items-center justify-between px-3 py-2 text-left text-[13px] hover:bg-raised"
                      >
                        <span className="text-ink">{asset.name}</span>
                        <span className="tabular text-ink-faint">{asset.code}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </Field>

        <Field label="Title">
          <Input
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            placeholder="e.g. Quarterly valve inspection"
            maxLength={200}
          />
        </Field>

        <Field label="Description" hint="Optional.">
          <textarea
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={2}
            maxLength={4000}
            className="w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] text-ink focus:border-signal focus:outline-none"
          />
        </Field>

        <Field label="Frequency (days)">
          <Input
            type="number"
            min={1}
            max={3650}
            value={frequencyDays}
            onChange={(event) => setFrequencyDays(Number(event.target.value))}
          />
        </Field>

        {error && (
          <p role="alert" className="rounded-[--radius-control] bg-failed-dim px-3 py-2 text-[13px] text-failed">
            {error}
          </p>
        )}

        <Button
          variant="primary"
          className="w-full justify-center"
          loading={busy}
          disabled={!title.trim() || !selectedAsset}
          onClick={handleCreate}
        >
          Create plan
        </Button>
      </div>
    </div>
  );
}
