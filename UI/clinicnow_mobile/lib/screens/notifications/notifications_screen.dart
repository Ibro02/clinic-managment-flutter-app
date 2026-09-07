import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/design_tokens.dart';
import '../../core/notification_center.dart';
import '../../models/notification_item.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';

/// The patient's own notifications - read/unread, mark-read, mark-all-read.
///
/// The list itself now auto-refreshes (review item C15): while this screen is
/// on top it registers as a watcher on the shared [NotificationCenter], whose
/// 20-second tick then fetches the list as well as the badge count, so a
/// notification arriving while the patient is looking at this screen appears
/// on its own. Pull-to-refresh stays, as a way to ask for it *now* - not as
/// the only way to see anything new (rulebook Part II §G).
class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');
  late final NotificationCenter _center;

  @override
  void initState() {
    super.initState();
    _center = context.read<NotificationCenter>();
    _center.beginWatchingList();
  }

  @override
  void dispose() {
    // Read once in initState rather than through `context` here: by dispose
    // time this element is already detached from the tree.
    _center.endWatchingList();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final center = context.watch<NotificationCenter>();
    final items = center.items;
    final hasUnread = items?.any((n) => !n.isRead) ?? false;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Obavijesti'),
        actions: [
          if (hasUnread)
            // No hardcoded colour: the app bar sits on `surface` now, so white
            // text would be invisible. The theme's own action colour is correct
            // in both light and dark.
            TextButton(
              onPressed: center.markAllAsRead,
              child: const Text('Označi sve'),
            ),
          const SizedBox(width: AppSpacing.xs),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: center.refresh,
        child: _content(context, center, items),
      ),
    );
  }

  Widget _content(BuildContext context, NotificationCenter center, List<NotificationItem>? items) {
    const padding = EdgeInsets.all(AppSpacing.md);

    // Nothing loaded yet and the fetch failed - this is the only case where the
    // error replaces the content, because there is no content to protect.
    if (items == null && center.listError != null) {
      return ListView(
        padding: padding,
        children: [
          AppErrorState(
            title: 'Obavijesti nisu učitane',
            message: center.listError!,
            onRetry: center.refresh,
          ),
        ],
      );
    }

    if (items == null) {
      return const Center(child: CircularProgressIndicator());
    }

    if (items.isEmpty) {
      return ListView(
        padding: padding,
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          if (center.listError != null) ...[_staleNotice(center), const SizedBox(height: AppSpacing.md)],
          const AppEmptyState(
            icon: Icons.notifications_none_rounded,
            title: 'Nemate obavijesti',
            message: 'Obavijesti o vašim terminima i uplatama pojavit će se ovdje.',
          ),
        ],
      );
    }

    return ListView.separated(
      padding: padding,
      physics: const AlwaysScrollableScrollPhysics(),
      // +1 for the staleness notice when the last poll failed. It is added
      // above the list rather than replacing it: notifications already on
      // screen are still true, and swapping them for an error would hide
      // correct information because a later refresh failed.
      itemCount: items.length + (center.listError != null ? 1 : 0),
      separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
      itemBuilder: (context, index) {
        if (center.listError != null) {
          if (index == 0) return _staleNotice(center);
          index--;
        }

        final item = items[index];

        return AppListCard(
          icon: item.isRead ? Icons.mail_outline : Icons.mark_email_unread_rounded,
          tone: item.isRead ? AppTone.neutral : AppTone.primary,
          // An unread notification is tinted rather than just bolded -
          // weight alone is easy to miss in a long list.
          highlighted: !item.isRead,
          title: item.title,
          subtitle: item.text,
          meta: _dateFormat.format(item.createdAtUtc.toLocal()),
          onTap: () => center.markAsRead(item),
        );
      },
    );
  }

  Widget _staleNotice(NotificationCenter center) => AppNotice(
        tone: AppTone.warning,
        icon: Icons.sync_problem_rounded,
        message: 'Lista možda nije najnovija. ${center.listError!}',
      );
}
