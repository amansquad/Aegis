import type { IncidentCategory, IncidentSeverity } from "./types";

/**
 * A faithful port of the server's rule-based extractor
 * (`Aegis.Infrastructure.Ai.HeuristicIncidentExtractor`).
 *
 * This exists so the demo deployment can show the actual intake behaviour without a language
 * model or a server round trip. It is not a separate design: the category keywords, safety
 * phrases and severity rules below are copied from the C# source line for line, because a demo
 * that classifies differently from the real system would be demonstrating a different product.
 *
 * The same ceiling applies here as on the server: this is never confident enough to skip review.
 * Keyword matching has no understanding of negation — "no leak, just checking the hydrant" scores
 * as a leak — which is exactly why nothing it produces is ever auto-accepted.
 */

export const HEURISTIC_MAX_CONFIDENCE = 0.55;
export const AUTO_ACCEPT_THRESHOLD = 0.85;

/** Order matters: more specific and more dangerous categories are tested first. */
const CATEGORY_RULES: [IncidentCategory, string[]][] = [
  ["PowerFault", ["power cut", "no power", "electric", "sparking", "cable", "substation", "transformer"]],
  ["StructuralDamage", ["collapse", "collapsed", "sinkhole", "subsidence", "crack", "struck", "hit the", "damaged"]],
  ["WaterQuality", ["brown water", "discolour", "discolor", "smell", "smells", "taste", "cloudy", "contaminat"]],
  ["SupplyLoss", ["no water", "no supply", "lost supply", "supply is off", "nothing coming"]],
  ["Blockage", ["blocked", "blockage", "drain", "sewer", "backing up", "overflow", "gully"]],
  ["PressureProblem", ["pressure", "trickle", "weak flow", "low flow"]],
  ["Leak", ["leak", "leaking", "burst", "water coming", "flooding", "flood", "gushing", "seeping"]],
  ["RoadDefect", ["pothole", "road surface", "pavement", "street light", "streetlight", "signage"]],
];

/** Generous on purpose: a false positive costs a glance, a false negative costs more. */
const SAFETY_PHRASES = [
  "gas", "smell of gas", "electric", "electrical", "sparking", "live wire", "exposed",
  "collapse", "sinkhole", "injured", "hurt", "danger", "dangerous", "hazard",
  "flooding the", "into the house", "into my house", "basement", "cellar",
  "school", "hospital", "child", "children", "elderly", "car has", "traffic",
];

const HIGH_SEVERITY_PHRASES = [
  "burst", "gushing", "collapse", "sinkhole", "no water", "no supply", "flooding",
  "whole street", "entire street", "many houses", "hospital", "school", "urgent",
];

const LOW_SEVERITY_PHRASES = [
  "dripping", "slight", "minor", "small", "slowly", "occasionally", "cosmetic", "not urgent",
];

const CATEGORY_HUMANISED: Record<IncidentCategory, string> = {
  Leak: "Reported leak",
  SupplyLoss: "Reported loss of supply",
  WaterQuality: "Reported water quality problem",
  PressureProblem: "Reported pressure problem",
  Blockage: "Reported blockage",
  StructuralDamage: "Reported structural damage",
  PowerFault: "Reported power fault",
  RoadDefect: "Reported road defect",
  Other: "Unclassified report",
};

// Mirrors the server's [A-Z0-9\-_/]+-shaped asset code pattern.
const ASSET_CODE_PATTERN = /\b[A-Za-z]{2,4}-[A-Za-z0-9]{1,4}-\d{2,6}\b/;

// Mirrors the server's street-name pattern.
const STREET_PATTERN =
  /\b\d{0,4}\s?[A-Z][a-z]+(?:\s[A-Z][a-z]+)*\s(?:Road|Street|Lane|Avenue|Close|Way|Drive|Crescent|Hill|Gardens|Square)\b/;

export interface HeuristicClassification {
  category: IncidentCategory;
  severity: IncidentSeverity;
  summary: string;
  locationHint: string | null;
  assetCodeHint: string | null;
  publicSafetyRisk: boolean;
  confidence: number;
}

function resolveSeverity(text: string, safetyRisk: boolean): IncidentSeverity {
  // A safety risk floors severity at High — nothing a keyword matcher reads should be able to
  // file a described danger as routine.
  if (safetyRisk) {
    return HIGH_SEVERITY_PHRASES.some((phrase) => text.includes(phrase)) ? "Critical" : "High";
  }

  if (HIGH_SEVERITY_PHRASES.some((phrase) => text.includes(phrase))) return "High";
  if (LOW_SEVERITY_PHRASES.some((phrase) => text.includes(phrase))) return "Low";

  return "Moderate";
}

function buildSummary(report: string, category: IncidentCategory): string {
  const condensed = report.trim().replace(/\s+/g, " ");
  const body = condensed.length <= 180 ? condensed : `${condensed.slice(0, 177)}…`;

  return `${CATEGORY_HUMANISED[category]}: ${body}`;
}

/** Classifies a free-text report using the same rules as the server's fallback extractor. */
export function classifyReport(report: string): HeuristicClassification {
  const text = report.toLowerCase();

  const matched = CATEGORY_RULES.find(([, keywords]) =>
    keywords.some((keyword) => text.includes(keyword)),
  );

  const category: IncidentCategory = matched ? matched[0] : "Other";
  const safetyRisk = SAFETY_PHRASES.some((phrase) => text.includes(phrase));
  const severity = resolveSeverity(text, safetyRisk);

  const corroboration = matched
    ? matched[1].filter((keyword) => text.includes(keyword)).length
    : 0;

  const confidence = matched
    ? Math.min(HEURISTIC_MAX_CONFIDENCE, 0.35 + corroboration * 0.08)
    : 0.2;

  const locationMatch = STREET_PATTERN.exec(report);
  const assetMatch = ASSET_CODE_PATTERN.exec(report);

  return {
    category,
    severity,
    summary: buildSummary(report, category),
    locationHint: locationMatch ? locationMatch[0].trim() : null,
    assetCodeHint: assetMatch ? assetMatch[0].toUpperCase() : null,
    publicSafetyRisk: safetyRisk,
    confidence,
  };
}

/**
 * Great-circle distance in metres, matching `GeoCoordinate.DistanceInMetresTo` on the server.
 */
export function distanceInMetres(
  lat1: number,
  lon1: number,
  lat2: number,
  lon2: number,
): number {
  const earthRadiusMetres = 6_371_000;
  const toRadians = (deg: number) => (deg * Math.PI) / 180;

  const dLat = toRadians(lat2 - lat1);
  const dLon = toRadians(lon2 - lon1);

  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRadians(lat1)) * Math.cos(toRadians(lat2)) * Math.sin(dLon / 2) ** 2;

  return earthRadiusMetres * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}
