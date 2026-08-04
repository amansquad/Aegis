import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../models/work_order.dart";

class CreateWorkOrderSheet extends ConsumerStatefulWidget {
  const CreateWorkOrderSheet({super.key});

  @override
  ConsumerState<CreateWorkOrderSheet> createState() => _CreateWorkOrderSheetState();
}

class _CreateWorkOrderSheetState extends ConsumerState<CreateWorkOrderSheet> {
  final _titleController = TextEditingController();
  final _descriptionController = TextEditingController();
  WorkOrderPriority _priority = WorkOrderPriority.medium;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _titleController.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _titleController.dispose();
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(workOrderRepositoryProvider).create(
            title: _titleController.text.trim(),
            description: _descriptionController.text.trim().isEmpty ? null : _descriptionController.text.trim(),
            priority: _priority,
          );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(
        left: 20,
        right: 20,
        top: 20,
        bottom: MediaQuery.of(context).viewInsets.bottom + 20,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text("Dispatch work order", style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
          const SizedBox(height: 16),
          TextField(
            controller: _titleController,
            maxLength: 200,
            decoration: const InputDecoration(labelText: "Title", hintText: "e.g. Replace failed isolation valve"),
          ),
          TextField(
            controller: _descriptionController,
            maxLines: 2,
            decoration: const InputDecoration(labelText: "Description", hintText: "Optional"),
          ),
          const SizedBox(height: 4),
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
          const SizedBox(height: 16),
          ElevatedButton(
            onPressed: _busy || _titleController.text.trim().isEmpty ? null : _submit,
            child: _busy
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2, color: AegisColors.void_),
                  )
                : const Text("Dispatch"),
          ),
        ],
      ),
    );
  }
}
