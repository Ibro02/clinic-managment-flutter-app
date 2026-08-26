# Visual Redesign — Design

> Companion to `CLAUDE.md`/`GOALS.md`/`PLAN.md`. Validated via brainstorming with Ibrahim on 2026-08-27. Covers both Flutter apps (`clinicnow_desktop`, `clinicnow_mobile`).

## 1. Purpose

Replace the current stock-Material look of both apps with one coherent, modern visual system — blue-accented, generously spaced, softly rounded — inspired by [vitalflow.framer.website/about-us](https://vitalflow.framer.website/about-us), adapted for a data-heavy staff desktop app and a booking-focused patient mobile app rather than copied as a marketing page. Every graded UX behavior (dropdowns from DB, confirm dialogs, disabled-with-reason, master-detail, capped pagination, etc. — CLAUDE.md Part II §K) is preserved exactly; only the visual skin changes.

## 2. Key decisions (from brainstorming)

| Decision | Choice | Why |
|---|---|---|
| Rollout scope | **Whole app, one coherent pass** (not staged) | Ibrahim's explicit choice — avoids a patchwork look where some screens are redesigned and others aren't. |
| Reference tokens | Extracted live from the reference site (see §3) rather than approximated from memory | Confirmed via computed-style inspection: accent `#4586FF`, Inter Display typeface, fully-rounded ("stadium") buttons, near-black-on-white text, generous whitespace. |
| Theming mechanism | **One shared `ThemeData` recipe** (`ColorScheme.fromSeed`, Material 3) applied via each app's own `MaterialApp.theme`/`darkTheme` | Flutter's Material widgets read color/type/shape from the ambient theme automatically — a correct theme re-skins most of the UI in one change; the remaining work is auditing screens for hardcoded styles that bypass it. |
| Dark mode | **In scope**, generated from the same blue seed via `ColorScheme.fromSeed(..., brightness: Brightness.dark)`, `ThemeMode.system` (follows the OS setting, no manual in-app toggle) | Ibrahim confirmed dark mode should stay in scope. M3's seed-based generation produces a correct, accessible dark palette without hand-picking colors — one seed, two schemes. `ThemeMode.system` matches modern app convention and needs no new settings UI (YAGNI — nothing in the brief asked for a manual toggle). |
| Desktop vs. mobile treatment | **Same tokens, different density.** Mobile leans fully into the reference's spacious, card-based, big-headline spirit (it's mostly single-column flows). Desktop keeps the same colors/type/shape but stays information-dense — `DataTable`s and forms get the softer visual treatment without pretending to be a marketing page. | A staff app showing 50 appointments can't use vitalflow's whitespace budget; a patient booking a single appointment can. |
| What's NOT redesigned | Layout structure, navigation patterns (nav rail on desktop, bottom nav on mobile), `DataTable` row/column structure, dialog flows, form field sets, validation UX | Explicitly out of scope — this is a visual skin change, not a UX/IA rework. Rebuilding structure risks the graded UX behaviors this project has spent 9 phases getting right. |
| Visual verification | **Mechanical verification only** (`flutter analyze`/`flutter test` clean, no visual regressions in logic), plus Ibrahim's own eyes on the running app at the end | This session's browser tooling cannot reliably render/screenshot a live Flutter app (a pre-existing, previously-documented environment limitation — see Phase 8/9 SDD ledgers). Implementers verify theme values are wired correctly and nothing crashes; true "does it look beautiful" confirmation happens when Ibrahim runs the app himself. |

## 3. Design tokens (extracted from the reference site)

**Color** (light):
- Seed / primary accent: `#4586FF` (blue)
- Text on background: `#0D0D0D` (near-black, not pure black — softer)
- Background: `#FFFFFF`
- Generated via `ColorScheme.fromSeed(seedColor: Color(0xFF4586FF), brightness: Brightness.light)` — M3 derives the full tonal palette (primary/secondary/tertiary containers, surface tones, error, etc.) from this one seed, which is both correct Material 3 practice and matches the reference's "one accent color, everything else neutral" restraint.

**Color** (dark): `ColorScheme.fromSeed(seedColor: Color(0xFF4586FF), brightness: Brightness.dark)` — same seed, M3-generated dark tonal palette. No hand-picked dark colors.

**Typography**: Inter (via the `google_fonts` package — `GoogleFonts.interTextTheme()`), matching the reference's "Inter Display" family. Headings at medium weight (500), generous line-height (~1.15-1.2×), consistent with the reference's `H1`-`H3` computed styles (38-42px, weight 500, line-height ~1.15×).

