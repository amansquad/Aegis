class MaintenancePlanListItem {
  const MaintenancePlanListItem({
    required this.id,
    required this.reference,
    required this.assetId,
    required this.title,
    required this.frequencyDays,
    required this.nextDueOnUtc,
    required this.lastCompletedOnUtc,
    required this.isActive,
    required this.isDue,
    required this.createdOnUtc,
  });

  final String id;
  final String reference;
  final String assetId;
  final String title;
  final int frequencyDays;
  final String nextDueOnUtc;
  final String? lastCompletedOnUtc;
  final bool isActive;
  final bool isDue;
  final String createdOnUtc;

  factory MaintenancePlanListItem.fromJson(Map<String, dynamic> json) => MaintenancePlanListItem(
        id: json["id"] as String,
        reference: json["reference"] as String,
        assetId: json["assetId"] as String,
        title: json["title"] as String,
        frequencyDays: json["frequencyDays"] as int,
        nextDueOnUtc: json["nextDueOnUtc"] as String,
        lastCompletedOnUtc: json["lastCompletedOnUtc"] as String?,
        isActive: json["isActive"] as bool,
        isDue: json["isDue"] as bool,
        createdOnUtc: json["createdOnUtc"] as String,
      );
}
