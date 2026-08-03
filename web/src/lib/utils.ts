import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** Merges class names, letting a later Tailwind utility win over an earlier conflicting one. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/**
 * Formats an instant as a coarse relative age.
 *
 * Coarse on purpose. An operator scanning a list needs "3 days" to judge whether an inspection is
 * stale; "3 days, 4 hours and 12 minutes" is noise that makes the column harder to scan and
 * implies a precision the underlying date does not have.
 */
export function relativeAge(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return "—";

  const elapsed = now - new Date(iso).getTime();
  if (Number.isNaN(elapsed)) return "—";

  const minutes = Math.floor(elapsed / 60_000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  if (days < 31) return `${days}d ago`;

  const months = Math.floor(days / 30.44);
  if (months < 24) return `${months}mo ago`;

  return `${Math.floor(days / 365.25)}y ago`;
}

/** Formats a coordinate pair to six decimals — roughly 10cm, past which the GPS is lying. */
export function formatCoordinate(lat: number | null, lon: number | null): string {
  if (lat === null || lon === null) return "No position recorded";
  return `${lat.toFixed(6)}, ${lon.toFixed(6)}`;
}
