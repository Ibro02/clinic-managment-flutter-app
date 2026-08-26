# Visual Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-skin both ClinicNow Flutter apps (`clinicnow_desktop`, `clinicnow_mobile`) with one shared, modern, blue-accented visual system — inspired by vitalflow.framer.website — in both light and dark mode, without changing any screen's structure, navigation, or graded UX behavior.

**Architecture:** One `AppTheme` class per app (`lib/core/app_theme.dart`, near-identical between the two apps but not shared as a package, matching this codebase's existing per-app-duplication convention) builds a Material 3 `ThemeData` from a single blue seed color via `ColorScheme.fromSeed`, with Inter typography (`google_fonts`) and stadium-shaped buttons/softly-rounded cards and inputs. Wiring this into each `MaterialApp` re-skins the large majority of every screen automatically (Material widgets read from the ambient theme); the remaining work is a small, targeted color-clash fix (one hardcoded blue that now collides with the new primary blue, in 3 files) and an editorial pass on the two login screens, the most visible "first impression" screens.

**Tech Stack:** Flutter (Material 3), `google_fonts` (new dependency, both apps).

**Spec:** [docs/superpowers/specs/2026-08-27-visual-redesign-design.md](../specs/2026-08-27-visual-redesign-design.md)

## Global Constraints

- Seed color: `Color(0xFF4586FF)` (the reference site's exact accent blue) — every color in both themes derives from this one seed via `ColorScheme.fromSeed`, never a hand-picked second color.
- Typography: Inter via `google_fonts` (`GoogleFonts.interTextTheme(...)`), matching the reference's "Inter Display" family.
- Shape: buttons (`Filled`/`Elevated`/`Outlined`/`Text`) are fully rounded (`StadiumBorder`); cards/dialogs/inputs use an 18px rounded-rectangle radius — rounder than Material's stock defaults, short of a full pill.
- Dark mode is in scope: `ColorScheme.fromSeed(seedColor: ..., brightness: Brightness.dark)` from the same seed, wired as `darkTheme`, with `themeMode: ThemeMode.system` (no manual toggle).
- No change to navigation structure, `DataTable` columns, dialog field sets, form validation logic, or any backend code. This is a visual-only change (spec §6).
- No shared Dart package between the two apps — `app_theme.dart` is written once and copied/adapted, not extracted into shared infrastructure (spec §4, matches this codebase's existing `RabbitMqPublisherConnectionProvider`-style duplication convention).
- This codebase has no backend involvement in this plan and no automated Flutter test suite beyond each app's existing single smoke test — verification is `flutter analyze` (must stay clean) + `flutter test` (existing tests must keep passing) + a live-run spot check to whatever extent this session's tooling allows (a known, previously-documented limitation — do not block a task on achieving a screenshot that can't be taken here).
- All user-facing text stays Bosnian; all new identifiers/comments stay English (CLAUDE.md §6 language rule).
- **Scope refinement from the spec:** the spec (§5) flagged `clinic_colors.dart` (identical in both apps — a fixed 8-color palette cycled by `locationId` to visually distinguish clinics) as needing "re-picking." Writing this plan found that unnecessary: those 8 colors (indigo/teal/deepOrange/purple/green/blue/brown/pink) are independent Material constants, not derived from the old teal seed, so they don't actually clash with the new blue seed any more than they clashed with teal — re-picking them would be unjustified churn on a working, purely-decorative palette. This plan does not touch `clinic_colors.dart` in either app.
- **Flutter/package API drift authorization**: this plan's `ThemeData` sub-theme code (`CardThemeData`, `DialogThemeData`, etc.) was written against general Material 3 API knowledge, not compiled against the exact installed Flutter 3.44.9. If `flutter analyze` reports a renamed type/property, fix the call site to match what actually compiles, preserving the same visual intent (rounded shape, same color source) — a normal SDK-version adjustment, not a design change, same authorization already used successfully for QuestPDF/fl_chart/printing in Phase 9. The `google_fonts: ^6.2.1` pin (Tasks 1-2) is a similarly best-effort version guess — if it doesn't resolve, bump to whatever version actually resolves against this project's installed Flutter 3.44.9/Dart 3.12.2 (confirmed compatible: `google_fonts`' own minimum requirement is Flutter 3.35/Dart 3.9, comfortably below what's installed).

---

## File Structure

**Desktop (`clinicnow_desktop`) — new files:**
- `lib/core/app_theme.dart`

**Desktop — modified files:**
- `pubspec.yaml` — add `google_fonts`.
- `lib/main.dart` — wire `theme`/`darkTheme`/`themeMode`.
- `lib/screens/appointments/appointment_screen.dart` — one color-clash fix.
- `lib/screens/dashboard/dashboard_screen.dart` — one color-clash fix (pie chart).
- `lib/screens/login_screen.dart` — editorial pass.

**Mobile (`clinicnow_mobile`) — new files:**
- `lib/core/app_theme.dart`

**Mobile — modified files:**
- `pubspec.yaml` — add `google_fonts`.
- `lib/main.dart` — wire `theme`/`darkTheme`/`themeMode`.
- `lib/screens/appointments/my_appointments_screen.dart` — one color-clash fix.
- `lib/screens/appointments/appointment_detail_screen.dart` — one color-clash fix.
- `lib/screens/login_screen.dart` — editorial pass.

---

## Task 1: Desktop shared theme (`AppTheme`, wired into `main.dart`)

**Files:**
- Create: `UI/clinicnow_desktop/lib/core/app_theme.dart`
- Modify: `UI/clinicnow_desktop/pubspec.yaml`
- Modify: `UI/clinicnow_desktop/lib/main.dart`

**Interfaces:**
- Produces: `AppTheme.light() : ThemeData`, `AppTheme.dark() : ThemeData` — consumed by `main.dart`'s `MaterialApp`. Same public shape as Task 2's mobile `AppTheme`, so both apps' `main.dart` wiring is identical.

- [ ] **Step 1: Add `google_fonts` to `pubspec.yaml`**

Add this line to the `dependencies:` block (after `intl: 0.20.2`):

```yaml
  google_fonts: ^6.2.1
```

- [ ] **Step 2: `lib/core/app_theme.dart`**

```dart
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Shared visual language for the ClinicNow desktop app: a single blue seed
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
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: scheme.surfaceContainerLow,
        indicatorShape: _stadium,
      ),
    );
  }
}
```

- [ ] **Step 3: Wire it into `lib/main.dart`**

Change:

```dart
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: Colors.teal),
          useMaterial3: true,
        ),
```

to:

```dart
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
        themeMode: ThemeMode.system,
```

Add the import (with the other relative imports, alphabetically before `layouts/app_shell.dart`):

```dart
import 'core/app_theme.dart';
```

- [ ] **Step 4: Fetch dependencies & analyze**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter pub get && flutter analyze
```
Expected: `google_fonts` resolves, no analyzer issues. If any `ThemeData`-related type in Step 2 doesn't compile against the installed Flutter version, fix it per this plan's Global Constraints API-drift authorization.

- [ ] **Step 5: Commit**

```bash
git add pubspec.yaml pubspec.lock lib/core/app_theme.dart lib/main.dart
git commit -m "feat(redesign): add desktop AppTheme (blue seed, Inter, light+dark)"
```

---

## Task 2: Mobile shared theme (`AppTheme`, wired into `main.dart`)

**Files:**
- Create: `UI/clinicnow_mobile/lib/core/app_theme.dart`
- Modify: `UI/clinicnow_mobile/pubspec.yaml`
- Modify: `UI/clinicnow_mobile/lib/main.dart`

**Interfaces:**
- Produces: `AppTheme.light() : ThemeData`, `AppTheme.dark() : ThemeData` — same shape as Task 1, independent implementation (no shared package, per Global Constraints).

- [ ] **Step 1: Add `google_fonts` to `pubspec.yaml`**

Add this line to the `dependencies:` block (after `intl: 0.20.2`):

```yaml
  google_fonts: ^6.2.1
```

- [ ] **Step 2: `lib/core/app_theme.dart`**

Byte-for-byte the same file as Task 1's `UI/clinicnow_desktop/lib/core/app_theme.dart`, but drop the `navigationRailTheme` block (the mobile app uses a bottom nav bar, not a nav rail) and add a `navigationBarTheme` in its place, since Material 3's `NavigationBar` is what a bottom-nav-using Flutter app should use for its indicator shape to pick up the stadium theme too:

```dart
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
```

- [ ] **Step 3: Wire it into `lib/main.dart`**

Read the actual current file first (it's a peer of the desktop `main.dart` you already changed in Task 1 but is a separate, independent file in a separate app) and apply the equivalent change: find the existing `theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: Colors.teal), useMaterial3: true)` block inside the `MaterialApp(...)` and replace it with:

```dart
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
        themeMode: ThemeMode.system,
```

Add `import 'core/app_theme.dart';` with the other relative imports.

- [ ] **Step 4: Fetch dependencies & analyze**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter pub get && flutter analyze
```
Expected: no issues. Same API-drift authorization as Task 1 applies.

- [ ] **Step 5: Commit**

```bash
git add pubspec.yaml pubspec.lock lib/core/app_theme.dart lib/main.dart
git commit -m "feat(redesign): add mobile AppTheme (blue seed, Inter, light+dark)"
```

---

## Task 3: Desktop color-clash fixes (status chip + dashboard pie chart)

**Files:**
- Modify: `UI/clinicnow_desktop/lib/screens/appointments/appointment_screen.dart`
- Modify: `UI/clinicnow_desktop/lib/screens/dashboard/dashboard_screen.dart`

**Interfaces:**
- Consumes: `AppTheme` (Task 1, already wired — this task only touches call sites that read `Theme.of(context).colorScheme` directly, no new shared type).

**Why this task exists:** two places hardcode a literal blue/teal that now visually collides with (or was literally) the new seed color, now that the theme's primary is blue (`#4586FF`) instead of the old teal. Every OTHER hardcoded status color (orange/green/red/grey) stays exactly as-is — they don't clash with blue and changing them would be unjustified busywork.

- [ ] **Step 1: Fix the "Confirmed" status color in `appointment_screen.dart`**

Find:

```dart
  Color _statusColor(int status) => switch (status) {
        0 => Colors.orange,
        1 => Colors.blue,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };
```

Replace with (add a `BuildContext context` parameter so the method can read the theme's tertiary color — a hue the seed generator picks specifically to sit well alongside primary, avoiding the blue-on-blue clash between a "Confirmed" status chip and the new blue primary buttons):

```dart
  Color _statusColor(BuildContext context, int status) => switch (status) {
        0 => Colors.orange,
        1 => Theme.of(context).colorScheme.tertiary,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };
```

Update its one call site:

```dart
                                DataCell(Chip(
                                  label: Text(appointment.statusName, style: const TextStyle(color: Colors.white, fontSize: 12)),
                                  backgroundColor: _statusColor(appointment.status),
```

to:

```dart
                                DataCell(Chip(
                                  label: Text(appointment.statusName, style: const TextStyle(color: Colors.white, fontSize: 12)),
                                  backgroundColor: _statusColor(context, appointment.status),
```

- [ ] **Step 2: Fix the dashboard's new-vs-existing pie chart colors**

In `dashboard_screen.dart`, find (inside `_NewVsExistingChart.build`):

```dart
        sections: [
          PieChartSectionData(value: newCount.toDouble(), title: 'Novi\n$newCount', color: Colors.teal, radius: 60),
          PieChartSectionData(value: existingCount.toDouble(), title: 'Postojeći\n$existingCount', color: Colors.blueGrey, radius: 60),
        ],
```

Replace with (reads the new theme's own palette instead of the old seed color/an unrelated grey, so both slices are visibly drawn from the same design system as everything else):

```dart
        sections: [
          PieChartSectionData(value: newCount.toDouble(), title: 'Novi\n$newCount', color: Theme.of(context).colorScheme.primary, radius: 60),
          PieChartSectionData(value: existingCount.toDouble(), title: 'Postojeći\n$existingCount', color: Theme.of(context).colorScheme.tertiary, radius: 60),
        ],
```

`_NewVsExistingChart` is a `StatelessWidget` whose `build(BuildContext context)` already has `context` in scope at this exact call site — no signature change needed here (unlike Step 1's `_statusColor`, which was a plain method without an existing `context` parameter).

- [ ] **Step 3: Analyze**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter analyze
```
Expected: no issues.

- [ ] **Step 4: Commit**

```bash
git add lib/screens/appointments/appointment_screen.dart lib/screens/dashboard/dashboard_screen.dart
git commit -m "fix(redesign): resolve blue-on-blue color clash in status chip and dashboard chart"
```

---

## Task 4: Mobile color-clash fixes (status colors)

**Files:**
- Modify: `UI/clinicnow_mobile/lib/screens/appointments/my_appointments_screen.dart`
- Modify: `UI/clinicnow_mobile/lib/screens/appointments/appointment_detail_screen.dart`

**Interfaces:**
- Consumes: `AppTheme` (Task 2, already wired).

- [ ] **Step 1: Fix `my_appointments_screen.dart`**

Find:

```dart
  Color _statusColor(int status) => switch (status) {
        0 => Colors.orange,
        1 => Colors.blue,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };
```

Replace with:

```dart
  Color _statusColor(BuildContext context, int status) => switch (status) {
        0 => Colors.orange,
        1 => Theme.of(context).colorScheme.tertiary,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };
```

Find its one call site:

```dart
                          return ListTile(
                            leading: CircleAvatar(
                              backgroundColor: _statusColor(appointment.status),
                              child: const Icon(Icons.event, color: Colors.white),
                            ),
```

and change it to:

```dart
                          return ListTile(
                            leading: CircleAvatar(
                              backgroundColor: _statusColor(context, appointment.status),
                              child: const Icon(Icons.event, color: Colors.white),
                            ),
```

(`context` is already in scope here — it's the `itemBuilder: (context, index) { ... }` parameter.)

- [ ] **Step 2: Fix `appointment_detail_screen.dart`**

Find:

```dart
  Color _statusColor(int status) => switch (status) {
        0 => Colors.orange,
        1 => Colors.blue,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };
```

Replace with the same pattern as Step 1:

```dart
  Color _statusColor(BuildContext context, int status) => switch (status) {
        0 => Colors.orange,
        1 => Theme.of(context).colorScheme.tertiary,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };
```

Find its call site:

```dart
            Chip(
              label: Text(_appointment.statusName, style: const TextStyle(color: Colors.white)),
              backgroundColor: _statusColor(_appointment.status),
            ),
```

and change it to:

```dart
            Chip(
              label: Text(_appointment.statusName, style: const TextStyle(color: Colors.white)),
              backgroundColor: _statusColor(context, _appointment.status),
            ),
```

(this is inside the screen's top-level `build(BuildContext context)`, so `context` is already in scope). Leave the `Colors.green` payment-status `Chip` (`backgroundColor: Colors.green` for a "Plaćeno" badge) untouched — green doesn't clash with the new blue primary.

- [ ] **Step 3: Analyze**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter analyze
```
Expected: no issues.

- [ ] **Step 4: Commit**

```bash
git add lib/screens/appointments/my_appointments_screen.dart lib/screens/appointments/appointment_detail_screen.dart
git commit -m "fix(redesign): resolve blue-on-blue status color clash on mobile"
```

---

## Task 5: Desktop login screen editorial pass

**Files:**
- Modify: `UI/clinicnow_desktop/lib/screens/login_screen.dart`

**Interfaces:**
- Consumes: `AppTheme` (Task 1). No new types produced — this is a layout/styling-only change to one screen; every field name, validator, and submit behavior is unchanged.

**Why this task exists:** the login screen is the very first thing every user sees, and the spec (§5) calls it out as the highest-value place to actually show the reference's spirit — a centered, generously-padded, editorial-feeling card — not just inherit the theme passively like every other screen.

- [ ] **Step 1: Wrap the form in a themed card with a brand mark and add a subtitle**

Find:

```dart
  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: FormBuilder(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'ClinicNow — Osoblje',
                    style: Theme.of(context).textTheme.headlineSmall,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 24),
```

Replace with:

```dart
  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(32),
                child: FormBuilder(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  CircleAvatar(
                    radius: 28,
                    backgroundColor: colorScheme.primaryContainer,
                    child: Icon(Icons.local_hospital, color: colorScheme.onPrimaryContainer, size: 28),
                  ),
                  const SizedBox(height: 20),
                  Text(
                    'ClinicNow — Osoblje',
                    style: Theme.of(context).textTheme.headlineSmall,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Prijavite se da pristupite administraciji klinike.',
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: colorScheme.onSurfaceVariant),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 24),
```

- [ ] **Step 2: Close the two new wrapping widgets (`Card` and its `Padding`)**

Find the end of the same `build` method:

```dart
                  FilledButton(
                    onPressed: _isSubmitting ? null : _submit,
                    child: _isSubmitting
                        ? const SizedBox(
                            height: 20,
                            width: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Prijava'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
```

Replace with (two extra closing widgets for the `Card`/`Padding` added in Step 1 — indentation of the pre-existing lines is intentionally left as Step 1 produced it; a final `dart format` pass in Step 4 normalizes it):

```dart
                  FilledButton(
                    onPressed: _isSubmitting ? null : _submit,
                    child: _isSubmitting
                        ? const SizedBox(
                            height: 20,
                            width: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Prijava'),
                  ),
                ],
              ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
```

- [ ] **Step 3: Format**

```bash
cd ClinicNow/UI/clinicnow_desktop && dart format lib/screens/login_screen.dart
```
This normalizes the indentation Step 2 intentionally left rough (formatting nested nested widget trees by hand is error-prone; `dart format` is the authoritative, deterministic way to fix indentation without risking a bracket-matching mistake).

- [ ] **Step 4: Analyze**

```bash
flutter analyze
```
Expected: no issues. If a bracket mismatch from Steps 1-2 surfaces here, fix it by carefully re-counting the `Card` → `Padding` → `FormBuilder` nesting introduced in Step 1 against its matching close in Step 2.

- [ ] **Step 5: Commit**

```bash
git add lib/screens/login_screen.dart
git commit -m "feat(redesign): editorial pass on desktop login screen"
```

---

## Task 6: Mobile login screen editorial pass

**Files:**
- Modify: `UI/clinicnow_mobile/lib/screens/login_screen.dart`

**Interfaces:**
- Consumes: `AppTheme` (Task 2). No new types — layout/styling-only, same fields/validators/behavior.

- [ ] **Step 1: Wrap the form in a themed card and add a subtitle**

Find:

```dart
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.local_hospital, size: 56),
                  const SizedBox(height: 12),
                  Text(
                    'ClinicNow',
                    style: Theme.of(context).textTheme.headlineMedium,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 32),
```

Replace with:

```dart
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  CircleAvatar(
                    radius: 36,
                    backgroundColor: Theme.of(context).colorScheme.primaryContainer,
                    child: Icon(Icons.local_hospital, color: Theme.of(context).colorScheme.onPrimaryContainer, size: 36),
                  ),
                  const SizedBox(height: 16),
                  Text(
                    'ClinicNow',
                    style: Theme.of(context).textTheme.headlineMedium,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Zakažite pregled u nekoliko koraka.',
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 32),
```

Then wrap the whole `FormBuilder` in a `Card` the same way Task 5 did: find

```dart
            padding: const EdgeInsets.all(24),
            child: FormBuilder(
              key: _formKey,
```

and replace with:

```dart
            padding: const EdgeInsets.all(24),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(28),
                child: FormBuilder(
              key: _formKey,
```

- [ ] **Step 2: Close the two new wrapping widgets**

Find the end of `build`:

```dart
                  TextButton(
                    onPressed: _isSubmitting
                        ? null
                        : () => Navigator.of(context).push(
                              MaterialPageRoute(
                                  builder: (_) => const RegisterScreen()),
                            ),
                    child: const Text('Nemate nalog? Registrujte se'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
```

Replace with:

```dart
                  TextButton(
                    onPressed: _isSubmitting
                        ? null
                        : () => Navigator.of(context).push(
                              MaterialPageRoute(
                                  builder: (_) => const RegisterScreen()),
                            ),
                    child: const Text('Nemate nalog? Registrujte se'),
                  ),
                ],
              ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
```

- [ ] **Step 3: Format**

```bash
cd ClinicNow/UI/clinicnow_mobile && dart format lib/screens/login_screen.dart
```

- [ ] **Step 4: Analyze**

```bash
flutter analyze
```
Expected: no issues. Same bracket-matching note as Task 5 Step 4 applies.

- [ ] **Step 5: Commit**

```bash
git add lib/screens/login_screen.dart
git commit -m "feat(redesign): editorial pass on mobile login screen"
```

---

## Task 7: Final verification across both apps

**Files:** none — this task only runs checks.

**Interfaces:** consumes everything from Tasks 1-6.

- [ ] **Step 1: Analyze + test both apps**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter analyze && flutter test
cd ClinicNow/UI/clinicnow_mobile && flutter analyze && flutter test
```
Expected: "No issues found!" and the existing single smoke test passing on both — this redesign must not break either app's existing (pre-Task-1) test.

- [ ] **Step 2: Confirm no leftover hardcoded seed-color references**

```bash
cd ClinicNow/UI && grep -rn "Colors.teal" clinicnow_desktop/lib clinicnow_mobile/lib
```
Expected: no matches in either app's `lib/` (the old seed color and the dashboard's `Colors.teal` pie-chart slice were the only two `Colors.teal` usages before this plan; Task 3 removed the pie-chart one and Tasks 1-2 removed the seed one). If this finds a real remaining usage neither Task 1-3 anticipated, read it in context and decide whether it's a genuine leftover to fix or an unrelated, correctly-still-teal usage (e.g. a `clinic_colors.dart` palette entry, which is explicitly out of scope per the plan's Global Constraints) — do not blindly remove every match.

- [ ] **Step 3: Live-run spot check (best-effort)**

Run `clinicnow_desktop` (`flutter run -d chrome` or `-d windows` if available) and log in as `staff@clinicnow.test`/`test`; separately run `clinicnow_mobile` and log in as `patient@clinicnow.test`/`test`. Confirm to whatever extent this session's tooling allows: the login screens show the new card/brand-mark layout, buttons across both apps render as fully-rounded pills, and toggling the OS/browser's dark-mode preference switches both apps into a dark blue-seeded theme rather than the old default. This session has a previously-documented limitation rendering/screenshotting a live Flutter app — if that blocks a full visual confirmation, say so explicitly in this task's report rather than asserting a visual result that wasn't actually observed (same standard applied in Phase 8/9's own live-verification tasks).

- [ ] **Step 4: Commit** (only if Steps 1-3 required any fix-up changes not already committed by Tasks 1-6; otherwise this task has nothing new to commit and that's fine)
