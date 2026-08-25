import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/news_item.dart';

class NewsItemProvider extends BaseProvider<NewsItem> {
  NewsItemProvider(AuthSession authSession) : super('NewsItem', authSession);

  @override
  NewsItem fromJson(Map<String, dynamic> json) => NewsItem.fromJson(json);

  /// A full absolute URL clients can pass straight to `Image.network`, e.g.
  /// `http://localhost:5203/api/NewsItem/1/image`.
  String? absoluteImageUrl(NewsItem item) {
    if (item.imageUrl == null) return null;
    final normalizedBase = BaseProvider.baseUrl.endsWith('/')
        ? BaseProvider.baseUrl.substring(0, BaseProvider.baseUrl.length - 1)
        : BaseProvider.baseUrl;
    return '$normalizedBase${item.imageUrl}';
  }
}
