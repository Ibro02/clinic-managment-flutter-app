import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Shared visual language for the ClinicNow mobile app: a single blue seed
/// (#4586FF, the exact accent from vitalflow.framer.website - see
/// docs/superpowers/specs/2026-08-27-visual-redesign-design.md), Inter
/// typography, fully-rounded ("stadium") buttons, and softly-rounded
/// cards/dialogs/inputs. Both light() and dark() derive from the SAME seed via
/// ColorScheme.fromSeed - never a hand-picked second palette.
class AppTheme {
  static const _seed = Color(0xFF4586FF);
  static const _cardRadius = 18.0;
  static const _fieldRadius = 16.0;
  static const _stadium = StadiumBorder();

  static ThemeData light() => _build(ColorScheme.fromSeed(seedColor: _seed, brightness: Brightness.light));

  static ThemeData dark() => _build(ColorScheme.fromSeed(seedColor: _seed, brightness: Brightness.dark));

  static ThemeData _build(ColorScheme scheme) {
    final base = ThemeData(colorScheme: scheme, useMaterial3: true, brightness: scheme.brightness);
    final buttonPadding = const EdgeInsets.symmetric(horizontal: 24, vertical: 14);
    final buttonTextStyle = GoogleFonts.inter(fontWeight: FontWeight.w600);
    final cardShape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(_cardRadius));
    final fieldBorder = OutlineInputBorder(
      borderRadius: BorderRadius.circular(_fieldRadius),
      borderSide: BorderSide.none,
    );

    return base.copyWith(
      textTheme: GoogleFonts.interTextTheme(base.textTheme).copyWith(
        headlineMedium: GoogleFonts.inter(fontSize: 30, fontWeight: FontWeight.w500, color: scheme.onSurface),
        headlineSmall: GoogleFonts.inter(fontSize: 24, fontWeight: FontWeight.w500, color: scheme.onSurface),
        titleLarge: GoogleFonts.inter(fontSize: 20, fontWeight: FontWeight.w600, color: scheme.onSurface),
      ),
      scaffoldBackgroundColor: scheme.surface,
      cardTheme: CardThemeData(
        elevation: 0,
        color: scheme.surfaceContainerLow,
        shape: cardShape,
        margin: const EdgeInsets.all(8),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(shape: _stadium, padding: buttonPadding, textStyle: buttonTextStyle),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(shape: _stadium, padding: buttonPadding, textStyle: buttonTextStyle),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(shape: _stadium, padding: buttonPadding, textStyle: buttonTextStyle),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(shape: _stadium, textStyle: buttonTextStyle),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: scheme.surfaceContainerHighest.withValues(alpha: 0.4),
        border: fieldBorder,
        enabledBorder: fieldBorder,
        focusedBorder: fieldBorder.copyWith(borderSide: BorderSide(color: scheme.primary, width: 2)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      ),
      dialogTheme: DialogThemeData(shape: cardShape),
      chipTheme: base.chipTheme.copyWith(
        shape: _stadium,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: scheme.surface,
        indicatorShape: _stadium,
      ),
    );
  }
}
