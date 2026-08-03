import type {
  Asset,
  AssetCondition,
  AssetCriticality,
  AssetStatus,
  AssetType,
  AuthenticationResult,
} from "./types";

/**
 * A fictional water utility, used when no API is configured.
 *
 * Real coordinates over central London, real-shaped asset codes, and a condition distribution
 * skewed the way a genuine registry is: mostly fine, a long tail of ageing kit, and a handful of
 * things that need attention today. A demo seeded with uniformly "Good" assets shows an interface
 * that never has to prove it can communicate urgency, which is precisely what this one is for.
 *
 * Generated deterministically from a fixed seed so that every visitor, screenshot and review sees
 * the same estate.
 */

const ORGANIZATION = "Northern Water";

/** A tiny deterministic PRNG. Math.random would give every reload a different estate. */
function seeded(seed: number) {
  let state = seed >>> 0;
  return () => {
    state = (state * 1_664_525 + 1_013_904_223) >>> 0;
    return state / 0x100000000;
  };
}

const random = seeded(20260803);

function pick<T>(values: readonly T[]): T {
  return values[Math.floor(random() * values.length)];
}

/** Weighted pick, so the estate has a realistic shape rather than a uniform one. */
function weighted<T extends string>(weights: Record<T, number>): T {
  const entries = Object.entries(weights) as [T, number][];
  const total = entries.reduce((sum, [, weight]) => sum + weight, 0);
  let roll = random() * total;

  for (const [value, weight] of entries) {
    roll -= weight;
    if (roll <= 0) return value;
  }

  return entries[entries.length - 1][0];
}

const DISTRICTS = [
  { code: "NW", name: "Northgate", lat: 51.5341, lon: -0.1352 },
  { code: "CE", name: "Central", lat: 51.5136, lon: -0.1123 },
  { code: "SE", name: "Southbank", lat: 51.5045, lon: -0.0865 },
  { code: "WE", name: "Westferry", lat: 51.5121, lon: -0.1755 },
  { code: "EA", name: "Eastvale", lat: 51.5228, lon: -0.0553 },
] as const;

const TYPE_WEIGHTS: Record<AssetType, number> = {
  Hydrant: 22,
  Valve: 20,
  Pipe: 18,
  Pump: 9,
  Drain: 8,
  Sensor: 6,
  Tank: 4,
  StreetLight: 4,
  Site: 3,
  TreatmentPlant: 1,
  Transformer: 2,
  Substation: 1,
  PowerLine: 1,
  Road: 0,
  Bridge: 0,
  Other: 1,
};

const CONDITION_WEIGHTS: Record<AssetCondition, number> = {
  VeryGood: 18,
  Good: 34,
  Fair: 24,
  Poor: 11,
  VeryPoor: 4,
  Unknown: 9,
};

const STATUS_WEIGHTS: Record<AssetStatus, number> = {
  Operational: 82,
  UnderMaintenance: 6,
  Faulted: 4,
  Planned: 5,
  Decommissioned: 3,
};

const CRITICALITY_WEIGHTS: Record<AssetCriticality, number> = {
  Low: 30,
  Medium: 42,
  High: 21,
  Critical: 7,
};

const TYPE_PREFIX: Record<AssetType, string> = {
  Pipe: "PIP",
  Pump: "PMP",
  Valve: "VLV",
  Hydrant: "HYD",
  Tank: "TNK",
  TreatmentPlant: "TRT",
  Transformer: "TFR",
  Substation: "SUB",
  PowerLine: "PWL",
  StreetLight: "STL",
  Road: "RDS",
  Bridge: "BRG",
  Drain: "DRN",
  Sensor: "SNS",
  Site: "STE",
  Other: "GEN",
};

const NAME_SUFFIX: Partial<Record<AssetType, string[]>> = {
  Pump: ["duty pump", "standby pump", "booster set", "transfer pump"],
  Valve: ["isolation valve", "pressure-reducing valve", "air valve", "washout valve"],
  Hydrant: ["hydrant", "pillar hydrant", "underground hydrant"],
  Pipe: ["distribution main", "trunk main", "service connection", "rising main"],
  Tank: ["service reservoir", "break tank", "storage tank"],
  Drain: ["gully", "manhole", "surface drain"],
  Sensor: ["pressure logger", "flow meter", "level sensor", "turbidity probe"],
  Site: ["pumping station", "depot", "control site"],
};

function daysAgo(days: number): string {
  return new Date(Date.now() - days * 86_400_000).toISOString();
}

function buildAssets(count: number): Asset[] {
  const assets: Asset[] = [];

  for (let index = 0; index < count; index++) {
    const type = weighted(TYPE_WEIGHTS);
    const district = DISTRICTS[index % DISTRICTS.length];
    const condition = weighted(CONDITION_WEIGHTS);
    const status = weighted(STATUS_WEIGHTS);

    // Scattered around the district centre, roughly a 2 km spread.
    const latitude = district.lat + (random() - 0.5) * 0.028;
    const longitude = district.lon + (random() - 0.5) * 0.044;

    // A realistic registry has gaps. About one asset in fourteen was never surveyed, which is
    // what makes the "no position recorded" state worth designing rather than hypothetical.
    const surveyed = random() > 0.07;

    const installedYearsAgo = Math.floor(random() * 48) + 1;
    const inspected = condition === "Unknown" ? null : daysAgo(Math.floor(random() * 900));

    const suffixes = NAME_SUFFIX[type] ?? ["asset"];

    assets.push({
      id: `demo-${index.toString().padStart(4, "0")}`,
      code: `${TYPE_PREFIX[type]}-${district.code}-${(index + 17).toString().padStart(4, "0")}`,
      name: `${district.name} ${pick(suffixes)}`,
      type,
      status,
      condition,
      criticality: weighted(CRITICALITY_WEIGHTS),
      latitude: surveyed ? Number(latitude.toFixed(6)) : null,
      longitude: surveyed ? Number(longitude.toFixed(6)) : null,
      parentAssetId: null,
      installedOn: new Date(
        Date.now() - installedYearsAgo * 365.25 * 86_400_000,
      )
        .toISOString()
        .slice(0, 10),
      lastInspectedOnUtc: inspected,
      createdOnUtc: daysAgo(Math.floor(random() * 1200) + 30),
    });
  }

  return assets;
}

export const DEMO_ASSETS: Asset[] = buildAssets(468);

export const DEMO_USER: AuthenticationResult = {
  accessToken: "demo",
  refreshToken: "demo",
  accessTokenExpiresOnUtc: new Date(Date.now() + 3_600_000).toISOString(),
  tokenType: "Bearer",
  user: {
    id: "demo-user",
    email: "ada.osei@northern-water.example",
    displayName: "Ada Osei",
    organizationId: "demo-org",
    organizationName: ORGANIZATION,
    roles: ["Administrator"],
    permissions: [
      "assets.view",
      "assets.create",
      "assets.update",
      "assets.decommission",
      "assets.export",
      "users.view",
      "incidents.view",
      "workorders.view",
      "analytics.operational.view",
      "analytics.executive.view",
    ],
  },
};
