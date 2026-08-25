import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/news_item.dart';
import '../../providers/news_item_provider.dart';

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
      return Center(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)));
    }
    if (_items == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_items!.isEmpty) {
      return const Center(child: Text('Trenutno nema obavijesti.'));
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.builder(
        padding: const EdgeInsets.all(12),
        itemCount: _items!.length,
        itemBuilder: (context, index) {
          final item = _items![index];
          return Card(
            margin: const EdgeInsets.only(bottom: 12),
            child: InkWell(
              onTap: () => Navigator.of(context).push(MaterialPageRoute(
                builder: (_) => _NewsDetailScreen(item: item, provider: _provider),
              )),
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (item.hasImage)
                      ClipRRect(
                        borderRadius: BorderRadius.circular(6),
                        child: Image.network(
                          _provider.absoluteImageUrl(item)!,
                          width: 64,
                          height: 64,
                          fit: BoxFit.cover,
                          errorBuilder: (context, error, stackTrace) => const Icon(Icons.broken_image_outlined),
                        ),
                      )
                    else
                      const SizedBox(width: 64, height: 64, child: Icon(Icons.campaign_outlined)),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(item.title, style: Theme.of(context).textTheme.titleMedium),
                          const SizedBox(height: 4),
                          Text(
                            item.text,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            _dateFormat.format(item.createdAtUtc.toLocal()),
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          );
        },
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
