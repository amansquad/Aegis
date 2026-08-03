"use client";

import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { AuthenticatedUser } from "./types";

/**
 * Client state only.
 *
 * The split is strict and worth stating: TanStack Query owns anything that came from the server,
 * Zustand owns anything that did not. Copying fetched data into a store is the single most common
 * source of stale dashboards, because the store then has to be invalidated by hand and eventually
 * is not.
 */

interface SessionState {
  token: string | null;
  user: AuthenticatedUser | null;
  signIn: (token: string, user: AuthenticatedUser) => void;
  signOut: () => void;
  hasPermission: (permission: string) => boolean;
}

export const useSession = create<SessionState>()(
  persist(
    (set, get) => ({
      token: null,
      user: null,
      signIn: (token, user) => set({ token, user }),
      signOut: () => set({ token: null, user: null }),

      // Client-side permission checks hide controls the user cannot use. They are a courtesy and
      // never enforcement — the server re-checks every permission on every request, because
      // anything the browser decides is a decision an attacker also controls.
      hasPermission: (permission) => get().user?.permissions.includes(permission) ?? false,
    }),
    { name: "aegis.session" },
  ),
);

export type Theme = "dark" | "light";

interface ThemeState {
  theme: Theme;
  toggle: () => void;
  apply: (theme: Theme) => void;
}

/**
 * Theme lives outside the persist middleware's hydration path.
 *
 * An inline script in the document head sets `data-theme` before first paint, and this store is
 * initialised from the same attribute. Both read one source, so React's first render agrees with
 * the DOM and there is no flash and no hydration mismatch.
 */
export const useTheme = create<ThemeState>((set) => ({
  theme: "dark",
  apply: (theme) => {
    document.documentElement.setAttribute("data-theme", theme);
    try {
      localStorage.setItem("aegis.theme", theme);
    } catch {
      // Private browsing, or storage disabled. The theme still applies for this session.
    }
    set({ theme });
  },
  toggle: () =>
    set((state) => {
      const next: Theme = state.theme === "dark" ? "light" : "dark";
      document.documentElement.setAttribute("data-theme", next);
      try {
        localStorage.setItem("aegis.theme", next);
      } catch {
        // As above.
      }
      return { theme: next };
    }),
}));

/** Runs in the document head before paint. Kept in one place so the store and the script agree. */
export const THEME_BOOTSTRAP_SCRIPT = `(function(){try{var t=localStorage.getItem("aegis.theme");if(t)document.documentElement.setAttribute("data-theme",t)}catch(e){}})()`;
