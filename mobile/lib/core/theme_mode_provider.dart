import "package:flutter/material.dart";
import "package:flutter_riverpod/flutter_riverpod.dart";

/// Dark by default, for the same reason the web client defaults to dark: this app is read by
/// someone standing at an asset in the field or in an evening control room, not at a desk in
/// daylight.
class ThemeModeNotifier extends Notifier<ThemeMode> {
  @override
  ThemeMode build() => ThemeMode.dark;

  void toggle() => state = state == ThemeMode.dark ? ThemeMode.light : ThemeMode.dark;
}

final themeModeProvider = NotifierProvider<ThemeModeNotifier, ThemeMode>(ThemeModeNotifier.new);
