import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/format.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../data/session.dart";
import "../../models/common.dart";
import "../../models/incident.dart";
import "../../widgets/badges.dart";
import "../../widgets/states.dart";
import "report_incident_sheet.dart";
import "incident_detail_sheet.dart";

enum _QuickFilter { awaitingTriage, open, safetyRisk, all }

class IncidentsScreen extends ConsumerStatefulWidget {
  const IncidentsScreen({super.key});

  @override
  ConsumerState<IncidentsScreen> createState() => _IncidentsScreenState();
}

class _IncidentsScreenState extends ConsumerState<IncidentsScreen> {
  _QuickFilter _quickFilter = _QuickFilter.awaitingTriage;
  Future<PagedResult<IncidentListItem>>? _future;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    setState(() {
      _future = ref.read(incidentRepositoryProvider).list(
            awaitingTriageOnly: _quickFilter == _QuickFilter.awaitingTriage,
            openOnly: _quickFilter == _QuickFilter.open,
            safetyRiskOnly: _quickFilter == _QuickFilter.safetyRisk,
            pageSize: 50,
          );
    });
  }

  @override
  Widget build(BuildContext context) {
    final canReport = ref.watch(sessionProvider).hasPermission("incidents.report");

    return Scaffold(
      floatingActionButton: canReport
          ? FloatingActionButton.extended(
              onPressed: () async {
                final reported = await showModalBottomSheet<bool>(
                  context: context,
                  isScrollControlled: true,
                  builder: (context) => const ReportIncidentSheet(),
                );
                if (reported == true) _load();
              },
              icon: const Icon(Icons.add),
              label: const Text("Report"),
            )
          : null,
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  for (final entry in const {
                    _QuickFilter.awaitingTriage: "Awaiting triage",
                    _QuickFilter.open: "Open",
                    _QuickFilter.safetyRisk: "Safety risk",
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
          ),
          Expanded(
            child: FutureBuilder<PagedResult<IncidentListItem>>(
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
                    title: "No incidents match this view",
                    description: "Try 'All' or report a new incident.",
                  );
                }

                return RefreshIndicator(
                  onRefresh: () async => _load(),
                  child: ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (context, index) {
                      final incident = items[index];
                      return ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(incident.summary, maxLines: 2, overflow: TextOverflow.ellipsis),
                        subtitle: Padding(
                          padding: const EdgeInsets.only(top: 4),
                          child: Row(
                            children: [
                              SeverityBadge(severity: incident.severity),
                              const SizedBox(width: 8),
                              Expanded(
                                child: Text(
                                  "${incident.reference} · ${categoryLabel[incident.category]}",
                                  style: const TextStyle(fontSize: 11, color: AegisColors.inkFaint),
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              Text(relativeAge(incident.reportedOnUtc),
                                  style: const TextStyle(fontSize: 11, color: AegisColors.inkFaint)),
                            ],
                          ),
                        ),
                        leading: incident.publicSafetyRisk
                            ? const Icon(Icons.warning_amber_rounded, color: AegisColors.failed)
                            : null,
                        trailing: IncidentStatusText(status: incident.status),
                        onTap: () async {
                          final changed = await showModalBottomSheet<bool>(
                            context: context,
                            isScrollControlled: true,
                            builder: (context) => IncidentDetailSheet(incident: incident),
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
