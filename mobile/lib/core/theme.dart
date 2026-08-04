import "package:flutter/material.dart";

/// The control-room visual world, ported from the web client's `globals.css` token set.
///
/// Dark is the default for the same reason it is on the web: a technician standing next to a
/// pump station at night, or a dispatcher's control room, is not a bright-screen environment.
/// The same five status hues carry the same five meanings on both surfaces, because a colour
/// that means "failed" on the web and something else on the phone would make the two clients
/// feel like different products describing the same world differently.
class AegisColors {
  const AegisColors._();

  static const void_ = Color(0xFF07090C);
  static const surface = Color(0xFF0D1117);
  static const raised = Color(0xFF141A22);
  static const overlay = Color(0xFF1B232D);

  static const line = Color(0xFF222B36);
  static const lineStrong = Color(0xFF303C4A);

  static const ink = Color(0xFFE8EEF6);
  static const inkMuted = Color(0xFF9AA8BA);
  static const inkFaint = Color(0xFF71809A);

  static const signal = Color(0xFF38BDF8);
  static const nominal = Color(0xFF34D399);
  static const watch = Color(0xFFFBBF24);
  static const degraded = Color(0xFFFB923C);
  static const failed = Color(0xFFF87171);

  static const signalDim = Color(0xFF082F49);
  static const nominalDim = Color(0xFF05271C);
  static const watchDim = Color(0xFF332506);
  static const degradedDim = Color(0xFF351A07);
  static const failedDim = Color(0xFF3A1417);

  // Light theme: hues re-picked for contrast on a near-white surface, the same choice the web
  // client makes -- an inverted dark palette fails contrast rather than reading correctly.
  static const lightVoid = Color(0xFFEEF2F7);
  static const lightSurface = Color(0xFFFFFFFF);
  static const lightRaised = Color(0xFFF6F8FC);
  static const lightLine = Color(0xFFDDE4EE);
  static const lightLineStrong = Color(0xFFC0CAD9);
  static const lightInk = Color(0xFF0F172A);
  static const lightInkMuted = Color(0xFF4D5D75);
  static const lightInkFaint = Color(0xFF6B7A92);

  static const lightSignal = Color(0xFF0369A1);
  static const lightNominal = Color(0xFF047857);
  static const lightWatch = Color(0xFF92620A);
  static const lightDegraded = Color(0xFFC2410C);
  static const lightFailed = Color(0xFFB91C1C);

  static const lightSignalDim = Color(0xFFDBEAFE);
  static const lightNominalDim = Color(0xFFD1FAE5);
  static const lightWatchDim = Color(0xFFFDF0CE);
  static const lightDegradedDim = Color(0xFFFFEDD5);
  static const lightFailedDim = Color(0xFFFEE2E2);
}

/// Numerals an operator reads as measurement -- asset codes, counts, timestamps -- use a
/// monospace face so nothing jitters as values tick, matching the web client's `.tabular` rule.
const aegisTabularStyle = TextStyle(fontFeatures: [FontFeature.tabularFigures()]);

ThemeData aegisDarkTheme() {
  const scheme = ColorScheme.dark(
    surface: AegisColors.surface,
    primary: AegisColors.signal,
    onPrimary: AegisColors.void_,
    error: AegisColors.failed,
    onSurface: AegisColors.ink,
  );

  return ThemeData(
    useMaterial3: true,
    brightness: Brightness.dark,
    colorScheme: scheme,
    scaffoldBackgroundColor: AegisColors.void_,
    cardColor: AegisColors.surface,
    dividerColor: AegisColors.line,
    appBarTheme: const AppBarTheme(
      backgroundColor: AegisColors.surface,
      foregroundColor: AegisColors.ink,
      elevation: 0,
      scrolledUnderElevation: 0,
    ),
    cardTheme: CardThemeData(
      color: AegisColors.surface,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: const BorderSide(color: AegisColors.line),
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: AegisColors.raised,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(7),
        borderSide: const BorderSide(color: AegisColors.lineStrong),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(7),
        borderSide: const BorderSide(color: AegisColors.lineStrong),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(7),
        borderSide: const BorderSide(color: AegisColors.signal),
      ),
      hintStyle: const TextStyle(color: AegisColors.inkFaint),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: AegisColors.signal,
        foregroundColor: AegisColors.void_,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(7)),
        padding: const EdgeInsets.symmetric(vertical: 14),
      ),
    ),
    textTheme: const TextTheme(
      bodyMedium: TextStyle(color: AegisColors.ink),
      bodySmall: TextStyle(color: AegisColors.inkMuted),
    ),
  );
}

ThemeData aegisLightTheme() {
  const scheme = ColorScheme.light(
    surface: AegisColors.lightSurface,
    primary: AegisColors.lightSignal,
    onPrimary: Colors.white,
    error: AegisColors.lightFailed,
    onSurface: AegisColors.lightInk,
  );

  return ThemeData(
    useMaterial3: true,
    brightness: Brightness.light,
    colorScheme: scheme,
    scaffoldBackgroundColor: AegisColors.lightVoid,
    cardColor: AegisColors.lightSurface,
    dividerColor: AegisColors.lightLine,
    appBarTheme: const AppBarTheme(
      backgroundColor: AegisColors.lightSurface,
      foregroundColor: AegisColors.lightInk,
      elevation: 0,
      scrolledUnderElevation: 0,
    ),
    cardTheme: CardThemeData(
      color: AegisColors.lightSurface,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: const BorderSide(color: AegisColors.lightLine),
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: AegisColors.lightRaised,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(7),
        borderSide: const BorderSide(color: AegisColors.lightLineStrong),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(7),
        borderSide: const BorderSide(color: AegisColors.lightLineStrong),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(7),
        borderSide: const BorderSide(color: AegisColors.lightSignal),
      ),
      hintStyle: const TextStyle(color: AegisColors.lightInkFaint),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: AegisColors.lightSignal,
        foregroundColor: Colors.white,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(7)),
        padding: const EdgeInsets.symmetric(vertical: 14),
      ),
    ),
    textTheme: const TextTheme(
      bodyMedium: TextStyle(color: AegisColors.lightInk),
      bodySmall: TextStyle(color: AegisColors.lightInkMuted),
    ),
  );
}
