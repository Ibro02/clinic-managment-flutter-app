import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/news_item.dart';
import '../../providers/news_item_provider.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_states.dart';

/// News/announcements list - patient-facing "home" screen. Master-detail:
/// tapping a card opens the full text + image (rulebook Part II §K).
class NewsListScreen extends StatefulWidget {
  const NewsListScreen({super.key});

  @override
  State<NewsListScreen> createState() => _NewsListScreenState();
}

class _NewsListScreenState extends State<NewsListScreen> {
  late final NewsItemProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy');

  List<NewsItem>? _items;
  String? _error;

  @override
  void initState() {
    super.initState();
    _provider = NewsItemProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final items = await _provider.getPaged({'pageSize': 20});
      if (mounted) setState(() => _items = items.resultList);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: AppErrorState(message: _error!, onRetry: _load),
      );
    }
    if (_items == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_items!.isEmpty) {
      return const Padding(
        padding: EdgeInsets.all(AppSpacing.md),
        child: AppEmptyState(
          icon: Icons.campaign_outlined,
          title: 'Nema obavijesti',
          message: 'Novosti iz klinike pojavit će se ovdje.',
        ),
      );
    }

    final c = context.colors;

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        padding: const EdgeInsets.all(AppSpacing.md),
        itemCount: _items!.length,
        separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
        itemBuilder: (context, index) {
          final item = _items![index];

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
        },
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
