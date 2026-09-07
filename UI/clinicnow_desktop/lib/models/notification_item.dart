/// Mirrors the backend's `NotificationDto`.
class NotificationItem {
  final int id;
  final String title;
  final String text;
  final bool isRead;
  final DateTime createdAtUtc;
  final DateTime? readAtUtc;

  const NotificationItem({
    required this.id,
    required this.title,
    required this.text,
    required this.isRead,
    required this.createdAtUtc,
    this.readAtUtc,
  });

  factory NotificationItem.fromJson(Map<String, dynamic> json) {
    return NotificationItem(
      id: json['id'] as int,
      title: json['title'] as String? ?? '',
      text: json['text'] as String? ?? '',
      isRead: json['isRead'] as bool? ?? false,
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      readAtUtc: json['readAtUtc'] == null ? null : DateTime.parse(json['readAtUtc'] as String),
    );
  }

  /// Optimistic local copy for the moment between clicking a notification and
  /// the server confirming the read - see `NotificationCenter.markAsRead`.
  NotificationItem copyWithRead() => NotificationItem(
        id: id,
        title: title,
        text: text,
        isRead: true,
        createdAtUtc: createdAtUtc,
        readAtUtc: readAtUtc ?? DateTime.now().toUtc(),
      );
}
