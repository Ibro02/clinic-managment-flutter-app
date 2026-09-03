import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

import 'design_tokens.dart';

/// Shared visual language for the ClinicNow mobile app.
///
/// Deliberately a near-copy of `clinicnow_desktop/lib/core/app_theme.dart`: the
/// two apps are one product, so a `FilledButton` must not be a different shape
/// on the phone than on the workstation. Keeping the files structurally
/// identical means a diff between them shows only the intended differences.
///
/// The mobile deltas, all marked `MOBILE DELTA` below:
///   1. Controls are [_touchHeight] tall, not [AppSizes.controlHeight] - a 42px
///      button is comfortable with a mouse and cramped with a thumb.
///   2. A `navigationBarTheme`, wired to the nav tokens. Desktop has a rail
///      drawn by hand in its shell and needs no bottom-bar theme.
///   3. No `dataTableTheme`. Mobile renders lists of cards, never a grid.
///
/// Everything else - palette, radii, typography, field treatment - is the same
/// hand-tuned token set, registered as a [ThemeExtension] so `context.colors`
/// resolves correctly in BOTH brightnesses. (Registering it is not optional: the
/// accessor in design_tokens.dart falls back to `AppColors.light`, so a theme
/// that omits the extension silently renders light colors on a dark surface.)
class AppTheme {
  const AppTheme._();

  /// MOBILE DELTA 1: comfortable thumb target, per Material's 48dp guidance.
  static const double _touchHeight = 48;

  static ThemeData light() => _build(AppColors.light, Brightness.light);

  static ThemeData dark() => _build(AppColors.dark, Brightness.dark);

