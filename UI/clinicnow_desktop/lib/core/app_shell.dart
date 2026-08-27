import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/auth_api.dart';
import '../core/auth_session.dart';
import '../core/design_tokens.dart';
import '../core/roles.dart';
import '../screens/appointments/appointment_screen.dart';
import '../screens/codebooks/codebooks_screen.dart';
import '../screens/dashboard/dashboard_screen.dart';
import '../screens/news/news_screen.dart';
import '../screens/people/doctor_screen.dart';
import '../screens/people/patient_screen.dart';
import '../screens/reports/reports_screen.dart';
import '../widgets/notifications_bell.dart';

/// Post-login shell for the desktop (staff) app: a persistent dark navigation
/// sidebar, a page header, and the content area.
///
/// The sidebar is deliberately dark against a light workspace. It is the one
/// surface a user looks at for their entire shift, so it carries the product's
/// identity; a stock light `NavigationRail` reads as an unstyled admin panel.
/// Everything else in the app stays quiet so this one contrast can do the work.
///
/// Shown/hidden by `main.dart` reacting to `AuthSession.isLoggedIn` - this
/// widget itself never navigates to `LoginScreen` directly; clearing the
/// session (here, or automatically on an HTTP 401 anywhere in the app) is
/// enough to redirect back.
class AppShell extends StatefulWidget {
  const AppShell({super.key});

  @override
  State<AppShell> createState() => _AppShellState();
}

/// One destination: what the sidebar shows, what the header says, and what the
/// content area builds. Header copy lives here so every screen gets a title and
/// a one-line description without each screen having to render its own.
class _NavEntry {
  final IconData icon;
  final IconData selectedIcon;
  final String label;
  final String title;
  final String subtitle;
  final String section;
  final WidgetBuilder builder;

  const _NavEntry({
    required this.icon,
    required this.selectedIcon,
    required this.label,
    required this.title,
    required this.subtitle,
    required this.section,
    required this.builder,
  });
}

class _AppShellState extends State<AppShell> {
  int _selectedIndex = 0;
  bool _collapsed = false;
  final _authApi = AuthApi();
  bool _isLoggingOut = false;

  Future<void> _logout(BuildContext context) async {
    final session = context.read<AuthSession>();
    final token = session.token;

    setState(() => _isLoggingOut = true);
    if (token != null) {
      await _authApi.logout(token); // best-effort; always clears locally after
    }
    session.clear();
  }

