"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api } from "@/lib/api";
import { useSession } from "@/lib/store";
import { CONDITION_LABEL, TYPE_LABEL, type Asset, type AssetCondition } from "@/lib/types";
import { relativeAge } from "@/lib/utils";
import {
  ConditionBadge,
  CriticalityMeter,
  ErrorState,
  Panel,
  RowSkeleton,
  StatusPill,
} from "@/components/ui";

/** Inspections older than this are treated as stale. Two years is typical for a distribution asset. */
const STALE_INSPECTION_DAYS = 730;

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

  const { data, isPending, error, refetch } = useQuery({
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

  const assets = fullSet ?? data?.items ?? [];
  const live = assets.filter((asset) => asset.status !== "Decommissioned");

  const failing = live.filter((a) => a.condition === "VeryPoor" || a.condition === "Poor");
  const faulted = live.filter((a) => a.status === "Faulted");
  const unassessed = live.filter((a) => a.condition === "Unknown");

  const staleThreshold = Date.now() - STALE_INSPECTION_DAYS * 86_400_000;
  const overdue = live.filter(
    (a) => a.lastInspectedOnUtc !== null && new Date(a.lastInspectedOnUtc).getTime() < staleThreshold,
  );

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

  const attention = [...failing]
    .sort(
      (a, b) =>
        CONDITION_RANK[a.condition] - CONDITION_RANK[b.condition] ||
        CRITICALITY_RANK[a.criticality] - CRITICALITY_RANK[b.criticality],
    )
    .slice(0, 8);

  const conditionSpread = (["VeryGood", "Good", "Fair", "Poor", "VeryPoor", "Unknown"] as const).map(
    (condition) => ({
      condition,
      count: live.filter((asset) => asset.condition === condition).length,
    }),
  );

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
          <p className="mt-0.5 text-[13px] text-ink-muted">
            {live.length
              ? `${live.length} assets in service across 5 districts`
              : "Loading the estate"}
          </p>
        </div>
        <Link
          href="/assets"
          className="text-[13px] font-medium text-signal transition-opacity hover:opacity-80"
        >
          Open the registry →
        </Link>
      </div>

      {/*
        A status strip, not a grid of identical stat cards. The four numbers sit in one panel
        because they answer one question — "is anything wrong right now?" — and separating them
        into four boxes would imply they are four unrelated facts.
      */}
      <Panel delay={40}>
        <dl className="grid grid-cols-2 divide-line md:grid-cols-4 md:divide-x">
          <Readout
            label="Poor or worse"
            value={failing.length}
            tone={failing.length > 0 ? "text-failed" : "text-ink"}
            caption="need intervention"
            pending={isPending}
          />
          <Readout
            label="Faulted"
            value={faulted.length}
            tone={faulted.length > 0 ? "text-degraded" : "text-ink"}
            caption="out of service now"
            pending={isPending}
          />
          <Readout
            label="Inspection overdue"
            value={overdue.length}
            tone={overdue.length > 0 ? "text-watch" : "text-ink"}
            caption="over 24 months"
            pending={isPending}
          />
          <Readout
            label="Never assessed"
            value={unassessed.length}
            tone="text-ink-muted"
            caption="no condition on record"
            pending={isPending}
          />
        </dl>
      </Panel>

      <div className="grid gap-4 xl:grid-cols-[1.6fr_1fr]">
        <Panel
          title="Needs attention"
          delay={80}
          action={
            <Link href="/assets?condition=Poor" className="text-[12px] text-signal hover:opacity-80">
              See all
            </Link>
          }
        >
          {isPending ? (
            <RowSkeleton count={6} />
          ) : attention.length === 0 ? (
            <div className="px-4 py-12 text-center">
              <p className="text-[14px] font-medium text-nominal">Nothing is in poor condition</p>
              <p className="mt-1 text-[13px] text-ink-muted">
                Every assessed asset is Fair or better.
              </p>
            </div>
          ) : (
            <ul className="divide-y divide-line">
              {attention.map((asset) => (
                <li key={asset.id}>
                  <div className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3 transition-colors hover:bg-raised">
                    <span className="tabular w-[132px] shrink-0 text-[12px] text-ink-muted">
                      {asset.code}
                    </span>
                    <span className="min-w-0 flex-1 truncate text-[13px] text-ink">
                      {asset.name}
                    </span>
                    <CriticalityMeter level={asset.criticality} />
                    <ConditionBadge condition={asset.condition} />
                    <span className="tabular w-[68px] shrink-0 text-right text-[12px] text-ink-faint">
                      {relativeAge(asset.lastInspectedOnUtc)}
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <div className="flex flex-col gap-4">
          <Panel title="Condition profile" delay={120} bodyClassName="p-4">
            {isPending ? (
              <div className="h-40 animate-pulse rounded bg-raised" />
            ) : (
              <div className="flex flex-col gap-2.5">
                {conditionSpread.map(({ condition, count }) => {
                  const share = live.length ? (count / live.length) * 100 : 0;

                  return (
                    <div key={condition} className="flex items-center gap-3">
                      <span className="w-24 shrink-0 text-[12px] text-ink-muted">
                        {CONDITION_LABEL[condition]}
                      </span>
                      <div className="h-2 min-w-0 flex-1 overflow-hidden rounded-full bg-raised">
                        <div
                          className={`h-full rounded-full ${CONDITION_BAR[condition]}`}
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

          <Panel title="Estate composition" delay={160} bodyClassName="p-4">
            {isPending ? (
              <div className="h-32 animate-pulse rounded bg-raised" />
            ) : (
              <ul className="flex flex-col gap-2">
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
      </div>
    </div>
  );
}

function Readout({
  label,
  value,
  caption,
  tone,
  pending,
}: {
  label: string;
  value: number;
  caption: string;
  tone: string;
  pending: boolean;
}) {
  return (
    <div className="px-4 py-4">
      <dt className="text-[12px] font-medium text-ink-muted">{label}</dt>
      <dd>
        {pending ? (
          <div className="mt-1.5 h-8 w-14 animate-pulse rounded bg-raised" />
        ) : (
          <span className={`tabular mt-0.5 block text-[30px] leading-none ${tone}`}>{value}</span>
        )}
        <span className="mt-1.5 block text-[11px] text-ink-faint">{caption}</span>
      </dd>
    </div>
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
