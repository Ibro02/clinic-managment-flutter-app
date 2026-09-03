import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/notification_item.dart';
import '../../providers/notification_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';

/// The patient's own notifications - read/unread, mark-read, mark-all-read.
/// Auto-refreshes via polling on entry/pull-to-refresh (rulebook Part II §G
/// allows SignalR or polling; see `NotificationsBell` on desktop for the
/// same rationale).
class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  late final NotificationProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  List<NotificationItem>? _items;
  String? _error;

  @override
  void initState() {
    super.initState();
    _provider = NotificationProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final items = await _provider.getPaged();
      if (mounted) setState(() => _items = items);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _markAsRead(NotificationItem item) async {
    if (item.isRead) return;
    await _provider.markAsRead(item.id);
    _load();
  }

  Future<void> _markAllAsRead() async {
    await _provider.markAllAsRead();
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Obavijesti'),
        actions: [
          if (_items != null && _items!.any((n) => !n.isRead))
            // No hardcoded colour: the app bar sits on `surface` now, so white
            // text would be invisible. The theme's own action colour is correct
            // in both light and dark.
            TextButton(
              onPressed: _markAllAsRead,
              child: const Text('Označi sve'),
            ),
          const SizedBox(width: AppSpacing.xs),
        ],
      ),
      body: _error != null
          ? Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: AppErrorState(message: _error!, onRetry: _load),
            )
          : _items == null
          ? const Center(child: CircularProgressIndicator())
          : _items!.isEmpty
          ? const Padding(
              padding: EdgeInsets.all(AppSpacing.md),
              child: AppEmptyState(
                icon: Icons.notifications_none_rounded,
                title: 'Nemate obavijesti',
                message: 'Obavijesti o vašim terminima i uplatama pojavit će se ovdje.',
              ),
            )
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView.separated(
                padding: const EdgeInsets.all(AppSpacing.md),
                itemCount: _items!.length,
                separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
                itemBuilder: (context, index) {
                  final item = _items![index];

                  return AppListCard(
                    icon: item.isRead ? Icons.mail_outline : Icons.mark_email_unread_rounded,
                    tone: item.isRead ? AppTone.neutral : AppTone.primary,
                    // An unread notification is tinted rather than just bolded -
                    // weight alone is easy to miss in a long list.
                    highlighted: !item.isRead,
                    title: item.title,
                    subtitle: item.text,
                    meta: _dateFormat.format(item.createdAtUtc.toLocal()),
                    onTap: () => _markAsRead(item),
                  );
                },
              ),
            ),
    );
  }
}
