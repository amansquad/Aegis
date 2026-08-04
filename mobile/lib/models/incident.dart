enum IncidentCategory {
  leak,
  supplyLoss,
  waterQuality,
  pressureProblem,
  blockage,
  structuralDamage,
  powerFault,
  roadDefect,
  other,
}

enum IncidentSeverity { low, moderate, high, critical }

enum IncidentStatus { reported, triaged, inProgress, resolved, closed, duplicate, rejected }

enum ClassificationMethod { manual, model, heuristic }

T _enumFromApiName<T extends Enum>(List<T> values, String apiName, T fallback) {
  for (final value in values) {
    final pascal = value.name[0].toUpperCase() + value.name.substring(1);
    if (pascal == apiName) return value;
  }
  return fallback;
}

String incidentCategoryToApi(IncidentCategory c) => c.name[0].toUpperCase() + c.name.substring(1);
String incidentSeverityToApi(IncidentSeverity s) => s.name[0].toUpperCase() + s.name.substring(1);

class IncidentListItem {
  const IncidentListItem({
    required this.id,
    required this.reference,
    required this.summary,
    required this.category,
    required this.severity,
    required this.status,
    required this.publicSafetyRisk,
    required this.requiresReview,
    required this.classifiedBy,
    required this.confidence,
    required this.locationHint,
    required this.latitude,
    required this.longitude,
    required this.assetId,
    required this.reportedOnUtc,
    required this.resolvedOnUtc,
  });

  final String id;
  final String reference;
  final String summary;
  final IncidentCategory category;
  final IncidentSeverity severity;
  final IncidentStatus status;
  final bool publicSafetyRisk;
  final bool requiresReview;
  final ClassificationMethod classifiedBy;
  final double? confidence;
  final String? locationHint;
  final double? latitude;
  final double? longitude;
  final String? assetId;
  final String reportedOnUtc;
  final String? resolvedOnUtc;

  bool get isOpen =>
      status == IncidentStatus.reported ||
      status == IncidentStatus.triaged ||
      status == IncidentStatus.inProgress;

  factory IncidentListItem.fromJson(Map<String, dynamic> json) => IncidentListItem(
        id: json["id"] as String,
        reference: json["reference"] as String,
        summary: json["summary"] as String,
        category: _enumFromApiName(IncidentCategory.values, json["category"] as String, IncidentCategory.other),
        severity:
            _enumFromApiName(IncidentSeverity.values, json["severity"] as String, IncidentSeverity.low),
        status: _enumFromApiName(IncidentStatus.values, json["status"] as String, IncidentStatus.reported),
        publicSafetyRisk: json["publicSafetyRisk"] as bool,
        requiresReview: json["requiresReview"] as bool,
        classifiedBy: _enumFromApiName(
            ClassificationMethod.values, json["classifiedBy"] as String, ClassificationMethod.heuristic),
        confidence: (json["confidence"] as num?)?.toDouble(),
        locationHint: json["locationHint"] as String?,
        latitude: (json["latitude"] as num?)?.toDouble(),
        longitude: (json["longitude"] as num?)?.toDouble(),
        assetId: json["assetId"] as String?,
        reportedOnUtc: json["reportedOnUtc"] as String,
        resolvedOnUtc: json["resolvedOnUtc"] as String?,
      );
}

class ReportIncidentResult {
  const ReportIncidentResult({
    required this.incidentId,
    required this.reference,
    required this.category,
    required this.severity,
    required this.summary,
    required this.requiresReview,
    required this.classifiedBy,
    required this.confidence,
    required this.matchedAssetCode,
    required this.possibleDuplicateOf,
  });

  final String incidentId;
  final String reference;
  final IncidentCategory category;
  final IncidentSeverity severity;
  final String summary;
  final bool requiresReview;
  final ClassificationMethod classifiedBy;
  final double? confidence;
  final String? matchedAssetCode;
  final String? possibleDuplicateOf;

  factory ReportIncidentResult.fromJson(Map<String, dynamic> json) => ReportIncidentResult(
        incidentId: json["incidentId"] as String,
        reference: json["reference"] as String,
        category: _enumFromApiName(IncidentCategory.values, json["category"] as String, IncidentCategory.other),
        severity:
            _enumFromApiName(IncidentSeverity.values, json["severity"] as String, IncidentSeverity.low),
        summary: json["summary"] as String,
        requiresReview: json["requiresReview"] as bool,
        classifiedBy: _enumFromApiName(
            ClassificationMethod.values, json["classifiedBy"] as String, ClassificationMethod.heuristic),
        confidence: (json["confidence"] as num?)?.toDouble(),
        matchedAssetCode: json["matchedAssetCode"] as String?,
        possibleDuplicateOf: json["possibleDuplicateOf"] as String?,
      );
}

const severityLabel = {
  IncidentSeverity.critical: "Critical",
  IncidentSeverity.high: "High",
  IncidentSeverity.moderate: "Moderate",
  IncidentSeverity.low: "Low",
};

const incidentStatusLabel = {
  IncidentStatus.reported: "Awaiting triage",
  IncidentStatus.triaged: "Triaged",
  IncidentStatus.inProgress: "In progress",
  IncidentStatus.resolved: "Resolved",
  IncidentStatus.closed: "Closed",
  IncidentStatus.duplicate: "Duplicate",
  IncidentStatus.rejected: "Rejected",
};

const categoryLabel = {
  IncidentCategory.leak: "Leak",
  IncidentCategory.supplyLoss: "Supply loss",
  IncidentCategory.waterQuality: "Water quality",
  IncidentCategory.pressureProblem: "Pressure problem",
  IncidentCategory.blockage: "Blockage",
  IncidentCategory.structuralDamage: "Structural damage",
  IncidentCategory.powerFault: "Power fault",
  IncidentCategory.roadDefect: "Road defect",
  IncidentCategory.other: "Other",
};
