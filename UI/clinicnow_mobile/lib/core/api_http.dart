import 'package:http/http.dart' as http;

import 'base_provider.dart';

/// Timeout-bounded, connection-reusing replacements for the top-level
/// `http.get`/`http.post`/... helpers.
///
/// [BaseProvider] covers the generic CRUD shape, but a good deal of the app
/// talks to the API outside it - the bespoke provider actions (confirm, cancel,
/// mark-read, capture), and the screens that download a file. Those called the
/// top-level `http` helpers directly, which meant two problems this file
/// removes everywhere at once:
///
/// - no timeout. Dart's default is effectively infinite, so on a dying mobile
///   connection those calls never completed and never failed: a spinner that
///   spins forever, with no error and no way out but killing the app. That
///   includes the notification poll, which is the one request guaranteed to be
///   running when the signal drops.
/// - a fresh connection per call, since each top-level helper opens a client
///   and closes it again - an extra TCP handshake on every request.
///
/// Transport failures surface as `ApiException(statusCode: 0)`, which is what
/// lets a caller tell "never reached the server" apart from a real rejection.
Future<http.Response> apiGet(
  Uri url, {
  Map<String, String>? headers,
  Duration? timeout,
}) =>
    BaseProvider.send(() => BaseProvider.client.get(url, headers: headers),
        timeout: timeout);

Future<http.Response> apiPost(
  Uri url, {
  Map<String, String>? headers,
  Object? body,
  Duration? timeout,
}) =>
    BaseProvider.send(
        () => BaseProvider.client.post(url, headers: headers, body: body),
        timeout: timeout);

Future<http.Response> apiPut(
  Uri url, {
  Map<String, String>? headers,
  Object? body,
  Duration? timeout,
}) =>
    BaseProvider.send(
        () => BaseProvider.client.put(url, headers: headers, body: body),
        timeout: timeout);

Future<http.Response> apiDelete(
  Uri url, {
  Map<String, String>? headers,
  Object? body,
  Duration? timeout,
}) =>
    BaseProvider.send(
        () => BaseProvider.client.delete(url, headers: headers, body: body),
        timeout: timeout);
