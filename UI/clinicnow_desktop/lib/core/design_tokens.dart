import 'dart:ui' show FontFeature;

import 'package:flutter/material.dart';

/// ---------------------------------------------------------------------------
/// ClinicNow design tokens
/// ---------------------------------------------------------------------------
/// Every visual decision resolves to a value in this file. Screens never
/// hard-code a `Color(0x...)`, a raw padding number, or a radius - they read
/// `context.colors`, `AppSpacing`, `AppRadius`.
///
/// Why not `ColorScheme.fromSeed`? The Material 3 tonal algorithm derives every
/// surface from one hue, which produces faintly purple, low-contrast greys that
/// make an app read as "a Flutter app" rather than as a product. The palette
/// below is hand-tuned: neutrals stay neutral, and blue appears only where it
/// carries meaning.
/// ---------------------------------------------------------------------------

/// 4px baseline grid.
abstract final class AppSpacing {
  static const double xxs = 4;
  static const double xs = 8;
  static const double sm = 12;
  static const double md = 16;
  static const double lg = 24;
  static const double xl = 32;
  static const double xxl = 48;

  static const EdgeInsets page = EdgeInsets.symmetric(horizontal: xl, vertical: lg);
  static const EdgeInsets card = EdgeInsets.all(lg);
}

/// Corner radii. Deliberately small - stadium buttons read as playful, and this
/// is a tool people use for eight hours a day.
abstract final class AppRadius {
  static const double xs = 6;
  static const double sm = 8;
  static const double md = 10;
  static const double lg = 14;
  static const double xl = 20;
  static const double pill = 999;

  static BorderRadius all(double r) => BorderRadius.circular(r);
}

abstract final class AppDuration {
  static const fast = Duration(milliseconds: 120);
  static const normal = Duration(milliseconds: 200);
  static const slow = Duration(milliseconds: 320);
  static const curve = Curves.easeOutCubic;
}

abstract final class AppSizes {
  static const double sidebarExpanded = 268;
  static const double sidebarCollapsed = 80;
  static const double topBarHeight = 68;
  static const double controlHeight = 42;
  static const double controlHeightSm = 34;
  static const double tableRowHeight = 54;
  static const double tableHeaderHeight = 44;
}

/// ---------------------------------------------------------------------------
/// Colors
/// ---------------------------------------------------------------------------
@immutable
class AppColors extends ThemeExtension<AppColors> {
  // Surfaces
  final Color canvas;
  final Color surface;
  final Color surfaceMuted;
  final Color surfaceHover;

  // Lines
  final Color border;
  final Color borderStrong;

  // Text
  final Color textPrimary;
  final Color textSecondary;
  final Color textMuted;

  // Brand
  final Color primary;
  final Color primaryStrong;
  final Color primarySoft;
  final Color onPrimary;

  // Status: a solid tone (text, icons, dots) and a soft tone (badge
  // backgrounds), so status chips never need ad-hoc opacity maths.
  final Color success;
  final Color successSoft;
  final Color warning;
  final Color warningSoft;
  final Color danger;
  final Color dangerSoft;
  final Color info;
  final Color infoSoft;
  final Color neutral;
  final Color neutralSoft;

  // Navigation rail. Separate from surface tokens because the rail is dark in
  // both themes - it is the signature element, not a surface.
  final Color navTop;
  final Color navBottom;
  final Color navBorder;
  final Color navText;
  final Color navTextMuted;
  final Color navItemHover;
  final Color navItemSelected;
  final Color navItemSelectedText;

  // Charts
  final List<Color> chartSeries;
  final Color chartGrid;

  final Color shadowColor;

  const AppColors({
    required this.canvas,
    required this.surface,
    required this.surfaceMuted,
    required this.surfaceHover,
    required this.border,
    required this.borderStrong,
    required this.textPrimary,
    required this.textSecondary,
    required this.textMuted,
    required this.primary,
    required this.primaryStrong,
    required this.primarySoft,
    required this.onPrimary,
    required this.success,
    required this.successSoft,
    required this.warning,
    required this.warningSoft,
    required this.danger,
    required this.dangerSoft,
    required this.info,
    required this.infoSoft,
    required this.neutral,
    required this.neutralSoft,
    required this.navTop,
    required this.navBottom,
    required this.navBorder,
    required this.navText,
    required this.navTextMuted,
    required this.navItemHover,
    required this.navItemSelected,
    required this.navItemSelectedText,
    required this.chartSeries,
    required this.chartGrid,
    required this.shadowColor,
  });

