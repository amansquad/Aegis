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
