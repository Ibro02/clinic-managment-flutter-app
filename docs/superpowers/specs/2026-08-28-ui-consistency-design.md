# UI Consistency & Design-System Adoption — Design

> Validated via brainstorming with Ibrahim on 2026-08-28. Supersedes the visual
> direction of `2026-08-27-visual-redesign-design.md` (stadium buttons, seeded
> `ColorScheme`) wherever the two disagree. Covers both Flutter apps.

## 1. Root cause

The brief was "the app needs consistency (grids, buttons, popups)". The cause is
not a missing design system — commit `4654256` already added a complete one to
both apps:

- `lib/core/design_tokens.dart` — hand-tuned `AppColors` (light + dark),
  `AppSpacing` (4px grid), `AppRadius` (6–20px), `AppSizes`, `AppDuration`,
  typography helpers, and a `context.colors` / `context.text` extension.
- `lib/widgets/ui/` — 25 components: `AppDialog` + `showAppDialog` +
  `showConfirmDialog`, `AppDataTable` (+ `AppColumn`, `AppTablePaging`,
  `AppRowAction`), `AppCard`, `AppSectionHeader`, `AppStatusBadge`,
  `AppCountPill`, `AppAvatar`, `AppField`, `AppFieldRow`, `AppFormSection`,
  `AppSearchField`, `AppToolbar`, `AppSegmented`, `AppEmptyState`,
  `AppErrorState`, `AppSkeleton`, `AppNotice`, `StatCard`.
- `lib/widgets/charts/` — 6 chart widgets reading `AppColors.chartSeries`.

**Nothing consumes it.** The only importers are each app's
`screens/design_preview_screen.dart` (a showroom) and desktop's
`layouts/app_shell.dart`. All 28 real screens are still raw Material:

| Raw usage in `screens/` | desktop | mobile |
|---|---|---|
| `AlertDialog` | 19 | 2 |
| `Card(` | 20 | 16 |
| `FilledButton` / `OutlinedButton` / `TextButton` | 68 | 27 |
| hardcoded `Colors.*` | 15 | 16 |

The fix is **adoption**, not authorship.

## 2. Key decisions

| Decision | Choice | Why |
|---|---|---|
| Palette | **Keep `#4586FF` blue.** DocSpot's own palette (`#DEDDDB`, `#C3C1BD`, `#101010`, `#3C5E48`, `#826E68`, `#A7C7C8`) drives **shape and layout only**, not colour. | Ibrahim's explicit choice. Preserves the dark-mode contrast fixes validated in the 2026-08-27 final review; re-toning would require re-validating every neutral and every dark-mode surface pairing. |
| Desktop depth | **Components + page structure.** | Ibrahim's explicit choice. The Medical Dashboard CRM shot is a UX mockup, so list screens get its page spine — not just re-skinned tables. |
| Breadth | **All 28 screens, both apps, one pass.** | Ibrahim's explicit choice. A partial pass reproduces the exact patchwork being complained about. |
| Library sharing | **Byte-identical duplication, pruned per app.** No shared Dart package. | Matches this codebase's deliberate convention (duplicated `RabbitMqPublisherConnectionProvider`; the 2026-08-27 spec's explicit "no shared package — YAGNI"). A package would restructure the build and release steps (Part II §L) close to submission for a benefit that only pays off with a third client. |
| Sharp edges | Already the tokens' stated intent (`AppRadius.xs = 6`; `app_theme.dart` comments explicitly reject stadium buttons as "toy-like on a dense clinical desktop"). | Nothing to decide — it just never reached the screens. |

## 3. Mobile theme bug (prerequisite)

`clinicnow_mobile/lib/core/app_theme.dart` is still the superseded 2026-08-27
theme: `ColorScheme.fromSeed`, `StadiumBorder` buttons, 18px cards, 16px fields,
borderless inputs. Critically, **it never registers `AppColors` as a
`ThemeExtension`**, so every `context.colors` call on mobile resolves through the
`?? AppColors.light` fallback at `design_tokens.dart:289` — light-mode colours on
a dark surface, in every design-system widget.

Replace it with desktop's `_build(AppColors, Brightness)` recipe, kept
structurally identical to desktop's file (so the two stay diffable) with a small
number of explicitly-commented mobile deltas:

1. Touch-height controls (48px) instead of desktop's 42px.
2. `navigationBarTheme` wired to the nav tokens (desktop has no bottom nav).
3. `dataTableTheme` omitted (mobile has no grids — see §5).

This lands **before** any mobile screen migration; migrating first would bake in
invisible dark-mode breakage.

## 4. Desktop — components + CRM page skeleton

18 screens. Three passes:

