"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import {
  Activity,
  AlertTriangle,
  ClipboardList,
  LayoutDashboard,
  LogOut,
  Menu,
  Moon,
  Sun,
  Wrench,
  X,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useSession, useTheme, type Theme } from "@/lib/store";
import { IS_DEMO } from "@/lib/api";

const NAV = [
  { href: "/dashboard", label: "Operations", icon: LayoutDashboard },
  { href: "/assets", label: "Asset registry", icon: Activity },
  { href: "/incidents", label: "Incidents", icon: AlertTriangle },
  { href: "/work-orders", label: "Work orders", icon: ClipboardList, pending: true },
  { href: "/maintenance", label: "Maintenance", icon: Wrench, pending: true },
] as const;

function ThemeToggle() {
  const { theme, toggle, apply } = useTheme();

  // The inline head script has already set the attribute. This syncs the store to it on mount so
  // React's idea of the theme and the DOM's never diverge.
  useEffect(() => {
    const current = (document.documentElement.getAttribute("data-theme") as Theme) ?? "dark";
    if (current !== theme) apply(current);
    // Intentionally mount-only: this reconciles the initial paint, not later toggles.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <button
      onClick={toggle}
      className="rounded-[--radius-control] p-2 text-ink-muted transition-colors hover:bg-raised hover:text-ink"
      aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
    >
      {theme === "dark" ? <Sun size={16} /> : <Moon size={16} />}
    </button>
  );
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const { user, signOut } = useSession();
  const [navOpen, setNavOpen] = useState(false);

  // Guard rather than middleware: the session lives in the browser, so the server has nothing to
  // check. The real enforcement is the API rejecting a request with no bearer token.
  useEffect(() => {
    if (!user) router.replace("/login");
  }, [user, router]);

  useEffect(() => {
    setNavOpen(false);
  }, [pathname]);

  if (!user) return null;

  return (
    <div className="flex min-h-dvh flex-col bg-void">
      <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center gap-3 border-b border-line bg-surface/90 px-4 backdrop-blur-md">
        <button
          onClick={() => setNavOpen((open) => !open)}
          className="rounded-[--radius-control] p-2 text-ink-muted hover:bg-raised hover:text-ink lg:hidden"
          aria-label={navOpen ? "Close navigation" : "Open navigation"}
          aria-expanded={navOpen}
        >
          {navOpen ? <X size={18} /> : <Menu size={18} />}
        </button>

        <Link href="/dashboard" className="flex items-center gap-2.5">
          {/* An authored mark, not an emoji: a shield notch over a signal pulse. */}
          <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden className="shrink-0">
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
          <span className="text-[14px] font-semibold tracking-[-0.02em] text-ink">AEGIS</span>
        </Link>

        <span aria-hidden className="hidden h-4 w-px bg-line sm:block" />
        <span className="hidden truncate text-[13px] text-ink-muted sm:block">
          {user.organizationName}
        </span>

        {IS_DEMO && (
          <span className="ml-auto hidden shrink-0 items-center gap-1.5 rounded-full bg-watch-dim px-2.5 py-1 text-[11px] font-medium text-watch sm:inline-flex">
            <span aria-hidden className="size-1.5 rounded-full bg-watch" />
            Demo data
          </span>
        )}

        <div className={cn("flex items-center gap-1", !IS_DEMO && "ml-auto")}>
          <ThemeToggle />
          <button
            onClick={() => {
              signOut();
              router.replace("/login");
            }}
            className="rounded-[--radius-control] p-2 text-ink-muted transition-colors hover:bg-raised hover:text-ink"
            aria-label="Sign out"
            title={`Sign out — ${user.email}`}
          >
            <LogOut size={16} />
          </button>
        </div>
      </header>

      <div className="flex flex-1">
        <nav
          className={cn(
            "fixed inset-y-14 left-0 z-20 w-60 shrink-0 border-r border-line bg-surface p-3",
            "transition-transform duration-200 lg:static lg:inset-auto lg:translate-x-0",
            navOpen ? "translate-x-0" : "-translate-x-full",
          )}
          aria-label="Sections"
        >
          <ul className="flex flex-col gap-0.5">
            {NAV.map(({ href, label, icon: Icon, ...rest }) => {
              const pending = "pending" in rest && rest.pending;
              const active = pathname === href || pathname.startsWith(`${href}/`);

              // Sections that do not exist yet are shown but disabled, with the reason stated.
              // Hiding them would make the product look smaller than it is; letting them 404
              // would waste the user's click and their trust.
              if (pending) {
                return (
                  <li key={href}>
                    <span
                      className="flex cursor-not-allowed items-center gap-2.5 rounded-[--radius-control] px-3 py-2 text-[13px] text-ink-faint"
                      title="Not built yet"
                    >
                      <Icon size={16} aria-hidden />
                      {label}
                      <span className="ml-auto text-[10px] uppercase tracking-wider">Soon</span>
                    </span>
                  </li>
                );
              }

              return (
                <li key={href}>
                  <Link
                    href={href}
                    aria-current={active ? "page" : undefined}
                    className={cn(
                      "flex items-center gap-2.5 rounded-[--radius-control] px-3 py-2 text-[13px] transition-colors",
                      active
                        ? "bg-raised font-medium text-ink"
                        : "text-ink-muted hover:bg-raised hover:text-ink",
                    )}
                  >
                    <Icon size={16} aria-hidden className={active ? "text-signal" : undefined} />
                    {label}
                  </Link>
                </li>
              );
            })}
          </ul>

          <div className="mt-4 border-t border-line pt-3">
            <p className="px-3 text-[12px] font-medium text-ink">{user.displayName}</p>
            <p className="truncate px-3 text-[11px] text-ink-faint">{user.email}</p>
            <p className="mt-1 px-3 text-[11px] text-ink-faint">{user.roles.join(", ")}</p>
          </div>
        </nav>

        {navOpen && (
          <button
            className="fixed inset-0 top-14 z-10 bg-void/70 lg:hidden"
            onClick={() => setNavOpen(false)}
            aria-label="Close navigation"
            tabIndex={-1}
          />
        )}

        <main className="min-w-0 flex-1 p-4 lg:p-6">{children}</main>
      </div>
    </div>
  );
}
