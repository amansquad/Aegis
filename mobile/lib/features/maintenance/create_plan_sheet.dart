import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "../../core/api_client.dart";
import "../../core/theme.dart";
import "../../data/api_providers.dart";
import "../../models/asset.dart";

class CreatePlanSheet extends ConsumerStatefulWidget {
  const CreatePlanSheet({super.key});

  @override
  ConsumerState<CreatePlanSheet> createState() => _CreatePlanSheetState();
}

class _CreatePlanSheetState extends ConsumerState<CreatePlanSheet> {
  final _assetSearchController = TextEditingController();
  final _titleController = TextEditingController();
  final _frequencyController = TextEditingController(text: "90");
  Asset? _selectedAsset;
  List<Asset> _assetResults = [];
  bool _busy = false;
  String? _error;

  Future<void> _searchAssets(String term) async {
    if (term.trim().isEmpty) {
      setState(() => _assetResults = []);
      return;
    }
    final page = await ref
        .read(assetRepositoryProvider)
        .list(searchTerm: term.trim(), pageSize: 6);
    if (mounted) setState(() => _assetResults = page.items);
  }

  Future<void> _submit() async {
    if (_selectedAsset == null) {
      setState(() => _error = "Choose an asset first.");
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await ref.read(maintenanceRepositoryProvider).create(
            assetId: _selectedAsset!.id,
            title: _titleController.text.trim(),
            frequencyDays: int.tryParse(_frequencyController.text) ?? 90,
          );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (error) {
      setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _assetSearchController.dispose();
    _titleController.dispose();
    _frequencyController.dispose();
    super.dispose();
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
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text("New maintenance plan", style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
            const SizedBox(height: 16),
            if (_selectedAsset != null)
              ListTile(
                contentPadding: EdgeInsets.zero,
                tileColor: AegisColors.raised,
                title: Text(_selectedAsset!.name),
                subtitle: Text(_selectedAsset!.code),
                trailing: IconButton(
                  icon: const Icon(Icons.close, size: 18),
                  onPressed: () => setState(() {
                    _selectedAsset = null;
                    _assetSearchController.clear();
                  }),
                ),
              )
            else ...[
              TextField(
                controller: _assetSearchController,
                decoration: const InputDecoration(labelText: "Asset", hintText: "e.g. HYD-NW-0042"),
                onChanged: _searchAssets,
              ),
              for (final asset in _assetResults)
                ListTile(
                  dense: true,
                  contentPadding: EdgeInsets.zero,
                  title: Text(asset.name),
                  subtitle: Text(asset.code),
                  onTap: () => setState(() {
                    _selectedAsset = asset;
                    _assetResults = [];
                  }),
                ),
            ],
            const SizedBox(height: 8),
            TextField(
              controller: _titleController,
              maxLength: 200,
              decoration: const InputDecoration(labelText: "Title", hintText: "e.g. Quarterly valve inspection"),
            ),
            TextField(
              controller: _frequencyController,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: "Frequency (days)"),
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
                  : const Text("Create plan"),
            ),
          ],
        ),
      ),
    );
  }
}