**Popups.** All 19 `AlertDialog`s become `AppDialog` opened via `showAppDialog`.
Destructive paths route through the existing `showConfirmDialog`. Dialog bodies
use `AppFormSection` / `AppField` / `AppFieldRow` instead of hand-rolled
`Row(children: [Expanded(FormBuilderTextField…)])`.

**Grids.** The raw `DataTable`s and codebook tables become `AppDataTable`, which
supplies loading skeleton, error + retry, empty state, sort affordance and capped
pagination by construction rather than by discipline.

**Page skeleton (the CRM spine).** Each list screen gets:
- a page header — title, one line of context, primary action right-aligned;
- an `AppToolbar` search/filter bar directly above the grid;
- a `StatCard` row where it carries meaning (appointments, dashboard, reports).

The dark sidebar the CRM look depends on is already defined in the nav tokens
(`navTop`/`navBottom`/`navItemSelected`) and already live in
`layouts/app_shell.dart`; only the page bodies need to stop being bare.

**Explicitly untouched:** navigation structure, table column sets, dialog field
sets, validation logic, and every business rule.

## 5. Mobile — DocSpot patterns

10 screens, migrated onto the same tokens at mobile density.

- **Prune** the desktop-only widgets from mobile's copy of `widgets/`. This turned
  out to be wider than planned: mobile's `pubspec.yaml` has **no `fl_chart`
  dependency**, but the design-system commit copied all six `widgets/charts/`
  files in anyway, so `flutter analyze` on mobile had been failing with ~20
  errors since. Removed from mobile: `widgets/charts/` (6 files),
  `widgets/ui/app_data_table.dart` (a grid mobile never renders), and
  `widgets/ui/stat_card.dart` (depends on the chart widgets, and the patient app
  has no dashboard). Adding `fl_chart` to mobile was rejected — it would add a
  dependency purely to satisfy unreachable code.
- **Add** `widgets/ui/app_tiles.dart`: `AppListCard` (the card-row that replaces
  `ListTile` across appointments/documents/notifications/recommendations),
  `AppGreetingHeader`, and `AppSlotPicker` (44px touch targets — a mistyped tap
  in a booking flow books the wrong hour). `AppSectionHeader` already existed in
  `app_card.dart` and covers the "section header with see-all" need.
- **Add** an optional `onTap` to `AppCard`, mirrored into the desktop copy so the
  two files stay byte-identical.
- **Replace** the hardcoded `Colors.orange/green/red/grey` status switches with
  `AppStatusBadge` + `AppTone`, so status reads identically in both apps.

## 6. Cleanup

- Delete `clinicnow_desktop/lib/core/app_shell.dart` — a dead 657-line duplicate;
  `main.dart` imports `layouts/app_shell.dart` (628 lines).
- **Revised during implementation:** `design_preview_screen.dart` (691 lines, in
  each app) turned out to be **unreachable** — nothing imports it in either app.
  Mobile's copy was the sole reason its chart widgets were compiled at all, so it
  was deleted there. Desktop's copy is left in place for now: it analyzes clean
  and is the component library's only showroom, but it is dead code under
  Part II §D and is flagged for Ibrahim's decision rather than removed
  unilaterally.

## 7. Grading side effect (CLAUDE.md Part II §K)

Adoption mechanically satisfies several §K items that would otherwise need a hand
audit: `AppDialog` renders the required top-right X on every popup;
`showConfirmDialog` covers "confirmation dialog for irreversible actions";
`AppDataTable` enforces capped pagination and renders cells, never raw IDs;
`AppEmptyState` covers "explain why rather than open an empty form".

## 8. Out of scope

- Any backend change. This is 100% Flutter.
- Navigation structure / information architecture in either app.
- Table column sets, dialog field sets, validation rules.
- A shared Dart package between the apps.
- Re-toning the palette (§2).

## 9. Verification

Achieved at completion:

- `flutter analyze`: desktop 3 issues, mobile 2 — all pre-existing `info`-level
  lints in files this work did not author, unchanged from the starting baseline.
  Mobile went from **70 issues to 2** (the `fl_chart` breakage in §5).
- `flutter test`: desktop 1/1, mobile 3/3, all passing.
- Grep gate: **zero** `AlertDialog` and **zero** `showDialog(` under either app's
  `lib/screens/`. Four `Colors.white` remain, each a deliberate foreground on a
  danger-filled button (the same pairing `showConfirmDialog` itself uses), plus
  one occurrence inside a comment.

**No repo-wide `dart format`.** Running one at a non-default width collapsed
deliberately-expanded code in 44 files this work never touched; that was reverted
via `git checkout` on exactly those files, leaving the working tree as only the
intended changes. This project has no formatter width configured, and its
existing style is wider than `dart format`'s 80-column default — so a formatter
pass is a separate decision, not a side effect of a UI change.

Pixel confirmation is Ibrahim's on the running app — this environment cannot
composite a Flutter frame (a documented limitation consistent with every prior
phase).