**Shape**:
- Buttons (`FilledButton`, `ElevatedButton`, `OutlinedButton`): fully rounded / stadium shape (`StadiumBorder()` or a radius ≥ 999), matching the reference's `border-radius: 80px`/`1000px` pill buttons.
- Cards, dialogs, text fields, chips: softly rounded (16-20px radius) — rounder than Material's stock 4-12px defaults, but not full pills (pills are reserved for buttons/badges, matching how the reference uses them only for CTAs).
- Elevation: flat-to-low (M3's tonal-surface elevation over heavy drop shadows) — matches the reference's shadow-light, whitespace-driven depth.

**Spacing**: generous by Material standards — 16-24px base padding inside cards/sections (vs. this codebase's current 8-16px in places), consistent with the reference's airy sections.

## 4. Architecture

**New shared theme file per app** (not shared as a package — the two apps don't currently share code, and introducing a shared package is out of scope/YAGNI for a visual change): `lib/core/app_theme.dart` in both `clinicnow_desktop` and `clinicnow_mobile`, each exposing:

```dart
class AppTheme {
  static ThemeData light() => ThemeData(
    colorScheme: ColorScheme.fromSeed(seedColor: _seed, brightness: Brightness.light),
    textTheme: GoogleFonts.interTextTheme(),
    // shape/component themes: filledButtonTheme, cardTheme, inputDecorationTheme,
    // dialogTheme, chipTheme, navigationRailTheme/bottomNavigationBarTheme, etc.
  );
  static ThemeData dark() => ThemeData(
    colorScheme: ColorScheme.fromSeed(seedColor: _seed, brightness: Brightness.dark),
    textTheme: GoogleFonts.interTextTheme(ThemeData(brightness: Brightness.dark).textTheme),
    // same component theme overrides as light()
  );
}
```

Both files are near-identical (same tokens, same component theme shapes) but kept as two files rather than a shared package, matching this codebase's existing convention of light duplication between the two apps over introducing shared infrastructure (e.g., `RabbitMqPublisherConnectionProvider` is deliberately duplicated between API and Worker for the same reason — see GOALS.md Phase 5).

Each app's `main.dart` gets `theme: AppTheme.light()`, `darkTheme: AppTheme.dark()`, `themeMode: ThemeMode.system` on its `MaterialApp`.

**New dependency**: `google_fonts` added to both `pubspec.yaml`s.

## 5. Screen-level audit (what actually needs touching beyond the theme)

Setting the theme re-skins any widget that reads ambient theme values (`Theme.of(context)`, default `FilledButton`/`Card`/`TextField` styling, etc.) automatically. What needs individual attention is anywhere a screen bypasses the theme with a hardcoded value:

- **Hardcoded colors**: e.g. `appointment_screen.dart`'s `_statusColor`/`_paymentStatusColor` switch statements return raw `Colors.orange`/`Colors.blue`/`Colors.green`/`Colors.red` — these stay as status-semantic colors (a cancelled appointment should still read as "red-ish") but should be re-picked from the new theme's actual palette (e.g. `Theme.of(context).colorScheme.error` family) rather than stock Material colors that will clash with the new blue-seeded scheme.
- **Hardcoded `TextStyle`s** that don't inherit from the theme's text styles (spot these during the audit; replace with `Theme.of(context).textTheme.*`).
- **`clinic_colors.dart`** (`clinicnow_desktop`) — per-clinic color coding for multi-location UI (seen in `doctor_screen.dart`/`appointment_screen.dart`); needs re-picking so its colors sit well against the new palette in both light and dark mode.
- **`fl_chart` colors** in `dashboard_screen.dart` (Phase 9) — currently `Theme.of(context).colorScheme.primary`/`Colors.teal`/`Colors.blueGrey` hardcoded for the pie chart; the bar chart already reads from the theme correctly and needs no change, the pie chart's two hardcoded colors get re-picked to sit well in the new palette.
- **Login screens** (both apps) and the **mobile home/news screens** are the highest-visibility, most editorial-feeling screens (closest in spirit to the reference's marketing-page feel) — worth a closer per-screen pass for spacing/hierarchy, not just a token swap, so the "modern and beautiful" feeling actually lands somewhere a user sees first.

## 6. Out of scope

- Any change to navigation structure (nav rail stays a nav rail, bottom nav stays bottom nav), screen flow, or information architecture.
- Any change to `DataTable` column sets, dialog field sets, or form validation logic.
- A manual dark/light toggle UI (system-driven only, per §2).
- Custom icon set / illustration work (Material Icons stay; only their *color*, via theme, changes).
- Any backend change — this is 100% a Flutter UI change across both apps.
- Introducing a shared Dart package between the two apps.

## 7. Verification

No backend involved. Both apps: `flutter analyze` and `flutter test` clean (established bar for UI-only changes in this codebase). Given this session's confirmed inability to visually render/screenshot a running Flutter app, the implementation plan's tasks verify theme wiring is mechanically correct (right seed color, right font, no hardcoded-color regressions introduced) rather than claiming a visual "looks good" that can't actually be observed here — final visual confirmation is Ibrahim's own review of the running app.
