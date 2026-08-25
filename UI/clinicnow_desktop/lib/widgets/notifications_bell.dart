import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../core/api_exception.dart';
import '../core/auth_session.dart';
import '../models/notification_item.dart';
import '../providers/notification_provider.dart';

/// A bell icon with an unread-count badge that opens the notification list.
/// Auto-refreshes via short-interval polling (rulebook Part II §G allows
/// SignalR *or* polling for auto-refresh - the backend also pushes over
/// SignalR on `/hubs/notifications`, but a client-side Dart SignalR
/// connection is deliberately not wired here; polling every 20s already
/// satisfies "no manual refresh required" without that extra client
/// complexity).
class NotificationsBell extends StatefulWidget {
  final AuthSession authSession;

  const NotificationsBell({super.key, required this.authSession});

  @override
  State<NotificationsBell> createState() => _NotificationsBellState();
}

class _NotificationsBellState extends State<NotificationsBell> {
  late final NotificationProvider _provider;
  Timer? _pollTimer;
  int _unreadCount = 0;

  @override
  void initState() {
    super.initState();
    _provider = NotificationProvider(widget.authSession);
    _refreshUnreadCount();
    _pollTimer = Timer.periodic(const Duration(seconds: 20), (_) => _refreshUnreadCount());
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    super.dispose();
  }

  Future<void> _refreshUnreadCount() async {
    try {
      final count = await _provider.getUnreadCount();
      if (mounted) setState(() => _unreadCount = count);
    } catch (error) {
      // A failed background poll shouldn't surface an error to the user -
      // just try again on the next tick.
    }
  }

  Future<void> _openList() async {
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => _NotificationsDialog(provider: _provider),
    );
    _refreshUnreadCount();
  }

  @override
  Widget build(BuildContext context) {
    return IconButton(
      tooltip: 'Obavijesti',
      onPressed: _openList,
      icon: Badge(
        label: Text('$_unreadCount'),
        isLabelVisible: _unreadCount > 0,
        child: const Icon(Icons.notifications_outlined),
      ),
    );
  }
}

class _NotificationsDialog extends StatefulWidget {
  final NotificationProvider provider;

  const _NotificationsDialog({required this.provider});

  @override
  State<_NotificationsDialog> createState() => _NotificationsDialogState();
}

class _NotificationsDialogState extends State<_NotificationsDialog> {
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');
  List<NotificationItem>? _items;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final items = await widget.provider.getPaged();
      if (mounted) setState(() => _items = items);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _markAsRead(NotificationItem item) async {
    if (item.isRead) return;
    await widget.provider.markAsRead(item.id);
    await _load();
  }

  Future<void> _markAllAsRead() async {
    await widget.provider.markAllAsRead();
    await _load();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Obavijesti'),
      content: SizedBox(
        width: 420,
        height: 420,
        child: _error != null
            ? Center(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)))
            : _items == null
                ? const Center(child: CircularProgressIndicator())
                : _items!.isEmpty
                    ? const Center(child: Text('Nemate obavijesti.'))
                    : ListView.separated(
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
      actions: [
        TextButton(
          onPressed: (_items != null && _items!.any((n) => !n.isRead)) ? _markAllAsRead : null,
          child: const Text('Označi sve kao pročitano'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Zatvori'),
        ),
      ],
    );
  }
}
