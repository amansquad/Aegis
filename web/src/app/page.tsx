"use client";

import Link from "next/link";
import { Activity, AlertTriangle, ClipboardList, Wrench } from "lucide-react";
import { useIsSignedIn } from "@/lib/store";
import { Button } from "@/components/ui";
import { InstallAppButton } from "@/components/install-app-button";

const MODULES = [
  {
    icon: Activity,
    title: "Asset registry",
    description: "Every pump, valve and main, with condition, criticality and inspection history.",
  },
  {
    icon: AlertTriangle,
    title: "Incidents",
    description: "Free-text reports classified automatically, resolved from the same picture as the field.",
  },
  {
    icon: ClipboardList,
    title: "Work orders",
    description: "Dispatch, assignment and completion, in one flow whether it started from a report or a plan.",
  },
  {
    icon: Wrench,
    title: "Maintenance",
    description: "Recurring service schedules that generate their own work and advance on completion.",
  },
] as const;

/**
 * The public-facing front door. A visitor who is not signed in should be told what this product
 * is before being asked for credentials — a bare login form with nothing above it answers a
 * question ("what is this?") that the previous behaviour never let anyone ask.
 */
export default function HomePage() {
  const signedIn = useIsSignedIn();
  const primaryHref = signedIn ? "/dashboard" : "/login";
  const primaryLabel = signedIn ? "Open dashboard" : "Sign in";

  return (
    <main className="flex min-h-dvh flex-col bg-void">
      <header className="flex items-center justify-between border-b border-line px-6 py-4 sm:px-10">
        <div className="flex items-center gap-2.5">
          <svg width="22" height="22" viewBox="0 0 20 20" aria-hidden className="shrink-0">
            <path
              d="M10 1.5 3 4.2v5.4c0 4 2.9 7.4 7 8.9 4.1-1.5 7-4.9 7-8.9V4.2L10 1.5Z"
              className="fill-signal/12 stroke-signal"
              strokeWidth="1.3"
            />
            <path
              d="M6.4 10.2h2l1.2-2.6 1.5 4.2 1.1-1.6h1.4"
              fill="none"
              className="stroke-signal"
              strokeWidth="1.3"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          <span className="text-[15px] font-semibold tracking-[-0.02em] text-ink">AEGIS</span>
        </div>

        <div className="flex items-center gap-2">
          <InstallAppButton />
          <Link href={primaryHref}>
            <Button variant="primary">{primaryLabel}</Button>
          </Link>
        </div>
      </header>

      <section className="relative flex flex-1 flex-col items-center justify-center overflow-hidden px-6 py-20 text-center sm:px-10">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 opacity-[0.5]"
          style={{
            backgroundImage:
              "linear-gradient(var(--color-line) 1px, transparent 1px), linear-gradient(90deg, var(--color-line) 1px, transparent 1px)",
            backgroundSize: "48px 48px",
            maskImage: "radial-gradient(ellipse 60% 55% at 50% 35%, #000 20%, transparent 75%)",
          }}
        />

        <div className="resolve relative max-w-2xl">
          <h1 className="text-4xl font-semibold leading-[1.12] tracking-[-0.035em] text-ink sm:text-5xl">
            Every asset your organisation is responsible for — in one system that knows what is
            about to fail.
          </h1>
          <p className="mx-auto mt-5 max-w-lg text-[15px] leading-relaxed text-ink-muted">
            Condition-driven maintenance, natural-language incident intake, and crews dispatched
            from the same picture the control room is looking at.
          </p>

          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Link href={primaryHref}>
              <Button variant="primary" className="px-6 py-2.5">
                {primaryLabel}
              </Button>
            </Link>
            <InstallAppButton className="px-6 py-2.5" />
          </div>
        </div>

        <dl
          className="resolve relative mt-16 grid grid-cols-3 gap-8 sm:gap-14"
          style={{ animationDelay: "120ms" }}
        >
          {[
            { value: "468", label: "assets tracked" },
            { value: "5", label: "districts" },
            { value: "24/7", label: "duty coverage" },
          ].map((stat) => (
            <div key={stat.label}>
              <dt className="tabular text-2xl text-ink">{stat.value}</dt>
              <dd className="mt-1 text-[12px] text-ink-faint">{stat.label}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="border-t border-line px-6 py-14 sm:px-10">
        <div className="mx-auto grid max-w-5xl gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {MODULES.map((module, index) => (
            <div
              key={module.title}
              style={{ animationDelay: `${160 + index * 40}ms` }}
              className="resolve rounded-[--radius-panel] border border-line bg-surface p-5 shadow-[--shadow-panel]"
            >
              <module.icon size={18} aria-hidden className="text-signal" />
              <h2 className="mt-3 text-[14px] font-semibold text-ink">{module.title}</h2>
              <p className="mt-1.5 text-[13px] leading-relaxed text-ink-muted">{module.description}</p>
            </div>
          ))}
        </div>
      </section>
    </main>
  );
}