  static ThemeData _build(AppColors c, Brightness brightness) {
    final scheme = ColorScheme.fromSeed(seedColor: c.primary, brightness: brightness).copyWith(
      primary: c.primary,
      onPrimary: c.onPrimary,
      primaryContainer: c.primarySoft,
      onPrimaryContainer: c.primaryStrong,
      secondary: c.primary,
      onSecondary: c.onPrimary,
      surface: c.surface,
      onSurface: c.textPrimary,
      onSurfaceVariant: c.textSecondary,
      surfaceContainerLowest: c.surface,
      surfaceContainerLow: c.surface,
      surfaceContainer: c.surfaceMuted,
      surfaceContainerHigh: c.surfaceMuted,
      surfaceContainerHighest: c.surfaceMuted,
      outline: c.border,
      outlineVariant: c.border,
      error: c.danger,
      onError: Colors.white,
      errorContainer: c.dangerSoft,
      onErrorContainer: c.danger,
      shadow: c.shadowColor,
    );

    final base = ThemeData(colorScheme: scheme, useMaterial3: true, brightness: brightness);
    final textTheme = _textTheme(base.textTheme, c);

    final fieldBorder = OutlineInputBorder(
      borderRadius: AppRadius.all(AppRadius.md),
      borderSide: BorderSide(color: c.border),
    );

    return base.copyWith(
      extensions: <ThemeExtension<dynamic>>[c],
      textTheme: textTheme,
      scaffoldBackgroundColor: c.canvas,
      canvasColor: c.canvas,
      dividerColor: c.border,
      splashFactory: InkSparkle.splashFactory,

      cardTheme: CardThemeData(
        elevation: 0,
        color: c.surface,
        surfaceTintColor: Colors.transparent,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: AppRadius.all(AppRadius.lg),
          side: BorderSide(color: c.border),
        ),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: c.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        barrierColor: c.shadowColor.withValues(alpha: 0.45),
        shape: RoundedRectangleBorder(
          borderRadius: AppRadius.all(AppRadius.xl),
          side: BorderSide(color: c.border),
        ),
        titleTextStyle: textTheme.headlineSmall,
        contentTextStyle: textTheme.bodyMedium,
      ),
      popupMenuTheme: PopupMenuThemeData(
        color: c.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: AppRadius.all(AppRadius.md),
          side: BorderSide(color: c.border),
        ),
        textStyle: textTheme.bodyMedium,
      ),
      menuTheme: MenuThemeData(
        style: MenuStyle(
          backgroundColor: WidgetStatePropertyAll(c.surface),
          surfaceTintColor: const WidgetStatePropertyAll(Colors.transparent),
          elevation: const WidgetStatePropertyAll(0),
          shape: WidgetStatePropertyAll(
            RoundedRectangleBorder(
              borderRadius: AppRadius.all(AppRadius.md),
              side: BorderSide(color: c.border),
            ),
          ),
        ),
      ),
      appBarTheme: AppBarTheme(
        backgroundColor: c.surface,
        surfaceTintColor: Colors.transparent,
        foregroundColor: c.textPrimary,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleTextStyle: textTheme.titleLarge,
      ),
      dividerTheme: DividerThemeData(color: c.border, thickness: 1, space: 1),

      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: c.primary,
          foregroundColor: c.onPrimary,
          disabledBackgroundColor: c.surfaceMuted,
          disabledForegroundColor: c.textMuted,
          elevation: 0,
          minimumSize: const Size(0, _touchHeight),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.md)),
          textStyle: textTheme.labelLarge,
        ).copyWith(
          overlayColor: WidgetStateProperty.resolveWith(
            (states) => states.contains(WidgetState.pressed) ? Colors.black.withValues(alpha: 0.08) : null,
          ),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: c.primary,
          foregroundColor: c.onPrimary,
          elevation: 0,
          minimumSize: const Size(0, _touchHeight),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.md)),
          textStyle: textTheme.labelLarge,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: c.textPrimary,
          backgroundColor: c.surface,
          side: BorderSide(color: c.borderStrong),
          minimumSize: const Size(0, _touchHeight),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.md)),
          textStyle: textTheme.labelLarge,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: c.primary,
          minimumSize: const Size(0, AppSizes.controlHeight),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.sm)),
          textStyle: textTheme.labelLarge,
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          foregroundColor: c.textSecondary,
          highlightColor: c.surfaceHover,
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.sm)),
        ),
      ),
      iconTheme: IconThemeData(color: c.textSecondary, size: 20),

      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: c.surface,
        isDense: true,
        border: fieldBorder,
        enabledBorder: fieldBorder,
        focusedBorder: fieldBorder.copyWith(borderSide: BorderSide(color: c.primary, width: 1.6)),
        errorBorder: fieldBorder.copyWith(borderSide: BorderSide(color: c.danger)),
        focusedErrorBorder: fieldBorder.copyWith(borderSide: BorderSide(color: c.danger, width: 1.6)),
        disabledBorder: fieldBorder.copyWith(borderSide: BorderSide(color: c.border)),
        // MOBILE DELTA 1: taller field box to match the 48px control rhythm.
        contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.md, vertical: AppSpacing.md),
        hintStyle: textTheme.bodyMedium?.copyWith(color: c.textMuted),
        labelStyle: textTheme.bodyMedium?.copyWith(color: c.textSecondary),
        floatingLabelStyle: textTheme.labelMedium?.copyWith(color: c.primary),
        helperStyle: textTheme.bodySmall?.copyWith(color: c.textMuted),
        errorStyle: textTheme.bodySmall?.copyWith(color: c.danger, fontWeight: FontWeight.w500),
        prefixIconColor: c.textMuted,
        suffixIconColor: c.textMuted,
      ),
      checkboxTheme: CheckboxThemeData(
        shape: RoundedRectangleBorder(borderRadius: AppRadius.all(4)),
        side: BorderSide(color: c.borderStrong, width: 1.4),
        fillColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? c.primary : Colors.transparent,
        ),
      ),
      radioTheme: RadioThemeData(
        fillColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? c.primary : c.borderStrong,
        ),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? Colors.white : c.surface,
        ),
        trackColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? c.primary : c.surfaceMuted,
        ),
        trackOutlineColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? Colors.transparent : c.borderStrong,
        ),
      ),

      // MOBILE DELTA 3: no `dataTableTheme` - mobile renders card lists, not grids.

      chipTheme: ChipThemeData(
        backgroundColor: c.surfaceMuted,
        selectedColor: c.primarySoft,
        side: BorderSide(color: c.border),
        elevation: 0,
        pressElevation: 0,
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: AppSpacing.xs),
        labelStyle: textTheme.labelMedium?.copyWith(color: c.textSecondary),
        shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.xs)),
      ),
      listTileTheme: ListTileThemeData(
        shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.sm)),
        iconColor: c.textSecondary,
        titleTextStyle: textTheme.bodyLarge,
        subtitleTextStyle: textTheme.bodySmall?.copyWith(color: c.textMuted),
        contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: AppSpacing.xxs),
      ),
      tabBarTheme: TabBarThemeData(
        labelColor: c.primary,
        unselectedLabelColor: c.textSecondary,
        labelStyle: textTheme.labelLarge,
        unselectedLabelStyle: textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w500),
        indicatorSize: TabBarIndicatorSize.label,
        dividerColor: c.border,
        overlayColor: WidgetStatePropertyAll(c.surfaceHover),
        indicator: UnderlineTabIndicator(
          borderSide: BorderSide(color: c.primary, width: 2),
          insets: const EdgeInsets.symmetric(horizontal: 2),
        ),
      ),

      // MOBILE DELTA 2: the bottom bar is mobile's primary navigation. It reads
      // from the same nav tokens the desktop rail uses, so "selected" means the
      // same blue in both apps.
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: c.surface,
        surfaceTintColor: Colors.transparent,
        indicatorColor: c.primarySoft,
        indicatorShape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.sm)),
        elevation: 0,
        height: 68,
        labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
        iconTheme: WidgetStateProperty.resolveWith(
          (states) => IconThemeData(
            size: 22,
            color: states.contains(WidgetState.selected) ? c.primary : c.textMuted,
          ),
        ),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => textTheme.labelMedium!.copyWith(
            color: states.contains(WidgetState.selected) ? c.primary : c.textMuted,
            fontSize: 12,
          ),
        ),
      ),

      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(color: c.textPrimary, borderRadius: AppRadius.all(AppRadius.xs)),
        textStyle: textTheme.bodySmall?.copyWith(color: c.surface),
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs, vertical: 6),
        waitDuration: const Duration(milliseconds: 400),
      ),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: c.textPrimary,
        contentTextStyle: textTheme.bodyMedium?.copyWith(color: c.surface),
        actionTextColor: c.primaryStrong,
        behavior: SnackBarBehavior.floating,
        elevation: 0,
        shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.md)),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: c.primary,
        linearTrackColor: c.surfaceMuted,
        circularTrackColor: c.surfaceMuted,
        linearMinHeight: 3,
      ),
      scrollbarTheme: ScrollbarThemeData(
        thickness: const WidgetStatePropertyAll(8),
        radius: const Radius.circular(AppRadius.xs),
        thumbColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.hovered)
              ? c.textMuted.withValues(alpha: 0.55)
              : c.textMuted.withValues(alpha: 0.3),
        ),
        crossAxisMargin: 2,
      ),
    );
  }

  /// Inter Tight for headings (tighter tracking gives large text authority),
  /// Inter for body. Identical to desktop's ramp - a heading must not be a
  /// different face on the phone.
  static TextStyle _display({
    required double size,
    required FontWeight weight,
    required double tracking,
    required Color color,
  }) =>
      GoogleFonts.interTight(
        fontSize: size,
        fontWeight: weight,
        letterSpacing: tracking,
        height: 1.2,
        color: color,
      );

  static TextTheme _textTheme(TextTheme base, AppColors c) {
    final body = GoogleFonts.interTextTheme(base);

    return body.copyWith(
      displaySmall: _display(size: 34, weight: FontWeight.w600, tracking: -0.9, color: c.textPrimary),
      headlineLarge: _display(size: 28, weight: FontWeight.w600, tracking: -0.7, color: c.textPrimary),
      headlineMedium: _display(size: 24, weight: FontWeight.w600, tracking: -0.5, color: c.textPrimary),
      headlineSmall: _display(size: 20, weight: FontWeight.w600, tracking: -0.3, color: c.textPrimary),
      titleLarge: GoogleFonts.inter(
        fontSize: 17,
        fontWeight: FontWeight.w600,
        letterSpacing: -0.2,
        height: 1.3,
        color: c.textPrimary,
      ),
      titleMedium: GoogleFonts.inter(fontSize: 15, fontWeight: FontWeight.w600, height: 1.35, color: c.textPrimary),
      titleSmall: GoogleFonts.inter(fontSize: 13.5, fontWeight: FontWeight.w600, height: 1.35, color: c.textPrimary),
      bodyLarge: GoogleFonts.inter(fontSize: 15, fontWeight: FontWeight.w400, height: 1.5, color: c.textPrimary),
      bodyMedium: GoogleFonts.inter(fontSize: 14, fontWeight: FontWeight.w400, height: 1.5, color: c.textPrimary),
      bodySmall: GoogleFonts.inter(fontSize: 13, fontWeight: FontWeight.w400, height: 1.45, color: c.textSecondary),
      labelLarge: GoogleFonts.inter(fontSize: 14, fontWeight: FontWeight.w600, letterSpacing: 0, color: c.textPrimary),
      labelMedium: GoogleFonts.inter(fontSize: 12.5, fontWeight: FontWeight.w600, color: c.textSecondary),
      labelSmall: GoogleFonts.inter(
        fontSize: 11.5,
        fontWeight: FontWeight.w600,
        letterSpacing: AppTypography.eyebrowTracking,
        color: c.textMuted,
      ),
    );
  }
}
