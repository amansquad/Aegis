enum AssetType {
  pipe,
  pump,
  valve,
  hydrant,
  tank,
  treatmentPlant,
  transformer,
  substation,
  powerLine,
  streetLight,
  road,
  bridge,
  drain,
  sensor,
  site,
  other,
}

enum AssetStatus { planned, operational, underMaintenance, faulted, decommissioned }

enum AssetCondition { unknown, veryGood, good, fair, poor, veryPoor }

enum AssetCriticality { low, medium, high, critical }

/// Enum values round-trip against the API's PascalCase JSON string, not Dart's camelCase member
/// names -- `AssetType.treatmentPlant` must parse `"TreatmentPlant"`, so parsing is done against
/// an explicit name table rather than `.byName`, which would require the API's own casing.
T _enumFromApi<T extends Enum>(List<T> values, String apiName, T fallback) {
  for (final value in values) {
    if (_pascalCase(value.name) == apiName) return value;
  }
  return fallback;
}

String _pascalCase(String camelCase) =>
    camelCase.isEmpty ? camelCase : camelCase[0].toUpperCase() + camelCase.substring(1);

String assetTypeToApi(AssetType type) => _pascalCase(type.name);
String assetStatusToApi(AssetStatus status) => _pascalCase(status.name);

class Asset {
  const Asset({
    required this.id,
    required this.code,
    required this.name,
    required this.type,
    required this.status,
    required this.condition,
    required this.criticality,
    required this.latitude,
    required this.longitude,
    required this.parentAssetId,
    required this.installedOn,
    required this.lastInspectedOnUtc,
    required this.createdOnUtc,
  });

  final String id;
  final String code;
  final String name;
  final AssetType type;
  final AssetStatus status;
  final AssetCondition condition;
  final AssetCriticality criticality;
  final double? latitude;
  final double? longitude;
  final String? parentAssetId;
  final String? installedOn;
  final String? lastInspectedOnUtc;
  final String createdOnUtc;

  factory Asset.fromJson(Map<String, dynamic> json) => Asset(
        id: json["id"] as String,
        code: json["code"] as String,
        name: json["name"] as String,
        type: _enumFromApi(AssetType.values, json["type"] as String, AssetType.other),
        status: _enumFromApi(AssetStatus.values, json["status"] as String, AssetStatus.operational),
        condition: _enumFromApi(AssetCondition.values, json["condition"] as String, AssetCondition.unknown),
        criticality:
            _enumFromApi(AssetCriticality.values, json["criticality"] as String, AssetCriticality.medium),
        latitude: (json["latitude"] as num?)?.toDouble(),
        longitude: (json["longitude"] as num?)?.toDouble(),
        parentAssetId: json["parentAssetId"] as String?,
        installedOn: json["installedOn"] as String?,
        lastInspectedOnUtc: json["lastInspectedOnUtc"] as String?,
        createdOnUtc: json["createdOnUtc"] as String,
      );
}

const conditionLabel = {
  AssetCondition.unknown: "Not assessed",
  AssetCondition.veryGood: "Very good",
  AssetCondition.good: "Good",
  AssetCondition.fair: "Fair",
  AssetCondition.poor: "Poor",
  AssetCondition.veryPoor: "Very poor",
};

const statusLabel = {
  AssetStatus.planned: "Planned",
  AssetStatus.operational: "In service",
  AssetStatus.underMaintenance: "Maintenance",
  AssetStatus.faulted: "Faulted",
  AssetStatus.decommissioned: "Retired",
};

const typeLabel = {
  AssetType.pipe: "Pipe",
  AssetType.pump: "Pump",
  AssetType.valve: "Valve",
  AssetType.hydrant: "Hydrant",
  AssetType.tank: "Tank",
  AssetType.treatmentPlant: "Treatment plant",
  AssetType.transformer: "Transformer",
  AssetType.substation: "Substation",
  AssetType.powerLine: "Power line",
  AssetType.streetLight: "Street light",
  AssetType.road: "Road",
  AssetType.bridge: "Bridge",
  AssetType.drain: "Drain",
  AssetType.sensor: "Sensor",
  AssetType.site: "Site",
  AssetType.other: "Other",
};
