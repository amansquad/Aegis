import "package:flutter/material.dart";

import "../core/theme.dart";
import "../models/asset.dart";
import "../models/incident.dart";
import "../models/work_order.dart";

/// Status is carried by a dot plus a word, never colour alone -- the same accessibility rule the
/// web client's `StatusPill`/`SeverityBadge`/`PriorityBadge` follow, for the same reason: roughly
/// one man in twelve cannot reliably separate red from green, and this app decides what gets a
/// crew sent to it.
class _DotBadge extends StatelessWidget {
  const _DotBadge({required this.label, required this.color, this.filled = false});

  final String label;
  final Color color;
  final bool filled;

  @override
  Widget build(BuildContext context) {
    if (!filled) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(width: 6, height: 6, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
          const SizedBox(width: 6),
          Text(label, style: TextStyle(fontSize: 12, color: color, fontWeight: FontWeight.w500)),
        ],
      );
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.16),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(width: 6, height: 6, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
          const SizedBox(width: 6),
          Text(label, style: TextStyle(fontSize: 11, color: color, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

const _conditionColor = {
  AssetCondition.veryGood: AegisColors.nominal,
  AssetCondition.good: AegisColors.nominal,
  AssetCondition.fair: AegisColors.watch,
  AssetCondition.poor: AegisColors.degraded,
  AssetCondition.veryPoor: AegisColors.failed,
  AssetCondition.unknown: AegisColors.inkFaint,
};

class ConditionBadge extends StatelessWidget {
  const ConditionBadge({super.key, required this.condition});
  final AssetCondition condition;

  @override
  Widget build(BuildContext context) =>
      _DotBadge(label: conditionLabel[condition]!, color: _conditionColor[condition]!, filled: true);
}

const _severityColor = {
  IncidentSeverity.low: AegisColors.inkFaint,
  IncidentSeverity.moderate: AegisColors.signal,
  IncidentSeverity.high: AegisColors.degraded,
  IncidentSeverity.critical: AegisColors.failed,
};

class SeverityBadge extends StatelessWidget {
  const SeverityBadge({super.key, required this.severity});
  final IncidentSeverity severity;

  @override
  Widget build(BuildContext context) =>
      _DotBadge(label: severityLabel[severity]!, color: _severityColor[severity]!, filled: true);
}

const _priorityColor = {
  WorkOrderPriority.low: AegisColors.inkFaint,
  WorkOrderPriority.medium: AegisColors.signal,
  WorkOrderPriority.high: AegisColors.degraded,
  WorkOrderPriority.critical: AegisColors.failed,
};

class PriorityBadge extends StatelessWidget {
  const PriorityBadge({super.key, required this.priority});
  final WorkOrderPriority priority;

  @override
  Widget build(BuildContext context) =>
      _DotBadge(label: workOrderPriorityLabel[priority]!, color: _priorityColor[priority]!, filled: true);
}

const _incidentStatusColor = {
  IncidentStatus.reported: AegisColors.signal,
  IncidentStatus.triaged: AegisColors.inkMuted,
  IncidentStatus.inProgress: AegisColors.watch,
  IncidentStatus.resolved: AegisColors.nominal,
  IncidentStatus.closed: AegisColors.inkFaint,
  IncidentStatus.duplicate: AegisColors.inkFaint,
  IncidentStatus.rejected: AegisColors.inkFaint,
};

class IncidentStatusText extends StatelessWidget {
  const IncidentStatusText({super.key, required this.status});
  final IncidentStatus status;

  @override
  Widget build(BuildContext context) => Text(
        incidentStatusLabel[status]!,
        style: TextStyle(fontSize: 12, fontWeight: FontWeight.w500, color: _incidentStatusColor[status]),
      );
}

const _workOrderStatusColor = {
  WorkOrderStatus.draft: AegisColors.signal,
  WorkOrderStatus.scheduled: AegisColors.inkMuted,
  WorkOrderStatus.inProgress: AegisColors.watch,
  WorkOrderStatus.completed: AegisColors.nominal,
  WorkOrderStatus.cancelled: AegisColors.inkFaint,
};

class WorkOrderStatusText extends StatelessWidget {
  const WorkOrderStatusText({super.key, required this.status});
  final WorkOrderStatus status;

  @override
  Widget build(BuildContext context) => Text(
        workOrderStatusLabel[status]!,
        style: TextStyle(fontSize: 12, fontWeight: FontWeight.w500, color: _workOrderStatusColor[status]),
      );
}
