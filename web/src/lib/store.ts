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
  accessTokenExpiresOnUtc: string | null;
  signIn: (token: string, user: AuthenticatedUser, accessTokenExpiresOnUtc: string) => void;
  signOut: () => void;
  hasPermission: (permission: string) => boolean;
  /**
   * True once `token`/`user` are set but the access token's own expiry has passed. Persisted
   * state with no expiry check would stay "signed in" in this tab forever — the API already
   * tells the client when the token dies, so a session that outlives it is a bug in the client,
   * not a feature.
   */
  isExpired: () => boolean;
}

export const useSession = create<SessionState>()(
  persist(
    (set, get) => ({
      token: null,
      user: null,
      accessTokenExpiresOnUtc: null,
      signIn: (token, user, accessTokenExpiresOnUtc) => set({ token, user, accessTokenExpiresOnUtc }),
      signOut: () => set({ token: null, user: null, accessTokenExpiresOnUtc: null }),

      // Client-side permission checks hide controls the user cannot use. They are a courtesy and
      // never enforcement — the server re-checks every permission on every request, because
      // anything the browser decides is a decision an attacker also controls.
      hasPermission: (permission) => get().user?.permissions.includes(permission) ?? false,

      isExpired: () => {
        const { user, accessTokenExpiresOnUtc } = get();

        if (!user) return false; // No session to expire; the caller treats this as signed out either way.

        // Missing or unparseable is treated as expired, not as "not expired" — a signed-in user
        // with no valid expiry on record is exactly the state a session persisted before this
        // field existed would be in, and failing open there is the wrong direction for a fail
        // safe to point.
        const expiresAt = accessTokenExpiresOnUtc ? new Date(accessTokenExpiresOnUtc).getTime() : NaN;
        return Number.isNaN(expiresAt) || Date.now() >= expiresAt;
      },
    }),
    { name: "aegis.session" },
  ),
);

/**
 * The one thing every screen should actually ask instead of reading `user` directly: whether
 * there is a session AND it has not expired. A truthy `user` from a token that died three days
 * ago is not a signed-in visitor — it is stale localStorage the app forgot to check.
 */
export function useIsSignedIn(): boolean {
  return useSession((state) => state.user !== null && !state.isExpired());
}

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
