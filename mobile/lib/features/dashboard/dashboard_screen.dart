import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";
import "package:go_router/go_router.dart";

import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../data/session.dart";
import "../../widgets/badges.dart";
import "../../widgets/states.dart";

final _openIncidentsProvider = FutureProvider.autoDispose((ref) =>
    ref.watch(incidentRepositoryProvider).list(openOnly: true, pageSize: 1));

final _safetyIncidentsProvider = FutureProvider.autoDispose((ref) =>
    ref.watch(incidentRepositoryProvider).list(safetyRiskOnly: true, openOnly: true, pageSize: 5));

final _unassignedWorkOrdersProvider = FutureProvider.autoDispose(
    (ref) => ref.watch(workOrderRepositoryProvider).list(unassignedOnly: true, pageSize: 5));

final _dueMaintenanceProvider =
    FutureProvider.autoDispose((ref) => ref.watch(maintenanceRepositoryProvider).list(dueOnly: true, pageSize: 5));

/// A simplified, mobile-shaped version of the web command centre: the same cross-module
/// synthesis (open incidents, unassigned work, due maintenance, each a door into its module),
/// laid out vertically for a phone rather than the web's five-column desktop grid.
class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(sessionProvider).user;
    final openIncidents = ref.watch(_openIncidentsProvider);
    final safetyIncidents = ref.watch(_safetyIncidentsProvider);
    final unassignedWorkOrders = ref.watch(_unassignedWorkOrdersProvider);
    final dueMaintenance = ref.watch(_dueMaintenanceProvider);

    return RefreshIndicator(
      onRefresh: () async {
        ref.invalidate(_openIncidentsProvider);
        ref.invalidate(_safetyIncidentsProvider);
        ref.invalidate(_unassignedWorkOrdersProvider);
        ref.invalidate(_dueMaintenanceProvider);
      },
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(
            "${_greeting()}, ${user?.displayName.split(' ').first ?? ''}",
            style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 4),
          const Row(
            children: [
              _PulseDot(),
              SizedBox(width: 6),
              Text("Live operations summary", style: TextStyle(color: AegisColors.inkMuted, fontSize: 13)),
            ],
          ),
          const SizedBox(height: 16),
          GridView.count(
            crossAxisCount: 2,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            mainAxisSpacing: 10,
            crossAxisSpacing: 10,
            childAspectRatio: 1.5,
            children: [
              _KpiCard(
                label: "Open incidents",
                value: openIncidents.valueOrNull?.totalCount,
                color: AegisColors.signal,
                icon: Icons.report_problem_outlined,
                onTap: () => context.go("/incidents"),
                sub: (safetyIncidents.valueOrNull?.totalCount ?? 0) > 0
                    ? "${safetyIncidents.valueOrNull!.totalCount} safety risk"
                    : null,
              ),
              _KpiCard(
                label: "Unassigned work",
                value: unassignedWorkOrders.valueOrNull?.totalCount,
                color: AegisColors.watch,
                icon: Icons.assignment_late_outlined,
                onTap: () => context.go("/work-orders"),
              ),
              _KpiCard(
                label: "Maintenance due",
                value: dueMaintenance.valueOrNull?.totalCount,
                color: AegisColors.degraded,
                icon: Icons.build_circle_outlined,
                onTap: () => context.go("/maintenance"),
              ),
              _KpiCard(
                label: "Assets",
                value: null,
                color: AegisColors.nominal,
                icon: Icons.settings_input_component_outlined,
                onTap: () => context.go("/assets"),
                sub: "Open registry",
              ),
            ],
          ),
          const SizedBox(height: 24),
          const Text("Priority queue", style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
          const SizedBox(height: 8),
          _PriorityQueue(
            safetyIncidents: safetyIncidents,
            unassignedWorkOrders: unassignedWorkOrders,
            dueMaintenance: dueMaintenance,
          ),
        ],
      ),
    );
  }

  String _greeting() {
    final hour = DateTime.now().hour;
    if (hour < 12) return "Good morning";
    if (hour < 18) return "Good afternoon";
    return "Good evening";
  }
}