  static const AppColors light = AppColors(
    canvas: Color(0xFFF4F6F9),
    surface: Color(0xFFFFFFFF),
    surfaceMuted: Color(0xFFF1F4F8),
    surfaceHover: Color(0xFFF7F9FC),
    border: Color(0xFFE4E8EF),
    borderStrong: Color(0xFFD2D8E2),
    textPrimary: Color(0xFF0D1523),
    textSecondary: Color(0xFF5B6779),
    textMuted: Color(0xFF8B95A7),
    primary: Color(0xFF4586FF),
    primaryStrong: Color(0xFF2F6BE0),
    primarySoft: Color(0xFFEBF2FF),
    onPrimary: Color(0xFFFFFFFF),
    success: Color(0xFF12A150),
    successSoft: Color(0xFFE6F6EE),
    warning: Color(0xFFB86E00),
    warningSoft: Color(0xFFFDF2E2),
    danger: Color(0xFFDC3A3F),
    dangerSoft: Color(0xFFFDECEC),
    info: Color(0xFF4586FF),
    infoSoft: Color(0xFFEBF2FF),
    neutral: Color(0xFF5B6779),
    neutralSoft: Color(0xFFF1F4F8),
    navTop: Color(0xFF101828),
    navBottom: Color(0xFF0B111C),
    navBorder: Color(0xFF1E2838),
    navText: Color(0xFFC6CEDC),
    navTextMuted: Color(0xFF77839A),
    navItemHover: Color(0xFF1A2434),
    navItemSelected: Color(0xFF1D2E4E),
    navItemSelectedText: Color(0xFFFFFFFF),
    chartSeries: [
      Color(0xFF4586FF), // blue - primary series
      Color(0xFF12A150), // green
      Color(0xFF8B5CF6), // violet
      Color(0xFFF59E0B), // amber
      Color(0xFF06B6D4), // cyan
      Color(0xFFEC4899), // pink
      Color(0xFF64748B), // slate - "other"
    ],
    chartGrid: Color(0xFFEDF0F5),
    shadowColor: Color(0xFF0D1523),
  );

  static const AppColors dark = AppColors(
    canvas: Color(0xFF0B0F17),
    surface: Color(0xFF131924),
    surfaceMuted: Color(0xFF1A2130),
    surfaceHover: Color(0xFF1C2434),
    border: Color(0xFF232C3C),
    borderStrong: Color(0xFF313B4E),
    textPrimary: Color(0xFFE9EDF5),
    textSecondary: Color(0xFF9CA7BA),
    textMuted: Color(0xFF6C7889),
    primary: Color(0xFF5B93FF),
    primaryStrong: Color(0xFF7EACFF),
    primarySoft: Color(0xFF17233B),
    onPrimary: Color(0xFF07101F),
    success: Color(0xFF35C47B),
    successSoft: Color(0xFF10241B),
    warning: Color(0xFFE0A040),
    warningSoft: Color(0xFF2A2013),
    danger: Color(0xFFF06A6F),
    dangerSoft: Color(0xFF2C1719),
    info: Color(0xFF5B93FF),
    infoSoft: Color(0xFF17233B),
    neutral: Color(0xFF9CA7BA),
    neutralSoft: Color(0xFF1A2130),
    navTop: Color(0xFF0E141F),
    navBottom: Color(0xFF090D15),
    navBorder: Color(0xFF1C2432),
    navText: Color(0xFFBFC8D8),
    navTextMuted: Color(0xFF6C7889),
    navItemHover: Color(0xFF171F2C),
    navItemSelected: Color(0xFF1A2A47),
    navItemSelectedText: Color(0xFFFFFFFF),
    chartSeries: [
      Color(0xFF5B93FF),
      Color(0xFF35C47B),
      Color(0xFFA78BFA),
      Color(0xFFFBBF24),
      Color(0xFF22D3EE),
      Color(0xFFF472B6),
      Color(0xFF94A3B8),
    ],
    chartGrid: Color(0xFF1D2534),
    shadowColor: Color(0xFF000000),
  );

  /// Hairline lift - never Material's default elevation, which spreads a grey
  /// blur that muddies a light canvas.
  List<BoxShadow> get shadowSm => [
        BoxShadow(color: shadowColor.withValues(alpha: 0.04), blurRadius: 2, offset: const Offset(0, 1)),
      ];

  List<BoxShadow> get shadowMd => [
        BoxShadow(color: shadowColor.withValues(alpha: 0.05), blurRadius: 8, offset: const Offset(0, 2)),
        BoxShadow(color: shadowColor.withValues(alpha: 0.03), blurRadius: 2, offset: const Offset(0, 1)),
      ];

  List<BoxShadow> get shadowLg => [
        BoxShadow(color: shadowColor.withValues(alpha: 0.14), blurRadius: 40, offset: const Offset(0, 16)),
        BoxShadow(color: shadowColor.withValues(alpha: 0.06), blurRadius: 8, offset: const Offset(0, 2)),
      ];

  Color series(int index) => chartSeries[index % chartSeries.length];

  @override
  AppColors copyWith() => this;

  @override
  AppColors lerp(ThemeExtension<AppColors>? other, double t) {
    if (other is! AppColors) return this;
    return t < 0.5 ? this : other;
  }
}

/// ---------------------------------------------------------------------------
/// Typography helpers
/// ---------------------------------------------------------------------------
/// Three roles: display (Inter Tight, tight tracking), body (Inter), and data
/// (Inter with tabular figures).
///
/// The data role matters more than it looks. Staff scan columns of times and
/// amounts all day; proportional digits make those columns ragged. Tabular
/// figures line them up, which reads as precision without anyone noticing why.
abstract final class AppTypography {
  static const List<FontFeature> tabular = [FontFeature.tabularFigures()];
  static const double eyebrowTracking = 0.6;
}

extension AppColorsX on BuildContext {
  AppColors get colors => Theme.of(this).extension<AppColors>() ?? AppColors.light;

  TextTheme get text => Theme.of(this).textTheme;

  bool get isDarkMode => Theme.of(this).brightness == Brightness.dark;
}
