import { DEMO_ASSETS, DEMO_INCIDENTS, DEMO_USER } from "./demo-data";
import { classifyReport, distanceInMetres } from "./incident-classifier";
import type {
  Asset,
  AssetFilters,
  AuthenticationResult,
  IncidentFilters,
  IncidentListItem,
  PagedResult,
  ReportIncidentInput,
  ReportIncidentResult,
  TriageIncidentInput,
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

/**
 * The in-session incident store.
 *
 * Assets in demo mode are read-only, so `DEMO_ASSETS` is served straight from the seed. Incidents
 * are created and triaged from the UI, so this is a mutable copy: it starts from the same seed
 * every session and changes as reports are submitted, exactly like the real database would,
 * except that a reload returns to the baseline rather than persisting anything.
 */
let demoIncidents: IncidentListItem[] = [...DEMO_INCIDENTS];

function demoIncidentList(filters: IncidentFilters): PagedResult<IncidentListItem> {
  const term = filters.searchTerm?.trim().toLowerCase();
  const openStatuses = new Set(["Reported", "Triaged", "InProgress"]);

  let rows = demoIncidents.filter((incident) => {
    if (filters.status && incident.status !== filters.status) return false;
    if (filters.category && incident.category !== filters.category) return false;
    if (filters.severity && incident.severity !== filters.severity) return false;
    if (filters.assetId && incident.assetId !== filters.assetId) return false;
    if (filters.openOnly && !openStatuses.has(incident.status)) return false;
    if (filters.awaitingTriageOnly && incident.status !== "Reported") return false;
    if (filters.safetyRiskOnly && !incident.publicSafetyRisk) return false;

    if (term) {
      const haystack = `${incident.summary} ${incident.reference}`.toLowerCase();
      if (!haystack.includes(term)) return false;
    }

    return true;
  });

  const sortBy = filters.sortBy ?? "reportedOnUtc";
  const descending = (filters.sortDirection ?? "Descending") === "Descending";

  rows = [...rows].sort((a, b) => {
    const left = a[sortBy as keyof IncidentListItem];
    const right = b[sortBy as keyof IncidentListItem];

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

/**
 * Resolves the asset a report concerns from our own data, exactly as the server does: a quoted
 * code is tried first and is only ever a lookup, then position within 150m. Nothing the
 * classifier returned is trusted as an identity — a code that does not exist in this estate
 * simply fails to match.
 */
function demoResolveAsset(assetCodeHint: string | null, lat: number | null, lon: number | null): Asset | null {
  if (assetCodeHint) {
    const byCode = DEMO_ASSETS.find((a) => a.code === assetCodeHint);
    if (byCode) return byCode;
  }

  if (lat === null || lon === null) return null;

  const ASSET_SEARCH_RADIUS_METRES = 150;

  const candidates = DEMO_ASSETS.filter(
    (a) =>
      a.status !== "Decommissioned" &&
      a.latitude !== null &&
      a.longitude !== null &&
      distanceInMetres(lat, lon, a.latitude, a.longitude) <= ASSET_SEARCH_RADIUS_METRES,
  );

  candidates.sort(
    (a, b) =>
      distanceInMetres(lat, lon, a.latitude!, a.longitude!) -
      distanceInMetres(lat, lon, b.latitude!, b.longitude!),
  );

  return candidates[0] ?? null;
}

/**
 * Looks for a recent, nearby incident of the same category — surfaced to a dispatcher, never
 * merged automatically. Two leaks on the same street in the same hour is unusual but entirely
 * possible, so this only ever returns a candidate for a human to judge.
 */
function demoFindPossibleDuplicate(
  category: IncidentListItem["category"],
  lat: number | null,
  lon: number | null,
): string | null {
  if (lat === null || lon === null) return null;

  const DUPLICATE_WINDOW_HOURS = 12;
  const DUPLICATE_RADIUS_METRES = 250;
  const since = Date.now() - DUPLICATE_WINDOW_HOURS * 3_600_000;

  const candidate = demoIncidents
    .filter(
      (i) =>
        i.category === category &&
        i.status !== "Duplicate" &&
        i.status !== "Rejected" &&
        i.latitude !== null &&
        i.longitude !== null &&
        new Date(i.reportedOnUtc).getTime() >= since &&
        distanceInMetres(lat, lon, i.latitude, i.longitude) <= DUPLICATE_RADIUS_METRES,
    )
    .sort((a, b) => new Date(b.reportedOnUtc).getTime() - new Date(a.reportedOnUtc).getTime())[0];

  return candidate?.reference ?? null;
}

function buildDemoReference(reportedOnUtc: string): string {
  const year = new Date(reportedOnUtc).getUTCFullYear();
  const tail = Array.from({ length: 12 }, () =>
    "0123456789ABCDEF"[Math.floor(Math.random() * 16)],
  ).join("");

  return `INC-${year}-${tail}`;
}

/* ------------------------------------------------------------------ *
 * Public surface
 * ------------------------------------------------------------------ */

function toQueryString(filters: AssetFilters | IncidentFilters): string {
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

  async listIncidents(
    filters: IncidentFilters,
    token?: string,
  ): Promise<PagedResult<IncidentListItem>> {
    if (IS_DEMO) {
      await new Promise((resolve) => setTimeout(resolve, 180));
      return demoIncidentList(filters);
    }

    return request<PagedResult<IncidentListItem>>(
      `/api/v1/incidents?${toQueryString(filters)}`,
      { method: "GET" },
      token,
    );
  },

  async reportIncident(
    input: ReportIncidentInput,
    token?: string,
  ): Promise<ReportIncidentResult> {
    if (IS_DEMO) {
      // Deliberately the slowest demo call: this is standing in for a language model round trip,
      // and a form that resolves instantly would misrepresent what actually happens in production.
      await new Promise((resolve) => setTimeout(resolve, 900));

      if (input.reportText.trim().length < 10) {
        throw new ApiError("Please describe the problem in a little more detail.", 400);
      }

      const classification = classifyReport(input.reportText);
      const now = new Date().toISOString();
      const lat = input.latitude ?? null;
      const lon = input.longitude ?? null;

      const matchedAsset = demoResolveAsset(classification.assetCodeHint, lat, lon);
      const possibleDuplicateOf = demoFindPossibleDuplicate(classification.category, lat, lon);

      const incident: IncidentListItem = {
        id: `demo-incident-${crypto.randomUUID()}`,
        reference: buildDemoReference(now),
        summary: classification.summary,
        category: classification.category,
        severity: classification.severity,
        status: "Reported",
        publicSafetyRisk: classification.publicSafetyRisk,
        requiresReview: true, // The heuristic path always requires review, same as the server.
        classifiedBy: "Heuristic",
        confidence: classification.confidence,
        locationHint: classification.locationHint,
        latitude: lat,
        longitude: lon,
        assetId: matchedAsset?.id ?? null,
        reportedOnUtc: now,
        resolvedOnUtc: null,
      };

      demoIncidents = [incident, ...demoIncidents];

      return {
        incidentId: incident.id,
        reference: incident.reference,
        category: incident.category,
        severity: incident.severity,
        summary: incident.summary,
        requiresReview: incident.requiresReview,
        classifiedBy: incident.classifiedBy,
        confidence: incident.confidence,
        matchedAssetCode: matchedAsset?.code ?? null,
        possibleDuplicateOf,
      };
    }

    return request<ReportIncidentResult>("/api/v1/incidents", {
      method: "POST",
      body: JSON.stringify(input),
    }, token);
  },

  async triageIncident(
    incidentId: string,
    input: TriageIncidentInput,
    token?: string,
  ): Promise<void> {
    if (IS_DEMO) {
      await new Promise((resolve) => setTimeout(resolve, 300));

      demoIncidents = demoIncidents.map((incident) =>
        incident.id === incidentId
          ? {
              ...incident,
              category: input.category,
              severity: input.severity,
              summary: input.summary?.trim() || incident.summary,
              assetId: input.assetId ?? incident.assetId,
              status: "Triaged",
              classifiedBy: "Manual",
              requiresReview: false,
            }
          : incident,
      );

      return;
    }

    await request<void>(`/api/v1/incidents/${incidentId}/triage`, {
      method: "POST",
      body: JSON.stringify(input),
    }, token);
  },

  async resolveIncident(incidentId: string, notes: string | null, token?: string): Promise<void> {
    if (IS_DEMO) {
      await new Promise((resolve) => setTimeout(resolve, 300));

      demoIncidents = demoIncidents.map((incident) =>
        incident.id === incidentId
          ? { ...incident, status: "Resolved", resolvedOnUtc: new Date().toISOString() }
          : incident,
      );

      return;
    }

    await request<void>(`/api/v1/incidents/${incidentId}/resolve`, {
      method: "POST",
      body: JSON.stringify({ notes }),
    }, token);
  },
};
