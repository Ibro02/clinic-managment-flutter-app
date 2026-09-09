import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/news_item.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/news_item_provider.dart';
import '../../widgets/home_summary_card.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_states.dart';

/// The patient-facing "home" screen: a greeting carrying the patient's own
/// appointment counters ([HomeSummaryCard]), then the news/announcements feed.
/// The feed is master-detail - tapping a card opens the full text + image
/// (rulebook Part II §K).
class NewsListScreen extends StatefulWidget {
  const NewsListScreen({super.key});

  @override
  State<NewsListScreen> createState() => _NewsListScreenState();
}

class _NewsListScreenState extends State<NewsListScreen> {
  /// Mirrors .NET's `AppointmentStatus` - same values the "Termini" filter bar
  /// uses, so the two screens can never disagree about what "Na čekanju" means.
  static const _statusPending = 0;
  static const _statusConfirmed = 1;

  late final NewsItemProvider _provider;
  late final AppointmentProvider _appointmentProvider;
  final _dateFormat = DateFormat('dd.MM.yyyy');

  List<NewsItem>? _items;
  String? _error;

  /// Null until the counters load. A failed count request leaves them null
  /// rather than zero - "0 potvrđenih" would read as "my appointment is gone".
  int? _pendingCount;
  int? _confirmedCount;

  @override
  void initState() {
    super.initState();
    final session = context.read<AuthSession>();
    _provider = NewsItemProvider(session);
    _appointmentProvider = AppointmentProvider(session);
    _load();
  }

  /// Reloads both halves of the screen. They are independent on purpose: a
  /// counter that fails to load must not blank out the news feed, or vice versa.
  Future<void> _load() => Future.wait([_loadNews(), _loadCounts()]);

  Future<void> _loadNews() async {
    setState(() => _error = null);
    try {
      final items = await _provider.getPaged({'pageSize': 20});
      if (mounted) setState(() => _items = items.resultList);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  /// Counts come from the list endpoint's `count` with `pageSize: 1`, so the
  /// server never serialises rows this screen will not draw. The list is
  /// already scoped to the caller by the JWT (rulebook §5), so these are the
  /// patient's own appointments.
  Future<void> _loadCounts() async {
    try {
      final results = await Future.wait([
        _appointmentProvider.getPaged({'pageSize': 1, 'status': _statusPending}),
        _appointmentProvider.getPaged({'pageSize': 1, 'status': _statusConfirmed}),
      ]);
      if (!mounted) return;
      setState(() {
        _pendingCount = results[0].count;
        _confirmedCount = results[1].count;
      });
    } catch (_) {
      // Non-fatal: the card keeps its placeholder and the news feed below still
      // renders. Pull-to-refresh retries both halves.
      if (!mounted) return;
      setState(() {
        _pendingCount = null;
        _confirmedCount = null;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    // `watch`, not `read`: renaming yourself on the profile screen has to
    // change the greeting when you come back, without a re-login.
    final session = context.watch<AuthSession>();

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        // Keeps pull-to-refresh working when the feed is short enough not to
        // scroll on its own.
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          HomeSummaryCard(
            name: session.fullName,
            pendingCount: _pendingCount,
            confirmedCount: _confirmedCount,
          ),
          const SizedBox(height: AppSpacing.md),
          const AppSectionHeader(label: 'Obavijesti'),
          ..._newsSection(context),
        ],
      ),
    );
  }

  /// The feed's own body - loading, error, empty, or the cards. Returned as
  /// children of the page's list rather than replacing the whole screen, so the
  /// greeting stays put while only the section below it changes state.
  List<Widget> _newsSection(BuildContext context) {
    if (_error != null) {
      return [AppErrorState(message: _error!, onRetry: _loadNews)];
    }
    if (_items == null) {
      return const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: AppSpacing.xl),
          child: Center(child: CircularProgressIndicator()),
        ),
      ];
    }
    if (_items!.isEmpty) {
      return const [
        AppEmptyState(
          icon: Icons.campaign_outlined,
          title: 'Nema obavijesti',
          message: 'Novosti iz klinike pojavit će se ovdje.',
        ),
      ];
    }

    final cards = <Widget>[];
    for (var i = 0; i < _items!.length; i++) {
      if (i > 0) cards.add(const SizedBox(height: AppSpacing.xs));
      cards.add(_newsCard(context, _items![i]));
    }
    return cards;
  }

  Widget _newsCard(BuildContext context, NewsItem item) {
    final c = context.colors;

    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.sm + 2),
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute(builder: (_) => _NewsDetailScreen(item: item, provider: _provider)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Rulebook §K: the entity's image sits beside its name in a list.
          _thumbnail(context, item),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(item.title, style: context.text.titleSmall),
                const SizedBox(height: 3),
                Text(
                  item.text,
                  style: context.text.bodySmall?.copyWith(color: c.textSecondary),
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                ),
                const SizedBox(height: 4),
                Text(
                  _dateFormat.format(item.createdAtUtc.toLocal()),
                  style: context.text.bodySmall?.copyWith(color: c.textMuted, fontSize: 12),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// Row thumbnail. An item with no image still occupies the column, so rows
  /// never jog left and right as the list scrolls past items that happen to
  /// have a picture.
  Widget _thumbnail(BuildContext context, NewsItem item) {
    final c = context.colors;

    Widget placeholder(IconData icon) => Container(
      width: 56,
      height: 56,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: c.surfaceMuted,
        borderRadius: AppRadius.all(AppRadius.md),
        border: Border.all(color: c.border),
      ),
      child: Icon(icon, size: 22, color: c.textMuted),
    );

    if (!item.hasImage) return placeholder(Icons.campaign_outlined);

    return ClipRRect(
      borderRadius: AppRadius.all(AppRadius.md),
      child: Image.network(
        _provider.absoluteImageUrl(item)!,
        width: 56,
        height: 56,
        fit: BoxFit.cover,
        errorBuilder: (context, error, stackTrace) => placeholder(Icons.broken_image_outlined),
      ),
    );
  }
}

class _NewsDetailScreen extends StatelessWidget {
  final NewsItem item;
  final NewsItemProvider provider;

  const _NewsDetailScreen({required this.item, required this.provider});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Obavijest'),
        // Explicit Back/X per rulebook Part II §K - AppBar's default back
        // arrow already covers "Back"; this is the "X close" equivalent for
        // a full-screen detail push.
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: () => Navigator.of(context).pop(),
        ),
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (item.hasImage)
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              // Image kept well under half the screen height, per rulebook §6.
              child: Image.network(
                provider.absoluteImageUrl(item)!,
                fit: BoxFit.cover,
                errorBuilder: (context, error, stackTrace) => const Icon(Icons.broken_image_outlined, size: 48),
              ),
            ),
          const SizedBox(height: 16),
          Text(item.title, style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(item.text),
        ],
      ),
    );
  }
}
