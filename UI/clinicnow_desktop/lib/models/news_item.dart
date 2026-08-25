/// Mirrors the backend's `NewsItemDto`. Never carries raw image bytes - only
/// [hasImage]/[imageUrl] (the actual bytes are served from a dedicated
/// endpoint, kept off the list/detail payload).
class NewsItem {
  final int id;
  final String title;
  final String text;
  final bool hasImage;
  final String? imageUrl;
  final DateTime createdAtUtc;

  const NewsItem({
    required this.id,
    required this.title,
    required this.text,
    required this.hasImage,
    this.imageUrl,
    required this.createdAtUtc,
  });

  factory NewsItem.fromJson(Map<String, dynamic> json) {
    return NewsItem(
      id: json['id'] as int,
      title: json['title'] as String? ?? '',
      text: json['text'] as String? ?? '',
      hasImage: json['hasImage'] as bool? ?? false,
      imageUrl: json['imageUrl'] as String?,
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
    );
  }
}
