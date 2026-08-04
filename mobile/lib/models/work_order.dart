enum WorkOrderStatus { draft, scheduled, inProgress, completed, cancelled }

enum WorkOrderPriority { low, medium, high, critical }

T _enumFromApiName<T extends Enum>(List<T> values, String apiName, T fallback) {
  for (final value in values) {
    final pascal = value.name[0].toUpperCase() + value.name.substring(1);
    if (pascal == apiName) return value;
  }
  return fallback;
}

String workOrderPriorityToApi(WorkOrderPriority p) => p.name[0].toUpperCase() + p.name.substring(1);

class WorkOrderListItem {
  const WorkOrderListItem({
    required this.id,
    required this.reference,
    required this.title,
    required this.status,
    required this.priority,
    required this.assetId,
    required this.incidentId,
    required this.maintenancePlanId,
    required this.assignedToUserId,
    required this.scheduledFor,
    required this.startedOnUtc,
    required this.completedOnUtc,
    required this.createdOnUtc,
  });

  final String id;
  final String reference;
  final String title;
  final WorkOrderStatus status;
  final WorkOrderPriority priority;
  final String? assetId;
  final String? incidentId;
  final String? maintenancePlanId;
  final String? assignedToUserId;
  final String? scheduledFor;
  final String? startedOnUtc;
  final String? completedOnUtc;
  final String createdOnUtc;

  bool get isOpen =>
      status == WorkOrderStatus.draft ||
      status == WorkOrderStatus.scheduled ||
      status == WorkOrderStatus.inProgress;

  factory WorkOrderListItem.fromJson(Map<String, dynamic> json) => WorkOrderListItem(
        id: json["id"] as String,
        reference: json["reference"] as String,
        title: json["title"] as String,
        status: _enumFromApiName(WorkOrderStatus.values, json["status"] as String, WorkOrderStatus.draft),
        priority: _enumFromApiName(
            WorkOrderPriority.values, json["priority"] as String, WorkOrderPriority.medium),
        assetId: json["assetId"] as String?,
        incidentId: json["incidentId"] as String?,
        maintenancePlanId: json["maintenancePlanId"] as String?,
        assignedToUserId: json["assignedToUserId"] as String?,
        scheduledFor: json["scheduledFor"] as String?,
        startedOnUtc: json["startedOnUtc"] as String?,
        completedOnUtc: json["completedOnUtc"] as String?,
        createdOnUtc: json["createdOnUtc"] as String,
      );
}

class AssignableUser {
  const AssignableUser({required this.id, required this.displayName, required this.roles});

  final String id;
  final String displayName;
  final List<String> roles;

  factory AssignableUser.fromJson(Map<String, dynamic> json) => AssignableUser(
        id: json["id"] as String,
        displayName: json["displayName"] as String,
        roles: (json["roles"] as List).cast<String>(),
      );
}

const workOrderPriorityLabel = {
  WorkOrderPriority.critical: "Critical",
  WorkOrderPriority.high: "High",
  WorkOrderPriority.medium: "Medium",
  WorkOrderPriority.low: "Low",
};

const workOrderStatusLabel = {
  WorkOrderStatus.draft: "Awaiting assignment",
  WorkOrderStatus.scheduled: "Scheduled",
  WorkOrderStatus.inProgress: "In progress",
  WorkOrderStatus.completed: "Completed",
  WorkOrderStatus.cancelled: "Cancelled",
};
