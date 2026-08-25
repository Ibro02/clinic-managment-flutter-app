import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/notification_item.dart';
import '../../providers/notification_provider.dart';

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
            TextButton(
              onPressed: _markAllAsRead,
              child: const Text('Označi sve', style: TextStyle(color: Colors.white)),
            ),
        ],
      ),
      body: _error != null
          ? Center(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)))
          : _items == null
              ? const Center(child: CircularProgressIndicator())
              : _items!.isEmpty
                  ? const Center(child: Text('Nemate obavijesti.'))
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.separated(
                        itemCount: _items!.length,
                        separatorBuilder: (context, index) => const Divider(height: 1),
                        itemBuilder: (context, index) {
                          final item = _items![index];
                          return ListTile(
                            leading: Icon(
                              item.isRead ? Icons.mail_outline : Icons.mark_email_unread,
                              color: item.isRead ? null : Theme.of(context).colorScheme.primary,
                            ),
                            title: Text(item.title, style: TextStyle(fontWeight: item.isRead ? FontWeight.normal : FontWeight.bold)),
                            subtitle: Text('${item.text}\n${_dateFormat.format(item.createdAtUtc.toLocal())}'),
                            isThreeLine: true,
                            onTap: () => _markAsRead(item),
                          );
                        },
                      ),
                    ),
    );
  }
}
