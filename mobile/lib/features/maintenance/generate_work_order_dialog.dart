import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../models/maintenance_plan.dart";
import "../../models/work_order.dart";

class GenerateWorkOrderDialog extends ConsumerStatefulWidget {
  const GenerateWorkOrderDialog({super.key, required this.plan});
  final MaintenancePlanListItem plan;

  @override
  ConsumerState<GenerateWorkOrderDialog> createState() => _GenerateWorkOrderDialogState();
}

class _GenerateWorkOrderDialogState extends ConsumerState<GenerateWorkOrderDialog> {
  WorkOrderPriority _priority = WorkOrderPriority.medium;
  bool _busy = false;
  String? _error;

  Future<void> _generate() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(maintenanceRepositoryProvider).generateWorkOrder(widget.plan.id, _priority);
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text("Generate work order"),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            "Dispatches \"${widget.plan.title}\" as a work order. How urgently this occurrence "
            "needs doing is a dispatch decision -- it is not copied from the plan.",
            style: const TextStyle(fontSize: 13, color: AegisColors.inkMuted),
          ),
          const SizedBox(height: 16),
          DropdownButtonFormField<WorkOrderPriority>(
            initialValue: _priority,
            decoration: const InputDecoration(labelText: "Priority"),
            items: [
              for (final entry in workOrderPriorityLabel.entries)
                DropdownMenuItem(value: entry.key, child: Text(entry.value)),
            ],
            onChanged: (v) => setState(() => _priority = v ?? _priority),
          ),
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(_error!, style: const TextStyle(color: AegisColors.failed, fontSize: 13)),
          ],
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text("Cancel")),
        ElevatedButton(onPressed: _busy ? null : _generate, child: const Text("Generate")),
      ],
    );
  }
}