  List<_NavEntry> _entries(AuthSession session) {
    // Codebook/patient/doctor management is an Administrator/Staff concern;
    // Doctor accounts can log in to the desktop app and browse patients/
    // doctors (read-only there) but don't manage codebooks. The backend
    // independently enforces the same boundaries on every write endpoint
    // regardless of what the sidebar shows.
    final canManageCodebooks = session.hasRole(Roles.administrator) || session.hasRole(Roles.staff);
    // Same condition as canManageCodebooks today, named separately because the
    // two visibility rules (dashboard/reports vs. codebook CRUD) are
    // independent business decisions that happen to currently coincide.
    final canViewReports = session.hasRole(Roles.administrator) || session.hasRole(Roles.staff);

    return <_NavEntry>[
      if (canViewReports)
        _NavEntry(
          icon: Icons.dashboard_outlined,
          selectedIcon: Icons.dashboard_rounded,
          label: 'Početna',
          title: 'Početna',
          subtitle: 'Pregled dana i ključnih brojki',
          section: 'Rad',
          builder: (_) => const DashboardScreen(),
        ),
      _NavEntry(
        icon: Icons.people_outline,
        selectedIcon: Icons.people_rounded,
        label: 'Pacijenti',
        title: 'Pacijenti',
        subtitle: 'Kartoni, dokumentacija i historija posjeta',
        section: 'Rad',
        builder: (_) => const PatientScreen(),
      ),
      _NavEntry(
        icon: Icons.medical_services_outlined,
        selectedIcon: Icons.medical_services_rounded,
        label: 'Doktori',
        title: 'Doktori',
        subtitle: 'Osoblje, specijalizacije i radno vrijeme',
        section: 'Rad',
        builder: (_) => const DoctorScreen(),
      ),
      _NavEntry(
        icon: Icons.event_outlined,
        selectedIcon: Icons.event_rounded,
        label: 'Termini',
        title: 'Termini',
        subtitle: 'Zakazivanje i pregled rasporeda',
        section: 'Rad',
        builder: (_) => const AppointmentScreen(),
      ),
      _NavEntry(
        icon: Icons.campaign_outlined,
        selectedIcon: Icons.campaign_rounded,
        label: 'Obavijesti',
        title: 'Obavijesti',
        subtitle: 'Vijesti i objave za pacijente',
        section: 'Rad',
        builder: (_) => const NewsScreen(),
      ),
      if (canViewReports)
        _NavEntry(
          icon: Icons.bar_chart_outlined,
          selectedIcon: Icons.bar_chart_rounded,
          label: 'Izvještaji',
          title: 'Izvještaji',
          subtitle: 'Termini i prihodi kroz vrijeme',
          section: 'Administracija',
          builder: (_) => const ReportsScreen(),
        ),
      if (canManageCodebooks)
        _NavEntry(
          icon: Icons.list_alt_outlined,
          selectedIcon: Icons.list_alt_rounded,
          label: 'Šifrarnici',
          title: 'Šifrarnici',
          subtitle: 'Gradovi, lokacije, usluge i specijalizacije',
          section: 'Administracija',
          builder: (_) => const CodebooksScreen(),
        ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<AuthSession>();
    final c = context.colors;
    final entries = _entries(session);

    // If a role change (re-login) shrinks the destination list, don't leave
    // _selectedIndex pointing past the end.
    final selectedIndex = _selectedIndex < entries.length ? _selectedIndex : 0;
    final current = entries[selectedIndex];

    return Scaffold(
      backgroundColor: c.canvas,
      body: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Sidebar(
            entries: entries,
            selectedIndex: selectedIndex,
            collapsed: _collapsed,
            session: session,
            isLoggingOut: _isLoggingOut,
            onSelect: (index) => setState(() => _selectedIndex = index),
            onLogout: () => _logout(context),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _TopBar(
                  title: current.title,
                  subtitle: current.subtitle,
                  collapsed: _collapsed,
                  onToggleSidebar: () => setState(() => _collapsed = !_collapsed),
                  session: session,
                ),
                Expanded(
                  child: ClipRect(
                    // Keyed so switching destinations rebuilds the subtree
                    // cleanly instead of reusing scroll positions across
                    // unrelated screens.
                    child: KeyedSubtree(
                      key: ValueKey(current.label),
                      child: current.builder(context),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// ---------------------------------------------------------------------------
/// Sidebar
/// ---------------------------------------------------------------------------
class _Sidebar extends StatelessWidget {
  final List<_NavEntry> entries;
  final int selectedIndex;
  final bool collapsed;
  final AuthSession session;
  final bool isLoggingOut;
  final ValueChanged<int> onSelect;
  final VoidCallback onLogout;

  const _Sidebar({
    required this.entries,
    required this.selectedIndex,
    required this.collapsed,
    required this.session,
    required this.isLoggingOut,
    required this.onSelect,
    required this.onLogout,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    // Map preserves insertion order, so sections appear in the order the
    // entries declare them.
    final sections = <String, List<int>>{};
    for (var i = 0; i < entries.length; i++) {
      sections.putIfAbsent(entries[i].section, () => []).add(i);
    }

    return AnimatedContainer(
      duration: AppDuration.normal,
      curve: AppDuration.curve,
      width: collapsed ? AppSizes.sidebarCollapsed : AppSizes.sidebarExpanded,
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [c.navTop, c.navBottom],
        ),
        border: Border(right: BorderSide(color: c.navBorder)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _brand(context),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: AppSpacing.xs),
              children: [
                for (final section in sections.entries) ...[
                  _sectionLabel(context, section.key),
                  for (final index in section.value)
                    _NavItem(
                      entry: entries[index],
                      selected: index == selectedIndex,
                      collapsed: collapsed,
                      onTap: () => onSelect(index),
                    ),
                  const SizedBox(height: AppSpacing.md),
                ],
              ],
            ),
          ),
          _userBlock(context),
        ],
      ),
    );
  }

  Widget _brand(BuildContext context) {
    final c = context.colors;

    final mark = Container(
      height: 38,
      width: 38,
      decoration: BoxDecoration(
        borderRadius: AppRadius.all(AppRadius.md),
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [c.primary, c.primaryStrong],
        ),
      ),
      child: const Icon(Icons.local_hospital_rounded, color: Colors.white, size: 21),
    );

    return Container(
      height: AppSizes.topBarHeight,
      padding: EdgeInsets.symmetric(horizontal: collapsed ? AppSpacing.lg - 3 : AppSpacing.md + 2),
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: c.navBorder)),
      ),
      child: Row(
        mainAxisAlignment: collapsed ? MainAxisAlignment.center : MainAxisAlignment.start,
        children: [
          mark,
          if (!collapsed) ...[
            const SizedBox(width: AppSpacing.sm),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'ClinicNow',
                    style: context.text.titleMedium?.copyWith(
                      color: Colors.white,
                      letterSpacing: -0.3,
                      fontSize: 16,
                    ),
                  ),
                  Text(
                    'Klinički sistem',
                    style: context.text.bodySmall?.copyWith(color: c.navTextMuted, fontSize: 11.5, height: 1.2),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _sectionLabel(BuildContext context, String label) {
    final c = context.colors;

    if (collapsed) {
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md, vertical: AppSpacing.xs),
        child: Divider(color: c.navBorder, height: 1, thickness: 1),
      );
    }

    return Padding(
      padding: const EdgeInsets.fromLTRB(AppSpacing.sm, AppSpacing.md, AppSpacing.sm, AppSpacing.xs),
      child: Text(
        label.toUpperCase(),
        style: context.text.labelSmall?.copyWith(color: c.navTextMuted, fontSize: 10.5),
      ),
    );
  }

  Widget _userBlock(BuildContext context) {
    final c = context.colors;
    final roleLabel = session.roles.isNotEmpty ? session.roles.first : '';

    final avatar = Container(
      height: 36,
      width: 36,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: c.navItemSelected,
        borderRadius: AppRadius.all(AppRadius.sm),
        border: Border.all(color: c.navBorder),
      ),
      child: Text(
        _initials(session.fullName),
        style: context.text.labelMedium?.copyWith(color: Colors.white, fontSize: 12.5),
      ),
    );

    final logoutButton = Tooltip(
      message: 'Odjava',
      child: IconButton(
        iconSize: 18,
        style: IconButton.styleFrom(
          foregroundColor: c.navTextMuted,
          hoverColor: c.navItemHover,
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.sm)),
        ),
        icon: isLoggingOut
            ? SizedBox(
                height: 16,
                width: 16,
                child: CircularProgressIndicator(strokeWidth: 2, color: c.navTextMuted),
              )
            : const Icon(Icons.logout_rounded),
        onPressed: isLoggingOut ? null : onLogout,
      ),
    );

    return Container(
      padding: const EdgeInsets.all(AppSpacing.sm),
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: c.navBorder)),
      ),
      child: collapsed
          ? Column(
              children: [
                avatar,
                const SizedBox(height: AppSpacing.xs),
                logoutButton,
              ],
            )
          : Row(
              children: [
                avatar,
                const SizedBox(width: AppSpacing.sm),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        session.fullName,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: context.text.titleSmall?.copyWith(color: Colors.white),
                      ),
                      Text(
                        roleLabel,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: context.text.bodySmall?.copyWith(
                          color: c.navTextMuted,
                          fontSize: 11.5,
                          height: 1.25,
                        ),
                      ),
                    ],
                  ),
                ),
                logoutButton,
              ],
            ),
    );
  }

  static String _initials(String fullName) {
    final parts = fullName.trim().split(RegExp(r'\s+')).where((p) => p.isNotEmpty).toList();
    if (parts.isEmpty) return '?';
    if (parts.length == 1) return parts.first.characters.first.toUpperCase();
    return (parts.first.characters.first + parts.last.characters.first).toUpperCase();
  }
}

