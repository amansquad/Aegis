/**
 * Contracts mirroring the Aegis API.
 *
 * Hand-written rather than generated, deliberately: this is the seam where a backend change
 * should break the build loudly. A generated client regenerated on every fetch would absorb a
 * removed field silently and surface it as `undefined` in the UI weeks later.
 */

export type AssetType =
  | "Pipe"
  | "Pump"
  | "Valve"
  | "Hydrant"
  | "Tank"
  | "TreatmentPlant"
  | "Transformer"
  | "Substation"
  | "PowerLine"
  | "StreetLight"
  | "Road"
  | "Bridge"
  | "Drain"
  | "Sensor"
  | "Site"
  | "Other";

export type AssetStatus =
  | "Planned"
  | "Operational"
  | "UnderMaintenance"
  | "Faulted"
  | "Decommissioned";

export type AssetCondition = "Unknown" | "VeryGood" | "Good" | "Fair" | "Poor" | "VeryPoor";

export type AssetCriticality = "Low" | "Medium" | "High" | "Critical";

export interface Asset {
  id: string;
  code: string;
  name: string;
  type: AssetType;
  status: AssetStatus;
  condition: AssetCondition;
  criticality: AssetCriticality;
  latitude: number | null;
  longitude: number | null;
  parentAssetId: string | null;
  installedOn: string | null;
  lastInspectedOnUtc: string | null;
  createdOnUtc: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface AuthenticatedUser {
  id: string;
  email: string;
  displayName: string;
  organizationId: string;
  organizationName: string;
  roles: string[];
  permissions: string[];
}

export interface AuthenticationResult {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresOnUtc: string;
  tokenType: string;
  user: AuthenticatedUser;
}

export interface AssetFilters {
  searchTerm?: string;
  type?: AssetType;
  status?: AssetStatus;
  condition?: AssetCondition;
  criticality?: AssetCriticality;
  excludeDecommissioned?: boolean;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: "Ascending" | "Descending";
}

/** Ordered worst-first, which is the order an operator triages in. */
export const CONDITION_ORDER: AssetCondition[] = [
  "VeryPoor",
  "Poor",
  "Fair",
  "Good",
  "VeryGood",
  "Unknown",
];

export const CONDITION_LABEL: Record<AssetCondition, string> = {
  Unknown: "Not assessed",
  VeryGood: "Very good",
  Good: "Good",
  Fair: "Fair",
  Poor: "Poor",
  VeryPoor: "Very poor",
};

export const STATUS_LABEL: Record<AssetStatus, string> = {
  Planned: "Planned",
  Operational: "In service",
  UnderMaintenance: "Maintenance",
  Faulted: "Faulted",
  Decommissioned: "Retired",
};

export const TYPE_LABEL: Record<AssetType, string> = {
  Pipe: "Pipe",
  Pump: "Pump",
  Valve: "Valve",
  Hydrant: "Hydrant",
  Tank: "Tank",
  TreatmentPlant: "Treatment plant",
  Transformer: "Transformer",
  Substation: "Substation",
  PowerLine: "Power line",
  StreetLight: "Street light",
  Road: "Road",
  Bridge: "Bridge",
  Drain: "Drain",
  Sensor: "Sensor",
  Site: "Site",
  Other: "Other",
};

/* ------------------------------------------------------------------ *
 * Incidents
 * ------------------------------------------------------------------ */

export type IncidentCategory =
  | "Leak"
  | "SupplyLoss"
  | "WaterQuality"
  | "PressureProblem"
  | "Blockage"
  | "StructuralDamage"
  | "PowerFault"
  | "RoadDefect"
  | "Other";

export type IncidentSeverity = "Low" | "Moderate" | "High" | "Critical";

export type IncidentStatus =
  | "Reported"
  | "Triaged"
  | "InProgress"
  | "Resolved"
  | "Closed"
  | "Duplicate"
  | "Rejected";

export type ClassificationMethod = "Manual" | "Model" | "Heuristic";

export interface IncidentListItem {
  id: string;
  reference: string;
  summary: string;
  category: IncidentCategory;
  severity: IncidentSeverity;
  status: IncidentStatus;
  publicSafetyRisk: boolean;
  requiresReview: boolean;
  classifiedBy: ClassificationMethod;
  confidence: number | null;
  locationHint: string | null;
  latitude: number | null;
  longitude: number | null;
  assetId: string | null;
  reportedOnUtc: string;
  resolvedOnUtc: string | null;
}

export interface IncidentFilters {
  searchTerm?: string;
  status?: IncidentStatus;
  category?: IncidentCategory;
  severity?: IncidentSeverity;
  assetId?: string;
  openOnly?: boolean;
  awaitingTriageOnly?: boolean;
  safetyRiskOnly?: boolean;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: "Ascending" | "Descending";
}

export interface ReportIncidentInput {
  reportText: string;
  latitude?: number | null;
  longitude?: number | null;
  reporterName?: string | null;
  reporterContact?: string | null;
}

export interface ReportIncidentResult {
  incidentId: string;
  reference: string;
  category: IncidentCategory;
  severity: IncidentSeverity;
  summary: string;
  requiresReview: boolean;
  classifiedBy: ClassificationMethod;
  confidence: number | null;
  matchedAssetCode: string | null;
  possibleDuplicateOf: string | null;
}

export interface TriageIncidentInput {
  category: IncidentCategory;
  severity: IncidentSeverity;
  summary?: string | null;
  assetId?: string | null;
}

/** Ordered worst-first, the order a triage queue is actually read in. */
export const SEVERITY_ORDER: IncidentSeverity[] = ["Critical", "High", "Moderate", "Low"];

export const SEVERITY_LABEL: Record<IncidentSeverity, string> = {
  Critical: "Critical",
  High: "High",
  Moderate: "Moderate",
  Low: "Low",
};

export const INCIDENT_STATUS_LABEL: Record<IncidentStatus, string> = {
  Reported: "Awaiting triage",
  Triaged: "Triaged",
  InProgress: "In progress",
  Resolved: "Resolved",
  Closed: "Closed",
  Duplicate: "Duplicate",
  Rejected: "Rejected",
};

export const CATEGORY_LABEL: Record<IncidentCategory, string> = {
  Leak: "Leak",
  SupplyLoss: "Supply loss",
  WaterQuality: "Water quality",
  PressureProblem: "Pressure problem",
  Blockage: "Blockage",
  StructuralDamage: "Structural damage",
  PowerFault: "Power fault",
  RoadDefect: "Road defect",
  Other: "Other",
};
