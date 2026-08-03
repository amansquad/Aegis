"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { api, ApiError, IS_DEMO } from "@/lib/api";
import { useSession } from "@/lib/store";
import { Button, Field, Input } from "@/components/ui";

export default function LoginPage() {
  const router = useRouter();
  const signIn = useSession((state) => state.signIn);

  const [email, setEmail] = useState(IS_DEMO ? "ada.osei@northern-water.example" : "");
  const [password, setPassword] = useState(IS_DEMO ? "demo-access" : "");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const result = await api.signIn(email, password);
      signIn(result.accessToken, result.user);
      router.replace("/dashboard");
    } catch (cause) {
      setError(
        cause instanceof ApiError
          ? cause.message
          : "Could not reach the Aegis API. Check your connection and try again.",
      );
      setBusy(false);
    }
  }

  return (
    <main className="grid min-h-dvh lg:grid-cols-[1.1fr_1fr]">
      {/*
        The left panel is the product's claim, not decoration. It exists because whoever opens
        this at 3am on a shared terminal should know within a second which system they are in.
        Hidden below lg, where the form is the only thing worth the viewport.
      */}
      <section className="relative hidden overflow-hidden border-r border-line bg-surface p-12 lg:flex lg:flex-col lg:justify-between">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 opacity-[0.5]"
          style={{
            backgroundImage:
              "linear-gradient(var(--color-line) 1px, transparent 1px), linear-gradient(90deg, var(--color-line) 1px, transparent 1px)",
            backgroundSize: "48px 48px",
            maskImage: "radial-gradient(ellipse 70% 60% at 30% 40%, #000 20%, transparent 75%)",
          }}
        />

        <div className="relative flex items-center gap-3">
          <svg width="24" height="24" viewBox="0 0 20 20" aria-hidden>
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
          <span className="text-[15px] font-semibold tracking-[-0.02em]">AEGIS</span>
        </div>

        <div className="resolve relative max-w-lg">
          <h1 className="text-4xl font-semibold leading-[1.12] tracking-[-0.035em] text-ink">
            Every pump, valve and main your organisation is responsible for — in one registry that
            knows what is about to fail.
          </h1>
          <p className="mt-5 max-w-md text-[14px] leading-relaxed text-ink-muted">
            Condition-driven maintenance, natural-language incident intake, and crews dispatched
            from the same picture the control room is looking at.
          </p>
        </div>

        <dl className="resolve relative grid grid-cols-3 gap-8" style={{ animationDelay: "120ms" }}>
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

      <section className="flex items-center justify-center p-6">
        <div className="w-full max-w-sm">
          <h2 className="text-xl font-semibold tracking-[-0.02em] text-ink">Sign in</h2>
          <p className="mt-1.5 text-[13px] text-ink-muted">
            {IS_DEMO
              ? "This deployment runs on demo data. Sign in with the details below to explore it."
              : "Use the credentials issued by your organisation's administrator."}
          </p>

          <form onSubmit={handleSubmit} className="mt-7 flex flex-col gap-4" noValidate>
            <Field label="Email address">
              <Input
                type="email"
                name="email"
                autoComplete="username"
                required
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                placeholder="you@utility.gov"
              />
            </Field>

            <Field label="Password">
              <Input
                type="password"
                name="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="••••••••••••"
              />
            </Field>

            {error && (
              <p
                role="alert"
                className="rounded-[--radius-control] bg-failed-dim px-3 py-2 text-[13px] text-failed"
              >
                {error}
              </p>
            )}

            <Button type="submit" variant="primary" loading={busy} className="mt-1 w-full py-2.5">
              {busy ? "Signing in" : "Sign in"}
            </Button>
          </form>

          <p className="mt-6 text-[12px] leading-relaxed text-ink-faint">
            {IS_DEMO
              ? "No API is configured, so this session is served from a seeded estate held in your browser. Nothing you do here leaves this device."
              : "Sessions expire after 15 minutes of inactivity and renew automatically while you work."}
          </p>
        </div>
      </section>
    </main>
  );
}
