/// Coarse relative age, ported from the web client's `relativeAge` in `lib/utils.ts`. Coarse on
/// purpose: an operator scanning a list needs "3d ago" to judge staleness, not "3 days, 4 hours".
String relativeAge(String? iso) {
  if (iso == null) return "—";

  final then = DateTime.tryParse(iso);
  if (then == null) return "—";

  final elapsed = DateTime.now().toUtc().difference(then.toUtc());
  final minutes = elapsed.inMinutes;

  if (minutes < 1) return "just now";
  if (minutes < 60) return "${minutes}m ago";

  final hours = elapsed.inHours;
  if (hours < 24) return "${hours}h ago";

  final days = elapsed.inDays;
  if (days < 31) return "${days}d ago";

  final months = (days / 30.44).floor();
  if (months < 24) return "${months}mo ago";

  return "${(days / 365.25).floor()}y ago";
}
