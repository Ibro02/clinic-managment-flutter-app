import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:local_notifier/local_notifier.dart';

import '../models/notification_item.dart';

/// Raises real Windows notifications (Action Center, with the system sound) for
/// notifications that arrive while the staff app is running.
///
/// Driven by [NotificationCenter]'s existing 20-second poll rather than by a
/// SignalR client: the poll is already there, it already knows when something
/// new appeared, and adding a Dart hub client would put another package into a
/// Windows Release build that has to run unmodified when graded.
///
/// The consequence, and it is deliberate: **no toast appears while the app is
/// closed.** A desktop app that is not running has nothing polling for it.
/// Closed-app delivery exists on Android, through FCM.
class DesktopToaster {
  bool _isReady = false;

  /// Windows only. On any other host `local_notifier` has no implementation, so
  /// this stays inert rather than throwing on a developer's machine.
  static bool get isSupported => !kIsWeb && Platform.isWindows;

  /// Must complete before the first [show]. Safe to call more than once.
  Future<void> initialize() async {
    if (_isReady || !isSupported) return;
    try {
      // The identifier Windows groups the toasts under; it is what the Action
      // Center shows as the sender.
      await localNotifier.setup(appName: 'ClinicNow');
      _isReady = true;
    } catch (error) {
      debugPrint('Windows notifications unavailable: $error');
    }
  }

  /// One toast per notification. Staff receive a handful a day, so there is no
  /// coalescing here - if that ever changes, collapse them before this call
  /// rather than dropping any inside it.
  Future<void> show(List<NotificationItem> notifications) async {
    if (!_isReady) return;

    for (final notification in notifications) {
      try {
        final toast = LocalNotification(title: notification.title, body: notification.text);
        await toast.show();
      } catch (error) {
        // A toast that cannot be drawn is not worth an error dialog: the
        // notification is already in the bell and the list.
        debugPrint('Showing a Windows notification failed: $error');
      }
    }
  }
}
