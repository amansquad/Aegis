import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";
import "package:go_router/go_router.dart";

import "../data/session.dart";
import "../features/auth/login_screen.dart";
import "../features/dashboard/dashboard_screen.dart";
import "../features/assets/assets_screen.dart";
import "../features/incidents/incidents_screen.dart";
import "../features/work_orders/work_orders_screen.dart";
import "../features/maintenance/maintenance_screen.dart";
import "app_shell.dart";

/// Bridges Riverpod's `sessionProvider` to go_router's `Listenable`-based refresh mechanism --
/// go_router does not know about Riverpod, so a session change (sign in, sign out, restore from
/// secure storage) has to be turned into a `notifyListeners()` call for the router to re-run its
/// redirect and act on it.
class _SessionRefresh extends ChangeNotifier {
  _SessionRefresh(Ref ref) {
    ref.listen(sessionProvider, (_, _) => notifyListeners());
  }
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = _SessionRefresh(ref);

  return GoRouter(
    initialLocation: "/dashboard",
    refreshListenable: refresh,
    redirect: (context, state) {
      final isSignedIn = ref.read(sessionProvider).isSignedIn;
      final onLogin = state.matchedLocation == "/login";

      if (!isSignedIn && !onLogin) return "/login";
      if (isSignedIn && onLogin) return "/dashboard";
      return null;
    },
    routes: [
      GoRoute(path: "/login", builder: (context, state) => const LoginScreen()),
      ShellRoute(
        builder: (context, state, child) => AppShell(child: child),
        routes: [
          GoRoute(path: "/dashboard", builder: (context, state) => const DashboardScreen()),
          GoRoute(path: "/assets", builder: (context, state) => const AssetsScreen()),
          GoRoute(path: "/incidents", builder: (context, state) => const IncidentsScreen()),
          GoRoute(path: "/work-orders", builder: (context, state) => const WorkOrdersScreen()),
          GoRoute(path: "/maintenance", builder: (context, state) => const MaintenanceScreen()),
        ],
      ),
    ],
  );
});
