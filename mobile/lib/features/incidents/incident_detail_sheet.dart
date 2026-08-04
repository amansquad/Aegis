import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../data/session.dart";
import "../../models/incident.dart";
import "../../widgets/badges.dart";

const _openStatuses = {IncidentStatus.reported, IncidentStatus.triaged, IncidentStatus.inProgress};

class IncidentDetailSheet extends ConsumerStatefulWidget {
  const IncidentDetailSheet({super.key, required this.incident});
  final IncidentListItem incident;

  @override
  ConsumerState<IncidentDetailSheet> createState() => _IncidentDetailSheetState();
}

class _IncidentDetailSheetState extends ConsumerState<IncidentDetailSheet> {
  late IncidentCategory _category = widget.incident.category;
  late IncidentSeverity _severity = widget.incident.severity;
  final _notesController = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _notesController.dispose();
    super.dispose();
  }

  Future<void> _triage() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(incidentRepositoryProvider).triage(
            widget.incident.id,
            category: _category,
            severity: _severity,
            assetId: widget.incident.assetId,
          );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _resolve() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref
          .read(incidentRepositoryProvider)
          .resolve(widget.incident.id, notes: _notesController.text.trim());
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final incident = widget.incident;
    final canTriage = ref.watch(sessionProvider).hasPermission("incidents.triage");
    final canClose = ref.watch(sessionProvider).hasPermission("incidents.close");
    final isOpen = _openStatuses.contains(incident.status);

    return DraggableScrollableSheet(
      initialChildSize: 0.75,
      expand: false,
      builder: (context, scrollController) => SingleChildScrollView(
        controller: scrollController,
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(incident.reference,
                style: const TextStyle(fontSize: 12, color: AegisColors.inkFaint, fontFeatures: [
                  FontFeature.tabularFigures(),
                ])),
            const SizedBox(height: 4),
            IncidentStatusText(status: incident.status),
            const SizedBox(height: 12),
            if (incident.publicSafetyRisk)
              Container(
                padding: const EdgeInsets.all(10),
                margin: const EdgeInsets.only(bottom: 12),
                decoration: BoxDecoration(
                  color: AegisColors.failedDim,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: const Text(
                  "This report describes possible danger to people. Confirm and dispatch urgently.",
                  style: TextStyle(color: AegisColors.failed, fontSize: 13, fontWeight: FontWeight.w600),
                ),
              ),
            Text(incident.summary, style: const TextStyle(fontSize: 14)),
            const SizedBox(height: 16),
            Row(
              children: [
                Text(categoryLabel[incident.category]!, style: const TextStyle(color: AegisColors.inkMuted)),
                const SizedBox(width: 12),
                SeverityBadge(severity: incident.severity),
              ],
            ),
            if (isOpen && canTriage) ...[
              const Divider(height: 32),
              const Text("Confirm classification", style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 10),
              DropdownButtonFormField<IncidentCategory>(
                initialValue: _category,
                decoration: const InputDecoration(labelText: "Category"),
                items: [
                  for (final entry in categoryLabel.entries)
                    DropdownMenuItem(value: entry.key, child: Text(entry.value)),
                ],
                onChanged: (v) => setState(() => _category = v ?? _category),
              ),
              const SizedBox(height: 10),
              DropdownButtonFormField<IncidentSeverity>(
                initialValue: _severity,
                decoration: const InputDecoration(labelText: "Severity"),
                items: [
                  for (final entry in severityLabel.entries)
                    DropdownMenuItem(value: entry.key, child: Text(entry.value)),
                ],
                onChanged: (v) => setState(() => _severity = v ?? _severity),
              ),
              const SizedBox(height: 12),
              ElevatedButton(onPressed: _busy ? null : _triage, child: const Text("Confirm triage")),
            ],
            if (isOpen && canClose) ...[
              const Divider(height: 32),
              const Text("Resolve", style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 10),
              TextField(
                controller: _notesController,
                maxLines: 2,
                decoration: const InputDecoration(labelText: "What was done", hintText: "Optional"),
              ),
              const SizedBox(height: 12),
              OutlinedButton(onPressed: _busy ? null : _resolve, child: const Text("Mark resolved")),
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
