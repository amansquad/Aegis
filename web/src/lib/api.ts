import { DEMO_ASSETS, DEMO_USER } from "./demo-data";
import type {
  Asset,
  AssetFilters,
  AuthenticationResult,
  PagedResult,
} from "./types";

/**
 * The API seam.
 *
 * One switch decides where data comes from: `NEXT_PUBLIC_API_URL`. Set it and every call goes to
 * the real Aegis API. Leave it unset and the same functions serve a seeded in-memory estate.
 *
 * The demo path is not a mock layer bolted beside the real one — it implements the identical
 * signatures, filtering, sorting and paging semantics, so the components above it cannot tell the
 * difference and no component ever branches on "are we in demo mode?". That is what stops the demo
 * from rotting into a separate, half-true version of the product.
 */

export const API_URL = process.env.NEXT_PUBLIC_API_URL?.replace(/\/$/, "") ?? "";

export const IS_DEMO = API_URL.length === 0;

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errorCode?: string;
  errors?: Record<string, string[]>;
}

async function request<T>(path: string, init: RequestInit = {}, token?: string): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  });

  if (!response.ok) {
    let problem: ProblemDetails = {};

    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      // A non-JSON error body means the failure happened before the API could shape a response —
      // a proxy timeout, a cold start. The status alone is what we have.
    }

    // Field-level validation errors are flattened into one readable line rather than dropped,
    // because "One or more validation errors occurred" tells the user nothing actionable.
    const fieldErrors = problem.errors
      ? Object.values(problem.errors).flat().join(" ")
      : "";

    throw new ApiError(
      fieldErrors || problem.detail || problem.title || `Request failed (${response.status})`,
      response.status,
      problem.errorCode,
    );
  }

  if (response.status === 204) return undefined as T;

  return (await response.json()) as T;
}

/* ------------------------------------------------------------------ *
 * Demo implementation
 * ------------------------------------------------------------------ */

/** Mirrors the server's clamping so a demo page cannot be asked for a million rows either. */
const MAX_PAGE_SIZE = 100;

function demoAssets(filters: AssetFilters): PagedResult<Asset> {
  const term = filters.searchTerm?.trim().toLowerCase();

  let rows = DEMO_ASSETS.filter((asset) => {
    if (filters.type && asset.type !== filters.type) return false;
    if (filters.status && asset.status !== filters.status) return false;
    if (filters.condition && asset.condition !== filters.condition) return false;
    if (filters.criticality && asset.criticality !== filters.criticality) return false;
    if (filters.excludeDecommissioned && asset.status === "Decommissioned") return false;

    if (term) {
      const haystack = `${asset.name} ${asset.code}`.toLowerCase();
      if (!haystack.includes(term)) return false;
    }

    return true;
  });

  const sortBy = filters.sortBy ?? "createdOnUtc";
  const descending = (filters.sortDirection ?? "Descending") === "Descending";

  rows = [...rows].sort((a, b) => {
    const left = a[sortBy as keyof Asset];
    const right = b[sortBy as keyof Asset];

    // Nulls sort last regardless of direction. An asset never inspected is not "the oldest
    // inspection"; it is a different thing, and burying it under real data hides it.
    if (left === null) return 1;
    if (right === null) return -1;

    const comparison =
      typeof left === "number" && typeof right === "number"
        ? left - right
        : String(left).localeCompare(String(right));

    return descending ? -comparison : comparison;
  });

  const pageSize = Math.min(filters.pageSize ?? 25, MAX_PAGE_SIZE);
  const page = Math.max(filters.page ?? 1, 1);
  const start = (page - 1) * pageSize;
  const items = rows.slice(start, start + pageSize);
  const totalPages = Math.ceil(rows.length / pageSize);

  return {
    items,
    page,
    pageSize,
    totalCount: rows.length,
    totalPages,
    hasPreviousPage: page > 1,
    hasNextPage: page < totalPages,
  };
}

/* ------------------------------------------------------------------ *
 * Public surface
 * ------------------------------------------------------------------ */

function toQueryString(filters: AssetFilters): string {
  const params = new URLSearchParams();

  for (const [key, value] of Object.entries(filters)) {
    if (value !== undefined && value !== null && value !== "") {
      params.set(key, String(value));
    }
  }

  return params.toString();
}

export const api = {
  async signIn(email: string, password: string): Promise<AuthenticationResult> {
    if (IS_DEMO) {
      // A deliberate pause. Without it the demo sign-in resolves in zero milliseconds, the
      // loading state never renders, and nobody ever sees whether it was designed.
      await new Promise((resolve) => setTimeout(resolve, 550));

      if (!email.trim() || !password) {
        throw new ApiError("Enter your email address and password.", 400);
      }

      return DEMO_USER;
    }

    return request<AuthenticationResult>("/api/v1/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
  },

  async listAssets(filters: AssetFilters, token?: string): Promise<PagedResult<Asset>> {
    if (IS_DEMO) {
      await new Promise((resolve) => setTimeout(resolve, 180));
      return demoAssets(filters);
    }

    return request<PagedResult<Asset>>(
      `/api/v1/assets?${toQueryString(filters)}`,
      { method: "GET" },
      token,
    );
  },
};
