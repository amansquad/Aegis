import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";

class ReportIncidentSheet extends ConsumerStatefulWidget {
  const ReportIncidentSheet({super.key});

  @override
  ConsumerState<ReportIncidentSheet> createState() => _ReportIncidentSheetState();
}

class _ReportIncidentSheetState extends ConsumerState<ReportIncidentSheet> {
  final _textController = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    // The submit button's enabled state depends on the field's length, so it needs a rebuild on
    // every keystroke -- TextEditingController does not trigger one on its own.
    _textController.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _textController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await ref.read(incidentRepositoryProvider).report(_textController.text.trim());
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
          const Text("Report an incident", style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
          const SizedBox(height: 4),
          const Text(
            "Describe the problem in your own words. Category, severity and any safety risk are "
            "classified automatically, and always reviewed by a dispatcher.",
            style: TextStyle(fontSize: 12, color: AegisColors.inkMuted),
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _textController,
            maxLines: 4,
            maxLength: 2000,
            decoration: const InputDecoration(
              hintText: "e.g. Water leaking from the hydrant on the corner here.",
            ),
          ),
          if (_error != null) ...[
            Text(_error!, style: const TextStyle(color: AegisColors.failed, fontSize: 13)),
            const SizedBox(height: 8),
          ],
          ElevatedButton(
            onPressed: _busy || _textController.text.trim().length < 10 ? null : _submit,
            child: _busy
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2, color: AegisColors.void_),
                  )
                : const Text("Submit report"),
          ),
        ],
      ),
    );
  }
}
