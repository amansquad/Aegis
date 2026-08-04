"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useState } from "react";
import {
  Activity as ActivityIcon,
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  ClipboardList,
  FileText,
  Wrench,
} from "lucide-react";
import { api } from "@/lib/api";
import { useSession } from "@/lib/store";
import { CONDITION_LABEL, TYPE_LABEL, type Asset, type AssetCondition } from "@/lib/types";
import { cn, relativeAge } from "@/lib/utils";
import { ConditionBadge, ErrorState, Panel, RowSkeleton } from "@/components/ui";
import { CountUp, RadialGauge } from "@/components/dashboard-ui";
import { SeverityBadge } from "@/components/incident-ui";
import { PriorityBadge } from "@/components/work-order-ui";

const CONDITION_BAR: Record<AssetCondition, string> = {
  VeryGood: "bg-nominal",
  Good: "bg-nominal/70",
  Fair: "bg-watch",
  Poor: "bg-degraded",
  VeryPoor: "bg-failed",
  Unknown: "bg-line-strong",
};

export default function DashboardPage() {
  const token = useSession((state) => state.token);
  const user = useSession((state) => state.user);

  // A tick that changes every 30s purely to re-render the "updated Xs ago" caption. Nothing
  // downstream of this depends on it for correctness — it only keeps a displayed string honest.
  const [, forceTick] = useState(0);
  useEffect(() => {
    const id = setInterval(() => forceTick((n) => n + 1), 30_000);
    return () => clearInterval(id);
  }, []);

  const { data, isPending, error, refetch, dataUpdatedAt } = useQuery({
    queryKey: ["assets", "all"],
    // The whole estate, because every panel on this page is a different cut of the same set.
    // Five separate filtered requests would be five round trips to answer one question.
    queryFn: () => api.listAssets({ pageSize: 100, page: 1 }, token ?? undefined),
  });

  const { data: fullSet } = useQuery({
    queryKey: ["assets", "summary"],
    queryFn: async () => {
      const pages = await Promise.all(
        [1, 2, 3, 4, 5].map((page) =>
          api.listAssets({ page, pageSize: 100 }, token ?? undefined),
        ),
      );
      return pages.flatMap((page) => page.items);
    },
  });

  // Cross-module summaries. Each fetch asks for at most five rows — the count comes free on
  // `totalCount`, and the rows themselves feed the activity feed and priority queue below, so
  // there is no separate "just give me the number" request to make.
  const { data: openIncidents } = useQuery({
    queryKey: ["dashboard", "incidents-open"],
    queryFn: () => api.listIncidents({ openOnly: true, pageSize: 1 }, token ?? undefined),
  });

  const { data: safetyIncidents } = useQuery({
    queryKey: ["dashboard", "incidents-safety"],
    queryFn: () =>
      api.listIncidents(
        { safetyRiskOnly: true, openOnly: true, pageSize: 5 },
        token ?? undefined,
      ),
  });

  const { data: recentIncidents } = useQuery({
    queryKey: ["dashboard", "incidents-recent"],
    queryFn: () =>
      api.listIncidents(
        { pageSize: 5, sortBy: "reportedOnUtc", sortDirection: "Descending" },
        token ?? undefined,
      ),
  });

  const { data: unassignedWorkOrders } = useQuery({
    queryKey: ["dashboard", "work-orders-unassigned"],
    queryFn: () =>
      api.listWorkOrders(
        { unassignedOnly: true, pageSize: 5, sortBy: "priority", sortDirection: "Descending" },
        token ?? undefined,
      ),
  });

  const { data: recentCompletedWorkOrders } = useQuery({
    queryKey: ["dashboard", "work-orders-completed"],
    queryFn: () =>
      api.listWorkOrders(
        { status: "Completed", pageSize: 5, sortBy: "completedOnUtc", sortDirection: "Descending" },
        token ?? undefined,
      ),
  });

  const { data: dueMaintenance } = useQuery({
    queryKey: ["dashboard", "maintenance-due"],
    queryFn: () => api.listMaintenancePlans({ dueOnly: true, pageSize: 5 }, token ?? undefined),
  });

  const assets = fullSet ?? data?.items ?? [];
  const live = assets.filter((asset) => asset.status !== "Decommissioned");

  const failing = live.filter((a) => a.condition === "VeryPoor" || a.condition === "Poor");
  const faulted = live.filter((a) => a.status === "Faulted");
  const unassessed = live.filter((a) => a.condition === "Unknown");

  // Worst first, then by criticality: the order a duty engineer would triage in, not alphabetical.
  const CONDITION_RANK: Record<AssetCondition, number> = {
    VeryPoor: 0,
    Poor: 1,
    Fair: 2,
    Good: 3,
    VeryGood: 4,
    Unknown: 5,
  };
  const CRITICALITY_RANK = { Critical: 0, High: 1, Medium: 2, Low: 3 };

  const worstAssets = [...failing]
    .sort(
      (a, b) =>
        CONDITION_RANK[a.condition] - CONDITION_RANK[b.condition] ||
        CRITICALITY_RANK[a.criticality] - CRITICALITY_RANK[b.criticality],
    )
    .slice(0, 3);

  const conditionSpread = (["VeryGood", "Good", "Fair", "Poor", "VeryPoor", "Unknown"] as const).map(
    (condition) => ({
      condition,
      count: live.filter((asset) => asset.condition === condition).length,
    }),
  );

  // The one number the gauge exists to show: what share of the live estate is Fair or better,
  // rendered as a score rather than a bar list so it can be read from across the room.
  const assessedCount = live.length - unassessed.length;
  const healthyCount = live.filter((a) => a.condition !== "Poor" && a.condition !== "VeryPoor").length;
  const healthScore = assessedCount > 0 ? (healthyCount / live.length) * 100 : 100;
  const healthTone =
    healthScore >= 90 ? "nominal" : healthScore >= 75 ? "watch" : healthScore >= 55 ? "degraded" : "failed";

  // A single merged, time-ordered feed. Two event kinds today — a report landing, a job closing
  // out — is enough for this to read as "things are happening" rather than a static snapshot.
  type FeedEntry = {
    id: string;
    time: string;
    icon: typeof FileText;
    tone: string;
    text: string;
    meta: string;
    href: string;
  };

  const feed: FeedEntry[] = [
    ...(recentIncidents?.items ?? []).map((incident) => ({
      id: `incident-${incident.id}`,
      time: incident.reportedOnUtc,
      icon: FileText,
      tone: incident.publicSafetyRisk ? "text-failed" : "text-signal",
      text: incident.summary,
      meta: `${incident.reference} reported`,
      href: "/incidents",
    })),
    ...(recentCompletedWorkOrders?.items ?? []).map((workOrder) => ({
      id: `wo-${workOrder.id}`,
      time: workOrder.completedOnUtc ?? workOrder.createdOnUtc,
      icon: CheckCircle2,
      tone: "text-nominal",
      text: workOrder.title,
      meta: `${workOrder.reference} completed`,
      href: "/work-orders",
    })),
  ]
    .sort((a, b) => new Date(b.time).getTime() - new Date(a.time).getTime())
    .slice(0, 8);

  // Four modules, one ranked list of what actually needs a human today. Safety risk outranks
  // everything else by construction; the remaining groups keep whatever order their own query
  // already sorted them in (priority for work orders, most-overdue-first for maintenance).
  type QueueEntry = {
    id: string;
    icon: typeof AlertTriangle;
    tone: string;
    title: string;
    detail: string;
    badge: React.ReactNode;
    href: string;
  };

  const queue: QueueEntry[] = [
    ...(safetyIncidents?.items ?? []).map((incident) => ({
      id: `incident-${incident.id}`,
      icon: AlertTriangle,
      tone: "text-failed",
      title: incident.summary,
      detail: `${incident.reference} — public safety risk`,
      badge: <SeverityBadge severity={incident.severity} />,
      href: "/incidents",
    })),
    ...worstAssets.map((asset) => ({
      id: `asset-${asset.id}`,
      icon: ActivityIcon,
      tone: "text-degraded",
      title: asset.name,
      detail: `${asset.code} — condition`,
      badge: <ConditionBadge condition={asset.condition} />,
      href: "/assets",
    })),
    ...(unassignedWorkOrders?.items ?? []).map((workOrder) => ({
      id: `wo-${workOrder.id}`,
      icon: ClipboardList,
      tone: "text-watch",
      title: workOrder.title,
      detail: `${workOrder.reference} — awaiting assignment`,
      badge: <PriorityBadge priority={workOrder.priority} />,
      href: "/work-orders",
    })),
    ...(dueMaintenance?.items ?? []).map((plan) => ({
      id: `plan-${plan.id}`,
      icon: Wrench,
      tone: "text-ink-muted",
      title: plan.title,
      detail: `${plan.reference} — due`,
      badge: (
        <span className="tabular text-[12px] text-ink-faint">
          every {plan.frequencyDays}d
        </span>
      ),
      href: "/maintenance",
    })),
  ].slice(0, 8);

  if (error) {
    return <ErrorState message={(error as Error).message} onRetry={() => refetch()} />;
  }

  return (
    <div className="mx-auto flex max-w-[1400px] flex-col gap-4">
      <div className="resolve flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-[-0.02em] text-ink">
            {greeting()}, {user?.displayName.split(" ")[0]}
          </h1>
          <p className="mt-0.5 flex items-center gap-2 text-[13px] text-ink-muted">
            <span className="relative flex size-1.5">
              <span className="absolute inline-flex size-full animate-ping rounded-full bg-nominal opacity-60" />
              <span className="relative inline-flex size-1.5 rounded-full bg-nominal" />
            </span>
            Live — updated {dataUpdatedAt ? relativeAge(new Date(dataUpdatedAt).toISOString()) : "just now"}
          </p>
        </div>
        <Link
          href="/assets"
          className="text-[13px] font-medium text-signal transition-opacity hover:opacity-80"
        >
          Open the registry →
        </Link>
      </div>

      {/* Five cross-module readouts, each a door into the module it counts. */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <KpiCard
          href="/assets?condition=Poor"
          delay={40}
          icon={ActivityIcon}
          tone="failed"
          label="Assets needing attention"
          value={failing.length}
          pending={isPending}
          caption="poor condition or worse"
        />
        <KpiCard
          href="/assets?status=Faulted"
          delay={60}
          icon={Wrench}
          tone="degraded"
          label="Faulted assets"
          value={faulted.length}
          pending={isPending}
          caption="out of service now"
        />
        <KpiCard
          href="/incidents?openOnly=true"
          delay={80}
          icon={FileText}
          tone="signal"
          label="Open incidents"
          value={openIncidents?.totalCount ?? 0}
          pending={openIncidents === undefined}
          caption={
            (safetyIncidents?.totalCount ?? 0) > 0
              ? `${safetyIncidents!.totalCount} public safety risk`
              : "awaiting triage or in progress"
          }
          captionTone={(safetyIncidents?.totalCount ?? 0) > 0 ? "text-failed" : undefined}
        />
        <KpiCard
          href="/work-orders?unassignedOnly=true"
          delay={100}
          icon={ClipboardList}
          tone="watch"
          label="Work orders unassigned"
          value={unassignedWorkOrders?.totalCount ?? 0}
          pending={unassignedWorkOrders === undefined}
          caption="awaiting a technician"
        />
        <KpiCard
          href="/maintenance?dueOnly=true"
          delay={120}
          icon={Wrench}
          tone="ink-muted"
          label="Maintenance due"
          value={dueMaintenance?.totalCount ?? 0}
          pending={dueMaintenance === undefined}
          caption="ready to dispatch"
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-[1.6fr_1fr]">
        <Panel
          title="Live activity"
          delay={160}
          action={<span className="text-[11px] uppercase tracking-wider text-ink-faint">Newest first</span>}
        >
          {feed.length === 0 && recentIncidents === undefined ? (
            <RowSkeleton count={6} />
          ) : feed.length === 0 ? (
            <div className="px-4 py-12 text-center">
              <p className="text-[14px] font-medium text-ink">Nothing has happened yet</p>
              <p className="mt-1 text-[13px] text-ink-muted">
                Reports and completed work will appear here as they happen.
              </p>
            </div>
          ) : (
            <ul className="divide-y divide-line">
              {feed.map((entry) => (
                <li key={entry.id}>
                  <Link
                    href={entry.href}
                    className="flex items-start gap-3 px-4 py-3 transition-colors hover:bg-raised"
                  >
                    <entry.icon size={15} aria-hidden className={`mt-0.5 shrink-0 ${entry.tone}`} />
                    <span className="min-w-0 flex-1">
                      <span className="line-clamp-1 block text-[13px] text-ink">{entry.text}</span>
                      <span className="tabular mt-0.5 block text-[11px] text-ink-faint">{entry.meta}</span>
                    </span>
                    <span className="tabular shrink-0 text-[11px] text-ink-faint">
                      {relativeAge(entry.time)}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <div className="flex flex-col gap-4">
          <Panel title="Fleet health" delay={200} bodyClassName="flex flex-col items-center gap-3 p-5">
            {isPending ? (
              <div className="size-[168px] animate-pulse rounded-full bg-raised" />
            ) : (
              <>
                <RadialGauge percent={healthScore} label="Fair or better" tone={healthTone} />
                <p className="text-center text-[12px] text-ink-faint">
                  {live.length.toLocaleString()} assets in service · {unassessed.length} never assessed
                </p>
              </>
            )}
          </Panel>

          <Panel title="Condition profile" delay={220} bodyClassName="p-4">
            {isPending ? (
              <div className="h-40 animate-pulse rounded bg-raised" />
            ) : (
              <div className="flex flex-col gap-2.5">
                {conditionSpread.map(({ condition, count }) => {
                  const share = live.length ? (count / live.length) * 100 : 0;

                  return (
                    <div key={condition} className="flex items-center gap-3">
                      <span className="w-20 shrink-0 text-[12px] text-ink-muted">
                        {CONDITION_LABEL[condition]}
                      </span>
                      <div className="h-2 min-w-0 flex-1 overflow-hidden rounded-full bg-raised">
                        <div
                          className={`h-full rounded-full transition-[width] duration-700 ${CONDITION_BAR[condition]}`}
                          style={{ width: `${Math.max(share, count > 0 ? 1.5 : 0)}%` }}
                        />
                      </div>
                      <span className="tabular w-8 shrink-0 text-right text-[12px] text-ink">
                        {count}
                      </span>
                    </div>
                  );
                })}
              </div>
            )}
          </Panel>
        </div>
      </div>

      <Panel
        title="Priority queue"
        delay={260}
        action={
          <span className="text-[11px] uppercase tracking-wider text-ink-faint">
            Across every module
          </span>
        }
      >
        {queue.length === 0 && safetyIncidents === undefined ? (
          <RowSkeleton count={5} />
        ) : queue.length === 0 ? (
          <div className="px-4 py-12 text-center">
            <p className="text-[14px] font-medium text-nominal">Nothing needs attention right now</p>
            <p className="mt-1 text-[13px] text-ink-muted">
              No safety risks, poor-condition assets, unassigned work or overdue maintenance.
            </p>
          </div>
        ) : (
          <ul className="divide-y divide-line">
            {queue.map((entry) => (
              <li key={entry.id}>
                <Link
                  href={entry.href}
                  className="flex items-center gap-3 px-4 py-3 transition-colors hover:bg-raised"
                >
                  <entry.icon size={15} aria-hidden className={`shrink-0 ${entry.tone}`} />
                  <span className="min-w-0 flex-1">
                    <span className="line-clamp-1 block text-[13px] text-ink">{entry.title}</span>
                    <span className="tabular mt-0.5 block text-[11px] text-ink-faint">
                      {entry.detail}
                    </span>
                  </span>
                  <span className="shrink-0">{entry.badge}</span>
                  <ArrowRight size={13} aria-hidden className="shrink-0 text-ink-faint" />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      <Panel title="Estate composition" delay={300} bodyClassName="p-4">
        {isPending ? (
          <div className="h-24 animate-pulse rounded bg-raised" />
        ) : (
          <ul className="grid grid-cols-2 gap-x-6 gap-y-2 sm:grid-cols-3 lg:grid-cols-7">
            {topTypes(live).map(([type, count]) => (
              <li key={type} className="flex items-center justify-between gap-3 text-[13px]">
                <span className="text-ink-muted">{TYPE_LABEL[type as never] ?? type}</span>
                <span className="tabular text-ink">{count}</span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

type KpiTone = "failed" | "degraded" | "watch" | "signal" | "nominal" | "ink-muted";

/**
 * A lookup rather than `text-${tone}` interpolation. Tailwind's build-time scanner matches literal
 * class strings in source — a template literal built from a variable produces a class name the
 * scanner never sees, so the utility is silently missing from the generated stylesheet.
 */
const KPI_TONE_CLASS: Record<KpiTone, string> = {
  failed: "text-failed",
  degraded: "text-degraded",
  watch: "text-watch",
  signal: "text-signal",
  nominal: "text-nominal",
  "ink-muted": "text-ink-muted",
};

function KpiCard({
  href,
  delay,
  icon: Icon,
  tone,
  label,
  value,
  caption,
  captionTone,
  pending,
}: {
  href: string;
  delay: number;
  icon: typeof ActivityIcon;
  tone: KpiTone;
  label: string;
  value: number;
  caption: string;
  captionTone?: string;
  pending: boolean;
}) {
  const toneClass = KPI_TONE_CLASS[tone];

  return (
    <Link
      href={href}
      style={{ animationDelay: `${delay}ms` }}
      className="resolve group flex flex-col gap-2.5 rounded-[--radius-panel] border border-line bg-surface p-4 shadow-[--shadow-panel] transition-all duration-150 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-[--shadow-pop]"
    >
      <div className="flex items-center justify-between">
        <span className="text-[11px] font-medium text-ink-muted">{label}</span>
        <Icon
          size={14}
          aria-hidden
          className={cn(toneClass, "transition-transform duration-150 group-hover:scale-110")}
        />
      </div>
      {pending ? (
        <div className="h-8 w-14 animate-pulse rounded bg-raised" />
      ) : (
        <CountUp value={value} className={cn("text-[28px] leading-none", toneClass)} />
      )}
      <span className={cn("text-[11px]", captionTone ?? "text-ink-faint")}>{caption}</span>
    </Link>
  );
}

function greeting() {
  const hour = new Date().getHours();
  if (hour < 12) return "Good morning";
  if (hour < 18) return "Good afternoon";
  return "Good evening";
}

function topTypes(assets: Asset[]) {
  const counts = new Map<string, number>();

  for (const asset of assets) {
    counts.set(asset.type, (counts.get(asset.type) ?? 0) + 1);
  }

  return [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 7);
}
