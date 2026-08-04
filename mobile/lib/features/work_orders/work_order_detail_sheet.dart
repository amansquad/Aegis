import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../data/session.dart";
import "../../models/work_order.dart";
import "../../widgets/badges.dart";

const _openStatuses = {WorkOrderStatus.draft, WorkOrderStatus.scheduled, WorkOrderStatus.inProgress};

class WorkOrderDetailSheet extends ConsumerStatefulWidget {
  const WorkOrderDetailSheet({super.key, required this.workOrder});
  final WorkOrderListItem workOrder;

  @override
  ConsumerState<WorkOrderDetailSheet> createState() => _WorkOrderDetailSheetState();
}

class _WorkOrderDetailSheetState extends ConsumerState<WorkOrderDetailSheet> {
  String? _selectedAssignee;
  final _notesController = TextEditingController();
  final _reasonController = TextEditingController();
  bool _busy = false;
  String? _error;
  List<AssignableUser>? _assignable;

  @override
  void initState() {
    super.initState();
    ref.read(workOrderRepositoryProvider).listAssignable().then((users) {
      if (mounted) setState(() => _assignable = users);
    });
  }

  @override
  void dispose() {
    _notesController.dispose();
    _reasonController.dispose();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final workOrder = widget.workOrder;
    final session = ref.watch(sessionProvider);
    final canAssign = session.hasPermission("workorders.assign");
    final canComplete = session.hasPermission("workorders.complete");
    final isOpen = _openStatuses.contains(workOrder.status);

    return DraggableScrollableSheet(
      initialChildSize: 0.75,
      expand: false,
      builder: (context, scrollController) => SingleChildScrollView(
        controller: scrollController,
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(workOrder.reference, style: const TextStyle(fontSize: 12, color: AegisColors.inkFaint)),
            const SizedBox(height: 4),
            WorkOrderStatusText(status: workOrder.status),
            const SizedBox(height: 12),
            Text(workOrder.title, style: const TextStyle(fontSize: 14)),
            const SizedBox(height: 8),
            PriorityBadge(priority: workOrder.priority),
            if (isOpen && canAssign) ...[
              const Divider(height: 32),
              Text(
                workOrder.assignedToUserId != null ? "Reassign" : "Assign",
                style: const TextStyle(fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 10),
              if (_assignable == null)
                const LinearProgressIndicator()
              else
                DropdownButtonFormField<String>(
                  initialValue: _selectedAssignee,
                  decoration: const InputDecoration(labelText: "Technician"),
                  items: [
                    for (final user in _assignable!)
                      DropdownMenuItem(value: user.id, child: Text(user.displayName)),
                  ],
                  onChanged: (v) => setState(() => _selectedAssignee = v),
                ),
              const SizedBox(height: 12),
              ElevatedButton(
                onPressed: _busy || _selectedAssignee == null
                    ? null
                    : () => _run(() => ref
                        .read(workOrderRepositoryProvider)
                        .assign(workOrder.id, _selectedAssignee!)),
                child: const Text("Assign"),
              ),
            ],
            if (workOrder.status == WorkOrderStatus.scheduled && canComplete) ...[
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: _busy ? null : () => _run(() => ref.read(workOrderRepositoryProvider).start(workOrder.id)),
                child: const Text("Mark as underway"),
              ),
            ],
            if (isOpen && workOrder.assignedToUserId != null && canComplete) ...[
              const Divider(height: 32),
              const Text("Complete", style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 10),
              TextField(
                controller: _notesController,
                maxLines: 2,
                decoration: const InputDecoration(labelText: "What was done", hintText: "Optional"),
              ),
              const SizedBox(height: 12),
              ElevatedButton(
                onPressed: _busy
                    ? null
                    : () => _run(() => ref
                        .read(workOrderRepositoryProvider)
                        .complete(workOrder.id, notes: _notesController.text.trim())),
                child: const Text("Mark completed"),
              ),
            ],
            if (isOpen && canAssign) ...[
              const Divider(height: 32),
              const Text("Cancel", style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 10),
              TextField(
                controller: _reasonController,
                maxLines: 2,
                decoration: const InputDecoration(labelText: "Reason", hintText: "Optional"),
              ),
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: _busy
                    ? null
                    : () => _run(() => ref
                        .read(workOrderRepositoryProvider)
                        .cancel(workOrder.id, reason: _reasonController.text.trim())),
                child: const Text("Withdraw work order"),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, style: const TextStyle(color: AegisColors.failed, fontSize: 13)),
            ],
          ],
        ),
      ),
    );
  }
}
