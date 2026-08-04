import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/format.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../models/asset.dart";
import "../../models/common.dart";
import "../../widgets/badges.dart";
import "../../widgets/states.dart";

class AssetsScreen extends ConsumerStatefulWidget {
  const AssetsScreen({super.key});

  @override
  ConsumerState<AssetsScreen> createState() => _AssetsScreenState();
}

class _AssetsScreenState extends ConsumerState<AssetsScreen> {
  final _searchController = TextEditingController();
  AssetStatus? _status;
  AssetType? _type;
  Future<PagedResult<Asset>>? _future;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    setState(() {
      _future = ref.read(assetRepositoryProvider).list(
            searchTerm: _searchController.text.trim(),
            status: _status,
            type: _type,
            pageSize: 50,
          );
    });
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
          child: TextField(
            controller: _searchController,
            decoration: const InputDecoration(
              prefixIcon: Icon(Icons.search, size: 20),
              hintText: "Search by code or name",
              isDense: true,
            ),
            onSubmitted: (_) => _load(),
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Row(
            children: [
              _FilterChipMenu<AssetStatus>(
                label: "Status",
                value: _status,
                labels: statusLabel,
                onChanged: (v) {
                  _status = v;
                  _load();
                },
              ),
              const SizedBox(width: 8),
              _FilterChipMenu<AssetType>(
                label: "Type",
                value: _type,
                labels: typeLabel,
                onChanged: (v) {
                  _type = v;
                  _load();
                },
              ),
            ],
          ),
        ),
        const SizedBox(height: 8),
        Expanded(
          child: FutureBuilder<PagedResult<Asset>>(
            future: _future,
            builder: (context, snapshot) {
              if (snapshot.connectionState == ConnectionState.waiting) return const LoadingList();

              if (snapshot.hasError) {
                final message =
                    snapshot.error is ApiException ? (snapshot.error as ApiException).message : "Unexpected error.";
                return ErrorView(message: message, onRetry: _load);
              }

              final items = snapshot.data?.items ?? [];

              if (items.isEmpty) {
                return const EmptyView(
                  title: "No assets match this view",
                  description: "Try clearing the search or filters.",
                );
              }

              return RefreshIndicator(
                onRefresh: () async => _load(),
                child: ListView.separated(
                  padding: const EdgeInsets.all(16),
                  itemCount: items.length,
                  separatorBuilder: (_, _) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final asset = items[index];
                    return ListTile(
                      contentPadding: EdgeInsets.zero,
                      title: Text(asset.name, maxLines: 1, overflow: TextOverflow.ellipsis),
                      subtitle: Text(
                        "${asset.code} · ${typeLabel[asset.type]}",
                        style: const TextStyle(fontSize: 12, color: AegisColors.inkFaint),
                      ),
                      trailing: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          ConditionBadge(condition: asset.condition),
                          const SizedBox(height: 4),
                          Text(
                            relativeAge(asset.lastInspectedOnUtc),
                            style: const TextStyle(fontSize: 10, color: AegisColors.inkFaint),
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
    );
  }
}

class _FilterChipMenu<T> extends StatelessWidget {
  const _FilterChipMenu({
    required this.label,
    required this.value,
    required this.labels,
    required this.onChanged,
  });

  final String label;
  final T? value;
  final Map<T, String> labels;
  final ValueChanged<T?> onChanged;

  @override
  Widget build(BuildContext context) {
    return PopupMenuButton<T?>(
      onSelected: onChanged,
      itemBuilder: (context) => [
        PopupMenuItem<T?>(value: null, child: const Text("Any")),
        for (final entry in labels.entries) PopupMenuItem<T?>(value: entry.key, child: Text(entry.value)),
      ],
      child: Chip(
        label: Text(value != null ? labels[value]! : label),
        avatar: value != null ? null : const Icon(Icons.filter_list, size: 16),
        backgroundColor: value != null ? AegisColors.signalDim : null,
      ),
    );
  }
}
