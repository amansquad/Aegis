import "package:dio/dio.dart";

/// Thrown for any failed request, carrying enough of the server's `ProblemDetails` body to show
/// the user something actionable -- mirrors `ApiError` in the web client's `lib/api.ts` exactly,
/// because the two clients hit the same API and should surface the same failures the same way.
class ApiException implements Exception {
  ApiException(this.message, this.statusCode, [this.errorCode]);

  final String message;
  final int statusCode;
  final String? errorCode;

  @override
  String toString() => message;
}

/// The API base URL is a build-time value, not a hardcoded one, because "which backend" is an
/// environment fact -- an Android emulator, an iOS simulator and a physical device on the same
/// Wi-Fi each reach a local dev API through a different host. Override with
/// `--dart-define=API_BASE_URL=http://192.168.1.20:5282/api/v1` when running against something
/// other than the Android emulator default.
const _defaultBaseUrl = "http://10.0.2.2:5282/api/v1";
const apiBaseUrl = String.fromEnvironment("API_BASE_URL", defaultValue: _defaultBaseUrl);

typedef TokenReader = Future<String?> Function();

/// A thin Dio wrapper. Handlers throw `ApiException` rather than Dio's own exception type, so
/// every screen catches one exception shape regardless of what went wrong underneath.
class ApiClient {
  ApiClient({required TokenReader readToken}) : _readToken = readToken {
    _dio = Dio(BaseOptions(baseUrl: apiBaseUrl, connectTimeout: const Duration(seconds: 15)));

    _dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _readToken();
          if (token != null) {
            options.headers["Authorization"] = "Bearer $token";
          }
          handler.next(options);
        },
      ),
    );
  }

  late final Dio _dio;
  final TokenReader _readToken;

  Future<Map<String, dynamic>> getJson(String path, {Map<String, dynamic>? query}) async {
    final response = await _request(() => _dio.get(path, queryParameters: query));
    return response.data as Map<String, dynamic>;
  }

  Future<T> post<T>(String path, {Object? body, T Function(dynamic)? map}) async {
    final response = await _request(() => _dio.post(path, data: body));
    if (map != null) return map(response.data);
    return response.data as T;
  }

  Future<void> postNoContent(String path, {Object? body}) async {
    await _request(() => _dio.post(path, data: body));
  }

  Future<Response> _request(Future<Response> Function() send) async {
    try {
      return await send();
    } on DioException catch (error) {
      final response = error.response;

      if (response == null) {
        throw ApiException("Could not reach the server. Check your connection.", 0);
      }

      final data = response.data;
      String? title;
      String? detail;
      String? errorCode;
      Map<String, dynamic>? fieldErrors;

      if (data is Map<String, dynamic>) {
        title = data["title"] as String?;
        detail = data["detail"] as String?;
        errorCode = data["errorCode"] as String?;
        fieldErrors = data["errors"] as Map<String, dynamic>?;
      }

      // Field-level validation errors are flattened into one readable line, matching the web
      // client's treatment of the same `ValidationProblemDetails` shape.
      final flattened = fieldErrors?.values
          .expand((v) => (v as List).cast<String>())
          .join(" ");

      final message = (flattened?.isNotEmpty ?? false)
          ? flattened!
          : detail ?? title ?? "Request failed (${response.statusCode}).";

      throw ApiException(message, response.statusCode ?? 0, errorCode);
    }
  }
}