/// A single sidebar destination. Stateful only to track hover, which matters
/// far more on desktop than on mobile - a rail with no hover feedback is the
/// clearest sign that a desktop app was built as a phone app.
class _NavItem extends StatefulWidget {
  final _NavEntry entry;
  final bool selected;
  final bool collapsed;
  final VoidCallback onTap;

  const _NavItem({
    required this.entry,
    required this.selected,
    required this.collapsed,
    required this.onTap,
  });

  @override
  State<_NavItem> createState() => _NavItemState();
}

class _NavItemState extends State<_NavItem> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final selected = widget.selected;

    final background = selected
        ? c.navItemSelected
        : _hovered
            ? c.navItemHover
            : Colors.transparent;

    final foreground = selected
        ? c.navItemSelectedText
        : _hovered
            ? c.navItemSelectedText
            : c.navText;

    final content = Row(
      mainAxisAlignment: widget.collapsed ? MainAxisAlignment.center : MainAxisAlignment.start,
      children: [
        Icon(selected ? widget.entry.selectedIcon : widget.entry.icon, size: 19, color: foreground),
        if (!widget.collapsed) ...[
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Text(
              widget.entry.label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: context.text.bodyMedium?.copyWith(
                color: foreground,
                fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                fontSize: 13.5,
              ),
            ),
          ),
        ],
      ],
    );

    final item = MouseRegion(
      cursor: SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: GestureDetector(
        onTap: widget.onTap,
        behavior: HitTestBehavior.opaque,
        child: AnimatedContainer(
          duration: AppDuration.fast,
          curve: AppDuration.curve,
          height: 40,
          margin: const EdgeInsets.symmetric(vertical: 1),
          padding: EdgeInsets.symmetric(horizontal: widget.collapsed ? 0 : AppSpacing.sm),
          decoration: BoxDecoration(
            color: background,
            borderRadius: AppRadius.all(AppRadius.sm),
          ),
          child: Stack(
            children: [
              // Left indicator bar. Structural, not decorative: it marks the
              // active destination even when the sidebar is collapsed and the
              // label is gone.
              Positioned(
                left: widget.collapsed ? 2 : -AppSpacing.xs,
                top: 0,
                bottom: 0,
                child: Center(
                  child: AnimatedContainer(
                    duration: AppDuration.fast,
                    width: 3,
                    height: selected ? 18 : 0,
                    decoration: BoxDecoration(
                      color: c.primary,
                      borderRadius: AppRadius.all(AppRadius.pill),
                    ),
                  ),
                ),
              ),
              Center(child: content),
            ],
          ),
        ),
      ),
    );

    if (!widget.collapsed) return item;
    return Tooltip(message: widget.entry.label, child: item);
  }
}

