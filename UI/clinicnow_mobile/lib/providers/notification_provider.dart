import '../core/api_http.dart';

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/notification_item.dart';

/// Notifications have no client-facing Insert/Update - only reads and the two
/// mark-read actions - so this doesn't extend [BaseProvider]'s CRUD methods,
/// just reuses its URI/header/decode plumbing via a private helper instance.
class NotificationProvider {
  final _NotificationBaseProvider _base;

  NotificationProvider(AuthSession authSession) : _base = _NotificationBaseProvider(authSession);

  Future<List<NotificationItem>> getPaged({bool? isRead}) async {
    final query = <String, dynamic>{'pageSize': 50};
    if (isRead != null) query['isRead'] = isRead;
    final response = await apiGet(_base.buildUri('api/Notification', query), headers: _base.authHeaders());
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(NotificationItem.fromJson).toList();
  }

  Future<int> getUnreadCount() async {
    final response = await apiGet(_base.buildUri('api/Notification/unread-count'), headers: _base.authHeaders());
    final decoded = _base.decode(response);
    return decoded is int ? decoded : int.tryParse('$decoded') ?? 0;
  }

  Future<void> markAsRead(int id) async {
    final response = await apiPost(_base.buildUri('api/Notification/$id/mark-read'), headers: _base.authHeaders());
    _base.decode(response, allowEmptyBody: true);
  }

  Future<void> markAllAsRead() async {
    final response = await apiPost(_base.buildUri('api/Notification/mark-all-read'), headers: _base.authHeaders());
    _base.decode(response, allowEmptyBody: true);
  }
}

class _NotificationBaseProvider extends BaseProvider<void> {
  _NotificationBaseProvider(AuthSession authSession) : super('Notification', authSession);

  @override
  void fromJson(Map<String, dynamic> json) {}
}
