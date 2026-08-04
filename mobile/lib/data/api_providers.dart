import "package:flutter_riverpod/flutter_riverpod.dart";

import "../core/api_client.dart";
import "session.dart";
import "repositories.dart";

/// A single `ApiClient` for the app's lifetime, reading whatever token is current in
/// `sessionProvider` at request time rather than capturing one at construction -- a sign-out
/// followed by a different sign-in must never leave a stale token attached to in-flight requests.
final apiClientProvider = Provider<ApiClient>((ref) {
  return ApiClient(readToken: () async => ref.read(sessionProvider).token);
});

final authRepositoryProvider = Provider((ref) => AuthRepository(ref.watch(apiClientProvider)));
final assetRepositoryProvider = Provider((ref) => AssetRepository(ref.watch(apiClientProvider)));
final incidentRepositoryProvider = Provider((ref) => IncidentRepository(ref.watch(apiClientProvider)));
final workOrderRepositoryProvider = Provider((ref) => WorkOrderRepository(ref.watch(apiClientProvider)));
final maintenanceRepositoryProvider =
    Provider((ref) => MaintenanceRepository(ref.watch(apiClientProvider)));
