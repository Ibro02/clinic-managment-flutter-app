/// Mirrors `ClinicNow.Model.Common.PagedResult<T>` from the backend - the
/// envelope returned by every list endpoint (rulebook Part II §D: pagination
/// is mandatory on every list endpoint, so every list-fetching call in this
/// app goes through this shape).
class PagedResult<T> {
  final List<T> resultList;
  final int count;

  PagedResult({required this.resultList, required this.count});

  factory PagedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) fromJsonT,
  ) {
    final rawList = json['resultList'] as List<dynamic>? ?? [];
    return PagedResult<T>(
      resultList: rawList
          .map((item) => fromJsonT(item as Map<String, dynamic>))
          .toList(),
      count: json['count'] as int? ?? 0,
    );
  }
}