class _PulseDot extends StatelessWidget {
  const _PulseDot();

  @override
  Widget build(BuildContext context) =>
      Container(width: 6, height: 6, decoration: const BoxDecoration(color: AegisColors.nominal, shape: BoxShape.circle));
}

class _KpiCard extends StatelessWidget {
  const _KpiCard({
    required this.label,
    required this.value,
    required this.color,
    required this.icon,
    required this.onTap,
    this.sub,
  });

  final String label;
  final int? value;
  final Color color;
  final IconData icon;
  final VoidCallback onTap;
  final String? sub;

  @override
  Widget build(BuildContext context) => Card(
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(10),
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Expanded(
                      child: Text(label, style: const TextStyle(fontSize: 11, color: AegisColors.inkMuted)),
                    ),
                    Icon(icon, size: 16, color: color),
                  ],
                ),
                const Spacer(),
                Text(
                  value?.toString() ?? "→",
                  style: TextStyle(fontSize: 26, fontWeight: FontWeight.w700, color: color),
                ),
                if (sub != null) ...[
                  const SizedBox(height: 2),
                  Text(sub!, style: const TextStyle(fontSize: 11, color: AegisColors.inkFaint)),
                ],
              ],
            ),
          ),
        ),
      );
}

class _PriorityQueue extends StatelessWidget {
  const _PriorityQueue({
    required this.safetyIncidents,
    required this.unassignedWorkOrders,
    required this.dueMaintenance,
  });

  final AsyncValue safetyIncidents;
  final AsyncValue unassignedWorkOrders;
  final AsyncValue dueMaintenance;

  @override
  Widget build(BuildContext context) {
    if (safetyIncidents.isLoading || unassignedWorkOrders.isLoading || dueMaintenance.isLoading) {
      return const SizedBox(height: 200, child: LoadingList());
    }

    final rows = <Widget>[
      for (final incident in safetyIncidents.valueOrNull?.items ?? [])
        ListTile(
          contentPadding: EdgeInsets.zero,
          leading: const Icon(Icons.warning_amber_rounded, color: AegisColors.failed),
          title: Text(incident.summary, maxLines: 1, overflow: TextOverflow.ellipsis),
          subtitle: Text("${incident.reference} — public safety risk"),
          trailing: SeverityBadge(severity: incident.severity),
          onTap: () => context.go("/incidents"),
        ),
      for (final workOrder in unassignedWorkOrders.valueOrNull?.items ?? [])
        ListTile(
          contentPadding: EdgeInsets.zero,
          leading: const Icon(Icons.assignment_late_outlined, color: AegisColors.watch),
          title: Text(workOrder.title, maxLines: 1, overflow: TextOverflow.ellipsis),
          subtitle: Text("${workOrder.reference} — awaiting assignment"),
          trailing: PriorityBadge(priority: workOrder.priority),
          onTap: () => context.go("/work-orders"),
        ),
      for (final plan in dueMaintenance.valueOrNull?.items ?? [])
        ListTile(
          contentPadding: EdgeInsets.zero,
          leading: const Icon(Icons.build_circle_outlined, color: AegisColors.inkMuted),
          title: Text(plan.title, maxLines: 1, overflow: TextOverflow.ellipsis),
          subtitle: Text("${plan.reference} — due"),
          trailing: Text("every ${plan.frequencyDays}d", style: const TextStyle(fontSize: 12, color: AegisColors.inkFaint)),
          onTap: () => context.go("/maintenance"),
        ),
    ];

    if (rows.isEmpty) {
      return const EmptyView(
        title: "Nothing needs attention right now",
        description: "No safety risks, unassigned work, or overdue maintenance.",
      );
    }

    return Column(children: rows.take(8).toList());
  }
}
