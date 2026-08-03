import type {
  Asset,
  AssetCondition,
  AssetCriticality,
  AssetStatus,
  AssetType,
  AuthenticationResult,
  ClassificationMethod,
  IncidentListItem,
  IncidentStatus,
} from "./types";
import { classifyReport } from "./incident-classifier";

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

/* ------------------------------------------------------------------ *
 * Incidents
 * ------------------------------------------------------------------ */

/**
 * Sample reports, written the way the public actually writes them: run-on sentences, no
 * category labels, occasional venting. Each is classified by the same heuristic the real
 * fallback path uses, so the seeded queue shows exactly what a fresh report would produce.
 */
const REPORT_SAMPLES = [
  "There is water gushing up through the pavement outside number 14, it's been going for an hour and now flooding into next door's driveway.",
  "No water at all this morning, whole street seems affected, my elderly neighbour needs it for her dialysis machine.",
  "Water coming out of the tap is brown and smells strange, been like this since yesterday, worried about giving it to the kids.",
  "Small drip from the hydrant on the corner, not urgent, probably just needs a washer.",
  "The drain outside the school is completely blocked and backing up onto the playground.",
  "Pressure has been really weak for two days now, barely a trickle from the kitchen tap.",
  "Strong smell of gas near the pumping station on Northgate Road, also seeing a burst pipe nearby.",
  "The road has collapsed into a sinkhole and a car has nearly gone into it, this is extremely dangerous.",
  "Exposed live wire hanging from the substation fence after last night's storm, please send someone urgently.",
  "Pothole in the road surface on Central Avenue, quite deep, damaged my tyre.",
  "Street light out for the third week running outside 22 Eastvale Close.",
  "Slight leak from the valve, honestly not urgent, just thought I'd mention it.",
  "Sewer overflow behind the shops, smells terrible and flies everywhere.",
  "Power cut across most of Westferry since 6pm, several houses affected.",
  "Water main has burst on Southbank Hill, gushing across the whole street and into the school playground.",
  "Cracked pipe visible where the road contractors were working last week, slowly seeping.",
  "Transformer sparking on the pole near the substation, quite alarming to watch.",
  "The cellar of my house is slowly filling with water, I think it's from the main outside.",
  "Just a small pothole, not urgent, cosmetic really.",
  "No supply since first thing this morning, nothing coming out of any tap in the house.",
  "Discoloured water again, this keeps happening every few months.",
  "Gully blocked outside the hospital entrance, water backing up during the rain this morning.",
  "Minor drip noticed under the stopcock, occasionally, not a big deal.",
  "Whole street flooding after what looks like a burst main near the junction, urgent please.",
  "Hit a large pothole in the road surface and blew a tyre, road surface is in poor state.",
  "Street light flickering on and off near the crossing, could be a hazard for traffic at night.",
];

const INCIDENT_STATUS_WEIGHTS: Record<IncidentStatus, number> = {
  Reported: 30,
  Triaged: 22,
  InProgress: 14,
  Resolved: 20,
  Closed: 8,
  Duplicate: 3,
  Rejected: 3,
};

function hoursAgo(hours: number): string {
  return new Date(Date.now() - hours * 3_600_000).toISOString();
}

function buildIncidentReference(index: number, reportedOnUtc: string): string {
  const year = new Date(reportedOnUtc).getUTCFullYear();
  // Deterministic 12-hex tail rather than a counter, matching why the server derives references
  // from the identifier's random tail rather than a per-tenant sequence.
  const tail = Math.floor(random() * 0xffffffffffff)
    .toString(16)
    .padStart(12, "0")
    .toUpperCase();

  return `INC-${year}-${tail}${index}`.slice(0, 20);
}

function buildIncidents(count: number): IncidentListItem[] {
  const incidents: IncidentListItem[] = [];

  for (let index = 0; index < count; index++) {
    const reportText = REPORT_SAMPLES[index % REPORT_SAMPLES.length];
    const classification = classifyReport(reportText);
    const district = DISTRICTS[(index * 3) % DISTRICTS.length];

    const ageHours = 1 + Math.floor(random() * 900);
    const reportedOnUtc = hoursAgo(ageHours);

    const latitude = district.lat + (random() - 0.5) * 0.03;
    const longitude = district.lon + (random() - 0.5) * 0.045;
    const hasPosition = random() > 0.1;

    const status = weighted(INCIDENT_STATUS_WEIGHTS);
    const isOpen = status === "Reported" || status === "Triaged" || status === "InProgress";

    // A minority were classified by a live model in production, at high confidence and already
    // clear of review — this is what the queue looks like once OpenRouter is configured.
    const classifiedByModel = random() > 0.72;
    const classifiedBy: ClassificationMethod = classifiedByModel ? "Model" : "Heuristic";
    const confidence = classifiedByModel
      ? Math.min(0.99, 0.86 + random() * 0.13)
      : classification.confidence;

    const requiresReview =
      classification.publicSafetyRisk || !classifiedByModel || confidence < 0.85;

    const resolvedOnUtc =
      status === "Resolved" || status === "Closed"
        ? hoursAgo(Math.max(ageHours - Math.floor(random() * ageHours * 0.6), 1))
        : null;

    // Roughly a third of positioned incidents are close enough to a real asset to have been
    // linked, mirroring the proximity match the server performs on report.
    const nearbyAsset =
      hasPosition && random() > 0.55
        ? DEMO_ASSETS.filter(
            (a) => a.latitude !== null && Math.abs(a.latitude - latitude) < 0.01,
          )[0]
        : undefined;

    incidents.push({
      id: `demo-incident-${index.toString().padStart(4, "0")}`,
      reference: buildIncidentReference(index, reportedOnUtc),
      summary: classification.summary,
      category: classification.category,
      severity: classification.severity,
      status,
      publicSafetyRisk: classification.publicSafetyRisk,
      requiresReview: isOpen && requiresReview,
      classifiedBy,
      confidence: Number(confidence.toFixed(2)),
      locationHint: classification.locationHint,
      latitude: hasPosition ? Number(latitude.toFixed(6)) : null,
      longitude: hasPosition ? Number(longitude.toFixed(6)) : null,
      assetId: nearbyAsset?.id ?? null,
      reportedOnUtc,
      resolvedOnUtc,
    });
  }

  return incidents;
}

/**
 * Seeded once at module load. Mutated in place by the demo API layer as reports are submitted
 * and triaged during a session — held in memory only, exactly like the rest of the demo estate,
 * so a reload returns to this baseline rather than persisting a visitor's changes.
 */
export const DEMO_INCIDENTS: IncidentListItem[] = buildIncidents(46);

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
