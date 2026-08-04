"use client";

import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * Counts a number up from zero to its target on mount or whenever the target changes.
 *
 * Skips the animation entirely under `prefers-reduced-motion`, checked directly here rather than
 * relying on the global CSS rule that collapses animation durations — that rule covers CSS
 * animations and transitions, not a value driven by `requestAnimationFrame`, so this hook has to
 * make its own accommodation for the same preference.
 */
function useCountUp(target: number, durationMs = 900): number {
  // Read once, lazily, rather than in an effect: reduced motion is a standing preference, not
  // something that needs its own render-triggering sync step, and the animation effect below can
  // simply not run at all when it is set — no separate "snap to target" branch required.
  const [reduceMotion] = useState(
    () => typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );

  const [value, setValue] = useState(target);
  const previous = useRef(target);

  useEffect(() => {
    if (reduceMotion) return;

    const from = previous.current;
    const delta = target - from;

    if (delta === 0) return;

    let frame: number;
    const start = performance.now();

    const tick = (now: number) => {
      const progress = Math.min((now - start) / durationMs, 1);
      // Ease-out cubic: fast start, settles rather than ticking to a hard stop.
      const eased = 1 - (1 - progress) ** 3;

      setValue(Math.round(from + delta * eased));

      if (progress < 1) {
        frame = requestAnimationFrame(tick);
      } else {
        previous.current = target;
      }
    };

    frame = requestAnimationFrame(tick);

    return () => cancelAnimationFrame(frame);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target]);

  return reduceMotion ? target : value;
}

export function CountUp({ value, className }: { value: number; className?: string }) {
  const displayed = useCountUp(value);

  return <span className={cn("tabular", className)}>{displayed.toLocaleString()}</span>;
}

/**
 * A single-metric radial gauge — the fleet health score reduced to one glance rather than a bar
 * list. The arc sweeps in on mount instead of appearing at full length, the one place on this
 * page a value's *arrival* is worth watching rather than just its resting state.
 */
export function RadialGauge({
  percent,
  label,
  tone,
}: {
  percent: number;
  label: string;
  tone: "nominal" | "watch" | "degraded" | "failed";
}) {
  const clamped = Math.max(0, Math.min(100, percent));
  const animated = useCountUp(Math.round(clamped));

  const size = 168;
  const stroke = 12;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  // Three-quarter sweep (270°), leaving a gap at the bottom so it reads as a gauge, not a ring.
  const arcFraction = 0.75;
  const arcLength = circumference * arcFraction;
  const filled = (animated / 100) * arcLength;

  const toneColor = `var(--color-${tone})`;

  return (
    <div className="flex flex-col items-center">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={label}>
        <g transform={`rotate(135 ${size / 2} ${size / 2})`}>
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            stroke="var(--color-line)"
            strokeWidth={stroke}
            strokeLinecap="round"
            strokeDasharray={`${arcLength} ${circumference}`}
          />
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            stroke={toneColor}
            strokeWidth={stroke}
            strokeLinecap="round"
            strokeDasharray={`${filled} ${circumference}`}
            style={{ transition: "stroke-dasharray 0.9s var(--ease-out-expo)" }}
          />
        </g>
        <text
          x="50%"
          y="48%"
          textAnchor="middle"
          dominantBaseline="middle"
          className="tabular"
          style={{ fontSize: 34, fontWeight: 600, fill: "var(--color-ink)" }}
        >
          {animated}%
        </text>
        <text
          x="50%"
          y="65%"
          textAnchor="middle"
          dominantBaseline="middle"
          style={{ fontSize: 11, fill: "var(--color-ink-faint)" }}
        >
          {label}
        </text>
      </svg>
    </div>
  );
}
