"use client";

import { useQuery } from "@tanstack/react-query";
import { X } from "lucide-react";
import { api } from "@/lib/api";
import { useSession } from "@/lib/store";
import type { Asset } from "@/lib/types";
import { STATUS_LABEL, TYPE_LABEL } from "@/lib/types";
import { formatCoordinate, relativeAge } from "@/lib/utils";
import { ConditionBadge, CriticalityMeter, StatusPill } from "@/components/ui";
import { useDialogA11y } from "@/lib/use-dialog-a11y";
import { SeverityBadge, IncidentStatusPill } from "@/components/incident-ui";
import { PriorityBadge, WorkOrderStatusPill } from "@/components/work-order-ui";
import { DueBadge } from "@/components/maintenance-ui";

/**
 * Read-only detail for one asset, as a side drawer for the same reason the incident and work
 * order drawers are: an operator scanning the registry does not lose their place in the table
 * behind it.
 *
 * Nothing here is editable — the asset registry itself has no mutation endpoints in this build —
 * so unlike the other two drawers this one only reads. What it adds over the table row is the
 * cross-module picture the row can't show: the incidents, work orders and maintenance plans that
 * reference this asset, each a door into the module that owns it.
 */
export function AssetDetailDrawer({ asset, onClose }: { asset: Asset; onClose: () => void }) {
  const token = useSession((state) => state.token);
  const dialogRef = useDialogA11y<HTMLElement>(onClose);

  const { data: incidents, isPending: incidentsPending } = useQuery({
    queryKey: ["asset-detail", asset.id, "incidents"],
    queryFn: () =>
      api.listIncidents(
        { assetId: asset.id, pageSize: 5, sortBy: "reportedOnUtc", sortDirection: "Descending" },
        token ?? undefined,
      ),
  });

  const { data: workOrders, isPending: workOrdersPending } = useQuery({
    queryKey: ["asset-detail", asset.id, "work-orders"],
    queryFn: () =>
      api.listWorkOrders(
        { assetId: asset.id, pageSize: 5, sortBy: "createdOnUtc", sortDirection: "Descending" },
        token ?? undefined,
      ),
  });

  const { data: maintenancePlans, isPending: maintenancePending } = useQuery({
    queryKey: ["asset-detail", asset.id, "maintenance"],
    queryFn: () => api.listMaintenancePlans({ assetId: asset.id, pageSize: 5 }, token ?? undefined),
  });

  return (
    <div className="fixed inset-0 z-40 flex justify-end">
      <button
        aria-label="Close"
        onClick={onClose}
        className="absolute inset-0 bg-void/70"
        tabIndex={-1}
      />

      <aside
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label={`Asset ${asset.code}`}
        tabIndex={-1}
        className="relative flex h-full w-full max-w-md flex-col overflow-y-auto border-l border-line bg-surface shadow-[--shadow-pop] focus:outline-none"
      >
        <header className="flex items-start justify-between gap-3 border-b border-line px-5 py-4">
          <div>
            <p className="tabular text-[12px] text-ink-faint">{asset.code}</p>
            <div className="mt-1 flex items-center gap-2">
              <StatusPill status={asset.status} />
            </div>
          </div>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-[--radius-control] p-2 text-ink-muted hover:bg-raised hover:text-ink"
          >
            <X size={16} aria-hidden />
          </button>
        </header>

        <div className="flex flex-col gap-4 px-5 py-4">
          <div>
            <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">Name</p>
            <p className="mt-1 text-[13px] leading-relaxed text-ink">{asset.name}</p>
          </div>

          <div className="flex flex-wrap items-center gap-4 text-[12px] text-ink-muted">
            <span>{TYPE_LABEL[asset.type]}</span>
            <ConditionBadge condition={asset.condition} />
            <CriticalityMeter level={asset.criticality} />
          </div>

          <dl className="grid grid-cols-2 gap-3 text-[12px]">
            <div>
              <dt className="text-ink-faint">Status</dt>
              <dd className="mt-0.5 text-ink">{STATUS_LABEL[asset.status]}</dd>
            </div>
            <div>
              <dt className="text-ink-faint">Last inspected</dt>
              <dd className="mt-0.5 text-ink">{relativeAge(asset.lastInspectedOnUtc)}</dd>
            </div>
            <div className="col-span-2">
              <dt className="text-ink-faint">Location</dt>
              <dd className="tabular mt-0.5 text-ink">
                {formatCoordinate(asset.latitude, asset.longitude)}
              </dd>
            </div>
            {asset.installedOn && (
              <div className="col-span-2">
                <dt className="text-ink-faint">Installed</dt>
                <dd className="mt-0.5 text-ink">{relativeAge(asset.installedOn)}</dd>
              </div>
            )}
          </dl>

          <hr className="border-line" />

          <LinkedSection
            title="Incidents"
            pending={incidentsPending}
            emptyText="No incidents reference this asset."
          >
            {incidents?.items.map((incident) => (
              <li key={incident.id} className="flex items-center gap-3 px-4 py-2.5">
                <span className="min-w-0 flex-1">
                  <span className="line-clamp-1 block text-[13px] text-ink">{incident.summary}</span>
                  <span className="tabular mt-0.5 block text-[11px] text-ink-faint">
                    {incident.reference}
                  </span>
                </span>
                <IncidentStatusPill status={incident.status} />
                <SeverityBadge severity={incident.severity} />
              </li>
            ))}
          </LinkedSection>

          <LinkedSection
            title="Work orders"
            pending={workOrdersPending}
            emptyText="No work orders reference this asset."
          >
            {workOrders?.items.map((workOrder) => (
              <li key={workOrder.id} className="flex items-center gap-3 px-4 py-2.5">
                <span className="min-w-0 flex-1">
                  <span className="line-clamp-1 block text-[13px] text-ink">{workOrder.title}</span>
                  <span className="tabular mt-0.5 block text-[11px] text-ink-faint">
                    {workOrder.reference}
                  </span>
                </span>
                <WorkOrderStatusPill status={workOrder.status} />
                <PriorityBadge priority={workOrder.priority} />
              </li>
            ))}
          </LinkedSection>

          <LinkedSection
            title="Maintenance plans"
            pending={maintenancePending}
            emptyText="No maintenance plans cover this asset."
          >
            {maintenancePlans?.items.map((plan) => (
              <li key={plan.id} className="flex items-center gap-3 px-4 py-2.5">
                <span className="min-w-0 flex-1">
                  <span className="line-clamp-1 block text-[13px] text-ink">{plan.title}</span>
                  <span className="tabular mt-0.5 block text-[11px] text-ink-faint">
                    {plan.reference} · every {plan.frequencyDays}d
                  </span>
                </span>
                <DueBadge isDue={plan.isDue} isActive={plan.isActive} />
              </li>
            ))}
          </LinkedSection>
        </div>
      </aside>
    </div>
  );
}

function LinkedSection({
  title,
  pending,
  emptyText,
  children,
}: {
  title: string;
  pending: boolean;
  emptyText: string;
  children: React.ReactNode;
}) {
  const hasChildren = Array.isArray(children) ? children.length > 0 : Boolean(children);

  return (
    <div>
      <p className="text-[11px] font-medium uppercase tracking-wider text-ink-faint">{title}</p>
      {pending ? (
        <p className="mt-2 text-[12px] text-ink-faint">Loading…</p>
      ) : hasChildren ? (
        <ul className="mt-2 divide-y divide-line rounded-[--radius-control] border border-line">
          {children}
        </ul>
      ) : (
        <p className="mt-2 text-[12px] text-ink-faint">{emptyText}</p>
      )}
    </div>
  );
}
