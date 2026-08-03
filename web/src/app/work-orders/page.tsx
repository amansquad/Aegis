"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Plus, Search, X } from "lucide-react";
import { api, ApiError } from "@/lib/api";
import { useSession } from "@/lib/store";
import {
  WORK_ORDER_PRIORITY_LABEL,
  WORK_ORDER_STATUS_LABEL,
  type CreateWorkOrderInput,
  type WorkOrderFilters,
  type WorkOrderListItem,
  type WorkOrderPriority,
  type WorkOrderStatus,
} from "@/lib/types";
import { relativeAge } from "@/lib/utils";
import { Button, EmptyState, ErrorState, Field, Input, Panel, RowSkeleton, Select } from "@/components/ui";
import { PriorityBadge, WorkOrderStatusPill } from "@/components/work-order-ui";
import { WorkOrderDetailDrawer } from "@/components/work-order-detail-drawer";

const PAGE_SIZE = 25;

type QuickFilter = "open" | "unassigned" | "all";

export default function WorkOrdersPage() {
  const token = useSession((state) => state.token);
  const hasPermission = useSession((state) => state.hasPermission);
  const queryClient = useQueryClient();

  const [quickFilter, setQuickFilter] = useState<QuickFilter>("open");
  const [search, setSearch] = useState("");
  const [priority, setPriority] = useState<WorkOrderPriority | "">("");
  const [status, setStatus] = useState<WorkOrderStatus | "">("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const filters: WorkOrderFilters = useMemo(
    () => ({
      searchTerm: search.trim() || undefined,
      priority: priority || undefined,
      status: status || undefined,
      openOnly: quickFilter === "open" || undefined,
      unassignedOnly: quickFilter === "unassigned" || undefined,
      page,
      pageSize: PAGE_SIZE,
      sortBy: "createdOnUtc",
      sortDirection: "Descending",
    }),
    [search, priority, status, quickFilter, page],
  );

  const { data, isPending, isFetching, error, refetch } = useQuery({
    queryKey: ["work-orders", filters],
    queryFn: () => api.listWorkOrders(filters, token ?? undefined),
    placeholderData: keepPreviousData,
  });

  const items = data?.items ?? [];
  const filtered = Boolean(search || priority || status);
  const selected = items.find((w) => w.id === selectedId) ?? null;

  function reset() {
    setSearch("");
    setPriority("");
    setStatus("");
    setPage(1);
  }

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["work-orders"] });
  }

  return (
    <div className="mx-auto flex max-w-[1400px] flex-col gap-4">
      <div className="resolve flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-[-0.02em] text-ink">Work orders</h1>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            {data ? `${data.totalCount.toLocaleString()} work orders` : "Loading"}
            {filtered && data ? " matching your filters" : ""}
          </p>
        </div>

        {hasPermission("workorders.create") && (
          <Button variant="primary" onClick={() => setCreating(true)}>
            <Plus size={14} aria-hidden />
            Dispatch work order
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
            ["open", "Open"],
            ["unassigned", "Awaiting assignment"],
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
            aria-label="Search work orders"
            className="w-full rounded-[--radius-control] border border-line-strong bg-raised py-2 pl-9 pr-3 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
          />
        </div>

        <FilterSelect
          label="Priority"
          value={priority}
          onChange={(value) => {
            setPriority(value as WorkOrderPriority | "");
            setPage(1);
          }}
          options={Object.entries(WORK_ORDER_PRIORITY_LABEL)}
        />
        <FilterSelect
          label="Status"
          value={status}
          onChange={(value) => {
            setStatus(value as WorkOrderStatus | "");
            setPage(1);
          }}
          options={Object.entries(WORK_ORDER_STATUS_LABEL)}
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
              title={filtered || quickFilter !== "all" ? "No work orders match this view" : "No work orders dispatched"}
              description={
                filtered || quickFilter !== "all"
                  ? "Nothing was excluded by a mistake here — try 'All' or clear the filters."
                  : "Work dispatched against an asset or an incident will appear here."
              }
              action={
                filtered ? (
                  <Button onClick={reset}>Clear filters</Button>
                ) : hasPermission("workorders.create") ? (
                  <Button variant="primary" onClick={() => setCreating(true)}>
                    Dispatch a work order
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
                      <th scope="col" className="px-4 py-2.5 font-medium">Priority</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Status</th>
                      <th scope="col" className="px-4 py-2.5 font-medium">Assigned to</th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">Dispatched</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {items.map((workOrder) => (
                      <WorkOrderRow
                        key={workOrder.id}
                        workOrder={workOrder}
                        onOpen={() => setSelectedId(workOrder.id)}
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
        <WorkOrderDetailDrawer
          workOrder={selected}
          onClose={() => setSelectedId(null)}
          onChanged={invalidate}
        />
      )}

      {creating && (
        <CreateWorkOrderModal
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false);
            invalidate();
          }}
        />
      )}
    </div>
  );
}

function WorkOrderRow({
  workOrder,
  onOpen,
}: {
  workOrder: WorkOrderListItem;
  onOpen: () => void;
}) {
  return (
    <tr onClick={onOpen} className="cursor-pointer transition-colors hover:bg-raised">
      <td className="tabular whitespace-nowrap px-4 py-3 text-[12px] text-ink-muted">
        {workOrder.reference}
      </td>
      <td className="px-4 py-3">
        <span className="line-clamp-1 max-w-md text-[13px] text-ink">{workOrder.title}</span>
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <PriorityBadge priority={workOrder.priority} />
      </td>
      <td className="whitespace-nowrap px-4 py-3">
        <WorkOrderStatusPill status={workOrder.status} />
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-[12px] text-ink-muted">
        {workOrder.assignedToUserId ? "Assigned" : "Unassigned"}
      </td>
      <td className="tabular whitespace-nowrap px-4 py-3 text-right text-[12px] text-ink-faint">
        {relativeAge(workOrder.createdOnUtc)}
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

function CreateWorkOrderModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: () => void;
}) {
  const token = useSession((state) => state.token);

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [priority, setPriority] = useState<WorkOrderPriority>("Medium");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleCreate() {
    setError(null);
    setBusy(true);

    try {
      const input: CreateWorkOrderInput = {
        title: title.trim(),
        description: description.trim() || null,
        priority,
      };

      await api.createWorkOrder(input, token ?? undefined);
      onCreated();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not dispatch this work order.");
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
        role="dialog"
        aria-label="Dispatch work order"
        className="relative flex w-full max-w-md flex-col gap-4 rounded-[--radius-panel] border border-line bg-surface p-5 shadow-[--shadow-pop]"
      >
        <div className="flex items-start justify-between gap-3">
          <h2 className="text-[15px] font-semibold tracking-[-0.01em] text-ink">
            Dispatch work order
          </h2>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-[--radius-control] p-1.5 text-ink-muted hover:bg-raised hover:text-ink"
          >
            <X size={16} aria-hidden />
          </button>
        </div>

        <Field label="Title">
          <Input
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            placeholder="e.g. Replace failed isolation valve"
            maxLength={200}
          />
        </Field>

        <Field label="Description" hint="Optional. Instructions or context for the technician.">
          <textarea
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={3}
            maxLength={4000}
            className="w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] text-ink focus:border-signal focus:outline-none"
          />
        </Field>

        <Field label="Priority">
          <Select
            value={priority}
            onChange={(event) => setPriority(event.target.value as WorkOrderPriority)}
          >
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

        <Button
          variant="primary"
          className="w-full justify-center"
          loading={busy}
          disabled={!title.trim()}
          onClick={handleCreate}
        >
          Dispatch
        </Button>
      </div>
    </div>
  );
}