/// ---------------------------------------------------------------------------
/// Top bar
/// ---------------------------------------------------------------------------
class _TopBar extends StatelessWidget {
  final String title;
  final String subtitle;
  final bool collapsed;
  final VoidCallback onToggleSidebar;
  final AuthSession session;

  const _TopBar({
    required this.title,
    required this.subtitle,
    required this.collapsed,
    required this.onToggleSidebar,
    required this.session,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Container(
      height: AppSizes.topBarHeight,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
      decoration: BoxDecoration(
        color: c.surface,
        border: Border(bottom: BorderSide(color: c.border)),
      ),
      child: Row(
        children: [
          IconButton(
            iconSize: 19,
            tooltip: collapsed ? 'Proširi meni' : 'Suzi meni',
            icon: Icon(collapsed ? Icons.menu_open_rounded : Icons.menu_rounded),
            onPressed: onToggleSidebar,
          ),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: context.text.titleLarge, maxLines: 1, overflow: TextOverflow.ellipsis),
                Text(
                  subtitle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: context.text.bodySmall?.copyWith(color: c.textMuted, height: 1.25),
                ),
              ],
            ),
          ),
          _dateChip(context),
          const SizedBox(width: AppSpacing.sm),
          NotificationsBell(authSession: session),
        ],
      ),
    );
  }

  /// Today's date, in tabular figures. Staff write dates onto paper referrals
  /// all day; having it in the chrome saves a glance at the taskbar.
  Widget _dateChip(BuildContext context) {
    final c = context.colors;

    return Container(
      height: AppSizes.controlHeightSm,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
      decoration: BoxDecoration(
        color: c.surfaceMuted,
        borderRadius: AppRadius.all(AppRadius.sm),
        border: Border.all(color: c.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.calendar_today_rounded, size: 14, color: c.textMuted),
          const SizedBox(width: AppSpacing.xs),
          Text(
            DateFormat('dd.MM.yyyy.').format(DateTime.now()),
            style: context.text.labelMedium?.copyWith(
              color: c.textSecondary,
              fontFeatures: AppTypography.tabular,
            ),
          ),
        ],
      ),
    );
  }
}
