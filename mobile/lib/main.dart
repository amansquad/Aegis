import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

import "core/router.dart";
import "core/theme.dart";
import "core/theme_mode_provider.dart";

void main() {
  runApp(const ProviderScope(child: AegisApp()));
}

class AegisApp extends ConsumerWidget {
  const AegisApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);
    final themeMode = ref.watch(themeModeProvider);

    return MaterialApp.router(
      title: "Aegis",
      debugShowCheckedModeBanner: false,
      themeMode: themeMode,
      theme: aegisLightTheme(),
      darkTheme: aegisDarkTheme(),
      routerConfig: router,
    );
  }
}
