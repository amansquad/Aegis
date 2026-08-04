/// Contracts mirroring the Aegis API, hand-written for the same reason `web/src/lib/types.ts` is:
/// this is the seam where a backend change should break the build loudly, rather than a generated
/// client silently absorbing a removed field.
class PagedResult<T> {
  const PagedResult({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.totalPages,
    required this.hasPreviousPage,
    required this.hasNextPage,
  });

  final List<T> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final int totalPages;
  final bool hasPreviousPage;
  final bool hasNextPage;

  factory PagedResult.fromJson(Map<String, dynamic> json, T Function(Map<String, dynamic>) fromJson) {
    return PagedResult(
      items: (json["items"] as List).map((e) => fromJson(e as Map<String, dynamic>)).toList(),
      page: json["page"] as int,
      pageSize: json["pageSize"] as int,
      totalCount: json["totalCount"] as int,
      totalPages: json["totalPages"] as int,
      hasPreviousPage: json["hasPreviousPage"] as bool,
      hasNextPage: json["hasNextPage"] as bool,
    );
  }
}

class AuthenticatedUser {
  const AuthenticatedUser({
    required this.id,
    required this.email,
    required this.displayName,
    required this.organizationId,
    required this.organizationName,
    required this.roles,
    required this.permissions,
  });

  final String id;
  final String email;
  final String displayName;
  final String organizationId;
  final String organizationName;
  final List<String> roles;
  final List<String> permissions;

  bool hasPermission(String permission) => permissions.contains(permission);

  factory AuthenticatedUser.fromJson(Map<String, dynamic> json) => AuthenticatedUser(
        id: json["id"] as String,
        email: json["email"] as String,
        displayName: json["displayName"] as String,
        organizationId: json["organizationId"] as String,
        organizationName: json["organizationName"] as String,
        roles: (json["roles"] as List).cast<String>(),
        permissions: (json["permissions"] as List).cast<String>(),
      );

  Map<String, dynamic> toJson() => {
        "id": id,
        "email": email,
        "displayName": displayName,
        "organizationId": organizationId,
        "organizationName": organizationName,
        "roles": roles,
        "permissions": permissions,
      };
}

class AuthenticationResult {
  const AuthenticationResult({
    required this.accessToken,
    required this.refreshToken,
    required this.accessTokenExpiresOnUtc,
    required this.tokenType,
    required this.user,
  });

  final String accessToken;
  final String refreshToken;
  final String accessTokenExpiresOnUtc;
  final String tokenType;
  final AuthenticatedUser user;

  factory AuthenticationResult.fromJson(Map<String, dynamic> json) => AuthenticationResult(
        accessToken: json["accessToken"] as String,
        refreshToken: json["refreshToken"] as String,
        accessTokenExpiresOnUtc: json["accessTokenExpiresOnUtc"] as String,
        tokenType: json["tokenType"] as String,
        user: AuthenticatedUser.fromJson(json["user"] as Map<String, dynamic>),
      );
}
