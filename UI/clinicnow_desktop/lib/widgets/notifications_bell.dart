import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/notification_center.dart';
import '../models/notification_item.dart';

/// A bell icon with an unread-count badge that opens the notification list.
///
/// Both the badge and the list read from the shared [NotificationCenter], which
/// polls every 20 seconds (rulebook Part II §G allows SignalR *or* polling; see
/// that class for why polling was chosen here). The list used to load once when
/// the dialog opened and never again - review item C15 - so a notification
/// arriving while a receptionist had the dialog open stayed invisible until
/// they closed and reopened it.
class NotificationsBell extends StatelessWidget {
  const NotificationsBell({super.key});

  @override
  Widget build(BuildContext context) {
    final unreadCount = context.watch<NotificationCenter>().unreadCount;

    return IconButton(
      tooltip: 'Obavijesti',
      onPressed: () => showDialog<void>(
        context: context,
        builder: (_) => const _NotificationsDialog(),
      ),
      icon: Badge(
        label: Text('$unreadCount'),
        isLabelVisible: unreadCount > 0,
        child: const Icon(Icons.notifications_outlined),
      ),
    );
  }
}

class _NotificationsDialog extends StatefulWidget {
  const _NotificationsDialog();

  @override
  State<_NotificationsDialog> createState() => _NotificationsDialogState();
}

class _NotificationsDialogState extends State<_NotificationsDialog> {
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');
  late final NotificationCenter _center;

  @override
  void initState() {
    super.initState();
    // Registering as a watcher is what makes the shared timer fetch the list,
    // not just the badge count, for as long as this dialog is open.
    _center = context.read<NotificationCenter>();
    _center.beginWatchingList();
  }

  @override
  void dispose() {
    // Read in initState rather than through `context` here: by dispose time
    // this element is already detached from the tree.
    _center.endWatchingList();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final center = context.watch<NotificationCenter>();
    final items = center.items;

    return AlertDialog(
      title: const Text('Obavijesti'),
      content: SizedBox(
        width: 420,
        height: 420,
        child: _content(context, center, items),
      ),
      actions: [
        TextButton(
          onPressed: (items != null && items.any((n) => !n.isRead)) ? center.markAllAsRead : null,
          child: const Text('Označi sve kao pročitano'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Zatvori'),
        ),
      ],
    );
  }

  Widget _content(BuildContext context, NotificationCenter center, List<NotificationItem>? items) {
    // Only replace the content with the error when there is no content to
    // protect. A poll that fails while rows are already on screen shows the
    // notice above them instead - those rows are still true.
    if (items == null) {
      return center.listError != null
          ? _errorState(context, center)
          : const Center(child: CircularProgressIndicator());
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (center.listError != null) _staleNotice(context, center),
        Expanded(
          child: items.isEmpty
              ? const Center(child: Text('Nemate obavijesti.'))
              : ListView.separated(
                  itemCount: items.length,
                  separatorBuilder: (context, index) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return ListTile(
                      leading: Icon(
                        item.isRead ? Icons.mail_outline : Icons.mark_email_unread,
                        color: item.isRead ? null : Theme.of(context).colorScheme.primary,
                      ),
                      title: Text(
                        item.title,
                        style: TextStyle(fontWeight: item.isRead ? FontWeight.normal : FontWeight.bold),
                      ),
                      subtitle: Text('${item.text}\n${_dateFormat.format(item.createdAtUtc.toLocal())}'),
                      isThreeLine: true,
                      onTap: () => center.markAsRead(item),
                    );
                  },
                ),
        ),
      ],
    );
  }

  Widget _errorState(BuildContext context, NotificationCenter center) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text('Obavijesti nisu učitane', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Text(center.listError!, textAlign: TextAlign.center),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: center.refresh,
            icon: const Icon(Icons.refresh_rounded, size: 17),
            label: const Text('Pokušaj ponovo'),
          ),
        ],
      ),
    );
  }

  Widget _staleNotice(BuildContext context, NotificationCenter center) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.sync_problem_rounded, size: 16, color: Theme.of(context).colorScheme.error),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              'Lista možda nije najnovija. ${center.listError!}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        ],
      ),
    );
  }
}
