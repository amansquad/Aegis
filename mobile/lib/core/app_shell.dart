import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";
import "package:go_router/go_router.dart";

import "theme_mode_provider.dart";
import "../data/session.dart";

const _tabs = [
  ("/dashboard", "Operations", Icons.dashboard_outlined, Icons.dashboard),
  ("/assets", "Assets", Icons.settings_input_component_outlined, Icons.settings_input_component),
  ("/incidents", "Incidents", Icons.report_problem_outlined, Icons.report_problem),
  ("/work-orders", "Work orders", Icons.assignment_outlined, Icons.assignment),
  ("/maintenance", "Maintenance", Icons.build_outlined, Icons.build),
];

/// The bottom-nav shell every module screen renders inside, the mobile equivalent of the web
/// client's `AppShell` sidebar -- same five sections, same "no session -> /login" guard (handled
/// one layer up, in the router's redirect), same sign-out affordance.
class AppShell extends ConsumerWidget {
  const AppShell({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final location = GoRouterState.of(context).matchedLocation;
    final currentIndex = _tabs.indexWhere((t) => t.$1 == location).clamp(0, _tabs.length - 1);
    final user = ref.watch(sessionProvider).user;
    final themeMode = ref.watch(themeModeProvider);

    return Scaffold(
      appBar: AppBar(
        title: Text(user?.organizationName ?? "AEGIS"),
        actions: [
          IconButton(
            tooltip: themeMode == ThemeMode.dark ? "Switch to light theme" : "Switch to dark theme",
            icon: Icon(themeMode == ThemeMode.dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined),
            onPressed: () => ref.read(themeModeProvider.notifier).toggle(),
          ),
          IconButton(
            tooltip: "Sign out — ${user?.email ?? ''}",
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(sessionProvider.notifier).signOut(),
          ),
        ],
      ),
      body: child,
      bottomNavigationBar: NavigationBar(
        selectedIndex: currentIndex,
        backgroundColor: Theme.of(context).cardColor,
        onDestinationSelected: (index) => context.go(_tabs[index].$1),
        destinations: [
          for (final tab in _tabs)
            NavigationDestination(icon: Icon(tab.$3), selectedIcon: Icon(tab.$4), label: tab.$2),
        ],
      ),
    );
  }
}
