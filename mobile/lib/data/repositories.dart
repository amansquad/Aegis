import "../core/api_client.dart";
import "../models/asset.dart";
import "../models/common.dart";
import "../models/incident.dart";
import "../models/maintenance_plan.dart";
import "../models/work_order.dart";

/// One repository per module, each a thin translation from `ApiClient` responses to typed
/// models -- the same role `api.ts` plays on the web client, minus the demo-mode branch, since
/// this client always talks to a real backend rather than standing in for a missing one.

class AuthRepository {
  AuthRepository(this._client);
  final ApiClient _client;

  Future<AuthenticationResult> signIn(String email, String password) => _client.post(
        "/auth/login",
        body: {"email": email, "password": password},
        map: (data) => AuthenticationResult.fromJson(data as Map<String, dynamic>),
      );
}

class AssetRepository {
  AssetRepository(this._client);
  final ApiClient _client;

  Future<PagedResult<Asset>> list({
    String? searchTerm,
    AssetType? type,
    AssetStatus? status,
    int page = 1,
    int pageSize = 25,
  }) async {
    final json = await _client.getJson("/assets", query: {
      if (searchTerm != null && searchTerm.isNotEmpty) "searchTerm": searchTerm,
      if (type != null) "type": assetTypeToApi(type),
      if (status != null) "status": assetStatusToApi(status),
      "page": page,
      "pageSize": pageSize,
    });
    return PagedResult.fromJson(json, (item) => Asset.fromJson(item));
  }
}

class IncidentRepository {
  IncidentRepository(this._client);
  final ApiClient _client;

  Future<PagedResult<IncidentListItem>> list({
    String? searchTerm,
    bool? openOnly,
    bool? awaitingTriageOnly,
    bool? safetyRiskOnly,
    int page = 1,
    int pageSize = 25,
  }) async {
    final json = await _client.getJson("/incidents", query: {
      if (searchTerm != null && searchTerm.isNotEmpty) "searchTerm": searchTerm,
      if (openOnly ?? false) "openOnly": true,
      if (awaitingTriageOnly ?? false) "awaitingTriageOnly": true,
      if (safetyRiskOnly ?? false) "safetyRiskOnly": true,
      "page": page,
      "pageSize": pageSize,
    });
    return PagedResult.fromJson(json, (item) => IncidentListItem.fromJson(item));
  }

  Future<ReportIncidentResult> report(String reportText, {double? latitude, double? longitude}) =>
      _client.post(
        "/incidents",
        body: {
          "reportText": reportText,
          "latitude": latitude,
          "longitude": longitude,
          "reporterName": null,
          "reporterContact": null,
        },
        map: (data) => ReportIncidentResult.fromJson(data as Map<String, dynamic>),
      );

  Future<void> triage(
    String incidentId, {
    required IncidentCategory category,
    required IncidentSeverity severity,
    String? summary,
    String? assetId,
  }) =>
      _client.postNoContent("/incidents/$incidentId/triage", body: {
        "category": incidentCategoryToApi(category),
        "severity": incidentSeverityToApi(severity),
        "summary": summary,
        "assetId": assetId,
      });

  Future<void> resolve(String incidentId, {String? notes}) =>
      _client.postNoContent("/incidents/$incidentId/resolve", body: {"notes": notes});
}

class WorkOrderRepository {
  WorkOrderRepository(this._client);
  final ApiClient _client;

  Future<PagedResult<WorkOrderListItem>> list({
    String? searchTerm,
    bool? openOnly,
    bool? unassignedOnly,
    String? assetId,
    String? incidentId,
    int page = 1,
    int pageSize = 25,
  }) async {
    final json = await _client.getJson("/work-orders", query: {
      if (searchTerm != null && searchTerm.isNotEmpty) "searchTerm": searchTerm,
      if (openOnly ?? false) "openOnly": true,
      if (unassignedOnly ?? false) "unassignedOnly": true,
      if (assetId != null) "assetId": assetId,
      if (incidentId != null) "incidentId": incidentId,
      "page": page,
      "pageSize": pageSize,
    });
    return PagedResult.fromJson(json, (item) => WorkOrderListItem.fromJson(item));
  }

  Future<String> create({
    required String title,
    String? description,
    required WorkOrderPriority priority,
    String? assetId,
    String? incidentId,
  }) =>
      _client.post(
        "/work-orders",
        body: {
          "title": title,
          "description": description,
          "priority": workOrderPriorityToApi(priority),
          "assetId": assetId,
          "incidentId": incidentId,
        },
        map: (data) => data as String,
      );

  Future<void> assign(String workOrderId, String userId, {String? scheduledFor}) =>
      _client.postNoContent("/work-orders/$workOrderId/assign", body: {
        "userId": userId,
        "scheduledFor": scheduledFor,
      });

  Future<void> start(String workOrderId) => _client.postNoContent("/work-orders/$workOrderId/start");

  Future<void> complete(String workOrderId, {String? notes}) =>
      _client.postNoContent("/work-orders/$workOrderId/complete", body: {"notes": notes});

  Future<void> cancel(String workOrderId, {String? reason}) =>
      _client.postNoContent("/work-orders/$workOrderId/cancel", body: {"reason": reason});

  /// Just enough of `/users` for an assignment picker -- the same narrow projection the web
  /// client's `listAssignableUsers` uses.
  Future<List<AssignableUser>> listAssignable() async {
    final json = await _client.getJson("/users", query: {"pageSize": 100, "status": "Active"});
    return (json["items"] as List)
        .map((e) => AssignableUser.fromJson(e as Map<String, dynamic>))
        .toList();
  }
}

class MaintenanceRepository {
  MaintenanceRepository(this._client);
  final ApiClient _client;

  Future<PagedResult<MaintenancePlanListItem>> list({
    String? searchTerm,
    bool? dueOnly,
    bool? activeOnly,
    String? assetId,
    int page = 1,
    int pageSize = 25,
  }) async {
    final json = await _client.getJson("/maintenance-plans", query: {
      if (searchTerm != null && searchTerm.isNotEmpty) "searchTerm": searchTerm,
      if (dueOnly ?? false) "dueOnly": true,
      if (activeOnly ?? false) "activeOnly": true,
      if (assetId != null) "assetId": assetId,
      "page": page,
      "pageSize": pageSize,
    });
    return PagedResult.fromJson(json, (item) => MaintenancePlanListItem.fromJson(item));
  }

  Future<String> create({
    required String assetId,
    required String title,
    String? description,
    required int frequencyDays,
    String? startingOn,
  }) =>
      _client.post(
        "/maintenance-plans",
        body: {
          "assetId": assetId,
          "title": title,
          "description": description,
          "frequencyDays": frequencyDays,
          "startingOn": startingOn,
        },
        map: (data) => data as String,
      );

  Future<String> generateWorkOrder(String planId, WorkOrderPriority priority) => _client.post(
        "/maintenance-plans/$planId/generate-work-order",
        body: {"priority": workOrderPriorityToApi(priority)},
        map: (data) => data as String,
      );

  Future<void> deactivate(String planId) =>
      _client.postNoContent("/maintenance-plans/$planId/deactivate");

  Future<void> reactivate(String planId) =>
      _client.postNoContent("/maintenance-plans/$planId/reactivate");
}
