import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/format.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../data/session.dart";
import "../../models/common.dart";
import "../../models/work_order.dart";
import "../../widgets/badges.dart";
import "../../widgets/states.dart";
import "create_work_order_sheet.dart";
import "work_order_detail_sheet.dart";

enum _QuickFilter { open, unassigned, all }

class WorkOrdersScreen extends ConsumerStatefulWidget {
  const WorkOrdersScreen({super.key});

  @override
  ConsumerState<WorkOrdersScreen> createState() => _WorkOrdersScreenState();
}

class _WorkOrdersScreenState extends ConsumerState<WorkOrdersScreen> {
  _QuickFilter _quickFilter = _QuickFilter.open;
  Future<PagedResult<WorkOrderListItem>>? _future;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    setState(() {
      _future = ref.read(workOrderRepositoryProvider).list(
            openOnly: _quickFilter == _QuickFilter.open,
            unassignedOnly: _quickFilter == _QuickFilter.unassigned,
            pageSize: 50,
          );
    });
  }

  @override
  Widget build(BuildContext context) {
    final canCreate = ref.watch(sessionProvider).hasPermission("workorders.create");

    return Scaffold(
      floatingActionButton: canCreate
          ? FloatingActionButton.extended(
              onPressed: () async {
                final created = await showModalBottomSheet<bool>(
                  context: context,
                  isScrollControlled: true,
                  builder: (context) => const CreateWorkOrderSheet(),
                );
                if (created == true) _load();
              },
              icon: const Icon(Icons.add),
              label: const Text("Dispatch"),
            )
          : null,
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: Row(
              children: [
                for (final entry in const {
                  _QuickFilter.open: "Open",
                  _QuickFilter.unassigned: "Awaiting assignment",
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
            child: FutureBuilder<PagedResult<WorkOrderListItem>>(
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
                    title: "No work orders match this view",
                    description: "Try 'All' or dispatch a new work order.",
                  );
                }

                return RefreshIndicator(
                  onRefresh: () async => _load(),
                  child: ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (context, index) {
                      final workOrder = items[index];
                      return ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(workOrder.title, maxLines: 1, overflow: TextOverflow.ellipsis),
                        subtitle: Padding(
                          padding: const EdgeInsets.only(top: 4),
                          child: Row(
                            children: [
                              PriorityBadge(priority: workOrder.priority),
                              const SizedBox(width: 8),
                              Expanded(
                                child: Text(
                                  workOrder.reference,
                                  style: const TextStyle(fontSize: 11, color: AegisColors.inkFaint),
                                ),
                              ),
                              Text(relativeAge(workOrder.createdOnUtc),
                                  style: const TextStyle(fontSize: 11, color: AegisColors.inkFaint)),
                            ],
                          ),
                        ),
                        trailing: WorkOrderStatusText(status: workOrder.status),
                        onTap: () async {
                          final changed = await showModalBottomSheet<bool>(
                            context: context,
                            isScrollControlled: true,
                            builder: (context) => WorkOrderDetailSheet(workOrder: workOrder),
                          );
                          if (changed == true) _load();
                        },
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
