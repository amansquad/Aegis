import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../data/session.dart";
import "../../models/common.dart";
import "../../models/maintenance_plan.dart";
import "../../widgets/states.dart";
import "create_plan_sheet.dart";
import "generate_work_order_dialog.dart";

enum _QuickFilter { due, active, all }

class MaintenanceScreen extends ConsumerStatefulWidget {
  const MaintenanceScreen({super.key});

  @override
  ConsumerState<MaintenanceScreen> createState() => _MaintenanceScreenState();
}

class _MaintenanceScreenState extends ConsumerState<MaintenanceScreen> {
  _QuickFilter _quickFilter = _QuickFilter.due;
  Future<PagedResult<MaintenancePlanListItem>>? _future;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    setState(() {
      _future = ref.read(maintenanceRepositoryProvider).list(
            dueOnly: _quickFilter == _QuickFilter.due,
            activeOnly: _quickFilter == _QuickFilter.active,
            pageSize: 50,
          );
    });
  }

  /// A plan's due date relative to now -- self-contained, not derived from a generic "time ago"
  /// helper, because a helper written for the past reads "just now" for any future date, and a
  /// plan due in five days is not due just now. Mirrors `dueDateLabel` in the web dashboard.
  String _dueLabel(String nextDueOnUtc) {
    final due = DateTime.parse(nextDueOnUtc);
    final days = due.difference(DateTime.now()).inDays;

    if (days > 1) return "Due in ${days}d";
    if (days == 1) return "Due tomorrow";
    if (days == 0) return "Due today";
    if (days == -1) return "Overdue by 1d";
    return "Overdue by ${-days}d";
  }

  @override
  Widget build(BuildContext context) {
    final canSchedule = ref.watch(sessionProvider).hasPermission("maintenance.schedule");

    return Scaffold(
      floatingActionButton: canSchedule
          ? FloatingActionButton.extended(
              onPressed: () async {
                final created = await showModalBottomSheet<bool>(
                  context: context,
                  isScrollControlled: true,
                  builder: (context) => const CreatePlanSheet(),
                );
                if (created == true) _load();
              },
              icon: const Icon(Icons.add),
              label: const Text("New plan"),
            )
          : null,
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: Row(
              children: [
                for (final entry in const {
                  _QuickFilter.due: "Due",
                  _QuickFilter.active: "Active",
                  _QuickFilter.all: "All",
                }.entries)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: ChoiceChip(
                      label: Text(entry.value),
                      selected: _quickFilter == entry.key,
                      onSelected: (_) {
                        _quickFilter = entry.key;
                        _load();
                      },
                    ),
                  ),
              ],
            ),
          ),
          Expanded(
            child: FutureBuilder<PagedResult<MaintenancePlanListItem>>(
              future: _future,
              builder: (context, snapshot) {
                if (snapshot.connectionState == ConnectionState.waiting) return const LoadingList();

                if (snapshot.hasError) {
                  final message = snapshot.error is ApiException
                      ? (snapshot.error as ApiException).message
                      : "Unexpected error.";
                  return ErrorView(message: message, onRetry: _load);
                }

                final items = snapshot.data?.items ?? [];

                if (items.isEmpty) {
                  return const EmptyView(
                    title: "No plans match this view",
                    description: "Try 'All' or create a maintenance plan.",
                  );
                }

                return RefreshIndicator(
                  onRefresh: () async => _load(),
                  child: ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (context, index) {
                      final plan = items[index];
                      return ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(plan.title, maxLines: 1, overflow: TextOverflow.ellipsis),
                        subtitle: Text(
                          "${plan.reference} · every ${plan.frequencyDays}d",
                          style: const TextStyle(fontSize: 11, color: AegisColors.inkFaint),
                        ),
                        trailing: !plan.isActive
                            ? const Text("Inactive", style: TextStyle(fontSize: 12, color: AegisColors.inkFaint))
                            : Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                crossAxisAlignment: CrossAxisAlignment.end,
                                children: [
                                  Text(
                                    _dueLabel(plan.nextDueOnUtc),
                                    style: TextStyle(
                                      fontSize: 12,
                                      fontWeight: FontWeight.w600,
                                      color: plan.isDue ? AegisColors.degraded : AegisColors.inkMuted,
                                    ),
                                  ),
                                  if (canSchedule)
                                    TextButton(
                                      style: TextButton.styleFrom(
                                        padding: EdgeInsets.zero,
                                        minimumSize: const Size(0, 24),
                                        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                                      ),
                                      onPressed: () async {
                                        final generated = await showDialog<bool>(
                                          context: context,
                                          builder: (context) => GenerateWorkOrderDialog(plan: plan),
                                        );
                                        if (generated == true) _load();
                                      },
                                      child: const Text("Generate", style: TextStyle(fontSize: 12)),
                                    ),
                                ],
                              ),
                      );
                    },
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
