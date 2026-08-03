"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { X } from "lucide-react";
import { api, ApiError } from "@/lib/api";
import { useSession } from "@/lib/store";
import type { WorkOrderListItem } from "@/lib/types";
import { relativeAge } from "@/lib/utils";
import { Button, Field, Select } from "@/components/ui";
import { PriorityBadge, WorkOrderStatusPill } from "@/components/work-order-ui";

const OPEN_STATUSES = new Set(["Draft", "Scheduled", "InProgress"]);

/**
 * Detail and progression actions for one work order, as a side drawer for the same reason the
 * incident drawer is one: a dispatcher working down a queue does not lose their place in it.
 */
export function WorkOrderDetailDrawer({
  workOrder,
  onClose,
  onChanged,
}: {
  workOrder: WorkOrderListItem;
  onClose: () => void;
  onChanged: () => void;
}) {
  const token = useSession((state) => state.token);
  const hasPermission = useSession((state) => state.hasPermission);

  const [assignee, setAssignee] = useState("");
  const [completionNotes, setCompletionNotes] = useState("");
  const [cancellationReason, setCancellationReason] = useState("");

  const [busy, setBusy] = useState<"assign" | "start" | "complete" | "cancel" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const isOpen = OPEN_STATUSES.has(workOrder.status);
  const canAssign = hasPermission("workorders.assign");
  const canComplete = hasPermission("workorders.complete");

  const { data: assignableUsers } = useQuery({
    queryKey: ["assignable-users"],
    queryFn: () => api.listAssignableUsers(token ?? undefined),
    enabled: isOpen && canAssign,
  });

  async function withBusy(action: typeof busy, run: () => Promise<void>) {
    setError(null);
    setBusy(action);

    try {
      await run();
      onChanged();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "That action could not be completed.");
    } finally {
      setBusy(null);
    }
  }

  async function handleAssign() {
    if (!assignee) return;

    await withBusy("assign", async () => {
      await api.assignWorkOrder(workOrder.id, { userId: assignee }, token ?? undefined);
    });
  }

  async function handleStart() {
    await withBusy("start", async () => {
      await api.startWorkOrder(workOrder.id, token ?? undefined);
    });
  }

  async function handleComplete() {
    await withBusy("complete", async () => {
      await api.completeWorkOrder(workOrder.id, completionNotes.trim() || null, token ?? undefined);
      onClose();
    });
  }

  async function handleCancel() {
    await withBusy("cancel", async () => {
      await api.cancelWorkOrder(workOrder.id, cancellationReason.trim() || null, token ?? undefined);
      onClose();
    });
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
        aria-label={`Work order ${workOrder.reference}`}
        className="relative flex h-full w-full max-w-md flex-col overflow-y-auto border-l border-line bg-surface shadow-[--shadow-pop]"
      >
        <header className="flex items-start justify-between gap-3 border-b border-line px-5 py-4">
          <div>
            <p className="tabular text-[12px] text-ink-faint">{workOrder.reference}</p>
            <div className="mt-1 flex items-center gap-2">
              <WorkOrderStatusPill status={workOrder.status} />
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
          <div>
            <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">Title</p>
            <p className="mt-1 text-[13px] leading-relaxed text-ink">{workOrder.title}</p>
          </div>

          <div className="flex flex-wrap items-center gap-4 text-[12px] text-ink-muted">
            <PriorityBadge priority={workOrder.priority} />
          </div>

          <dl className="grid grid-cols-2 gap-3 text-[12px]">
            <div>
              <dt className="text-ink-faint">Dispatched</dt>
              <dd className="mt-0.5 text-ink">{relativeAge(workOrder.createdOnUtc)}</dd>
            </div>
            {workOrder.scheduledFor && (
              <div>
                <dt className="text-ink-faint">Scheduled for</dt>
                <dd className="mt-0.5 text-ink">
                  {new Date(workOrder.scheduledFor).toLocaleDateString(undefined, {
                    day: "numeric",
                    month: "short",
                  })}
                </dd>
              </div>
            )}
            {workOrder.assetId && (
              <div className="col-span-2">
                <dt className="text-ink-faint">Linked asset</dt>
                <dd className="tabular mt-0.5 text-ink">{workOrder.assetId}</dd>
              </div>
            )}
            {workOrder.incidentId && (
              <div className="col-span-2">
                <dt className="text-ink-faint">Resolves incident</dt>
                <dd className="tabular mt-0.5 text-ink">{workOrder.incidentId}</dd>
              </div>
            )}
            {workOrder.startedOnUtc && (
              <div>
                <dt className="text-ink-faint">Started</dt>
                <dd className="mt-0.5 text-ink">{relativeAge(workOrder.startedOnUtc)}</dd>
              </div>
            )}
            {workOrder.completedOnUtc && (
              <div>
                <dt className="text-ink-faint">Completed</dt>
                <dd className="mt-0.5 text-ink">{relativeAge(workOrder.completedOnUtc)}</dd>
              </div>
            )}
          </dl>

          {isOpen && canAssign && (
            <>
              <hr className="border-line" />

              <div>
                <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">
                  {workOrder.assignedToUserId ? "Reassign" : "Assign"}
                </p>

                <Field label="Technician" hint="Reassigning is fine — nothing about that needs undoing first.">
                  <Select value={assignee} onChange={(event) => setAssignee(event.target.value)}>
                    <option value="">Choose a technician</option>
                    {assignableUsers?.map((user) => (
                      <option key={user.id} value={user.id}>
                        {user.displayName}
                      </option>
                    ))}
                  </Select>
                </Field>

                <Button
                  variant="primary"
                  className="mt-3 w-full justify-center"
                  loading={busy === "assign"}
                  disabled={!assignee}
                  onClick={handleAssign}
                >
                  {workOrder.assignedToUserId ? "Reassign" : "Assign"}
                </Button>
              </div>
            </>
          )}

          {workOrder.status === "Scheduled" && canComplete && (
            <>
              <hr className="border-line" />
              <Button className="w-full justify-center" loading={busy === "start"} onClick={handleStart}>
                Mark as underway
              </Button>
            </>
          )}

          {isOpen && workOrder.assignedToUserId && canComplete && (
            <>
              <hr className="border-line" />

              <div>
                <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">
                  Complete
                </p>

                <Field label="What was done" hint="Optional.">
                  <textarea
                    value={completionNotes}
                    onChange={(event) => setCompletionNotes(event.target.value)}
                    rows={2}
                    maxLength={2000}
                    placeholder="e.g. Valve replaced, section repressurised"
                    className="mt-1.5 w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
                  />
                </Field>

                <Button
                  variant="primary"
                  className="mt-3 w-full justify-center"
                  loading={busy === "complete"}
                  onClick={handleComplete}
                >
                  Mark completed
                </Button>
              </div>
            </>
          )}

          {isOpen && canAssign && (
            <>
              <hr className="border-line" />

              <div>
                <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">
                  Cancel
                </p>

                <Field label="Reason" hint="Optional.">
                  <textarea
                    value={cancellationReason}
                    onChange={(event) => setCancellationReason(event.target.value)}
                    rows={2}
                    maxLength={500}
                    placeholder="e.g. Duplicate dispatch"
                    className="mt-1.5 w-full resize-y rounded-[--radius-control] border border-line-strong bg-raised px-3 py-2 text-[13px] text-ink placeholder:text-ink-faint focus:border-signal focus:outline-none"
                  />
                </Field>

                <Button
                  className="mt-3 w-full justify-center"
                  loading={busy === "cancel"}
                  onClick={handleCancel}
                >
                  Withdraw work order
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
