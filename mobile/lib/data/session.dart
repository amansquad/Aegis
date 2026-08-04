import "dart:convert";

import "package:flutter_riverpod/flutter_riverpod.dart";
import "package:flutter_secure_storage/flutter_secure_storage.dart";

import "../models/common.dart";

/// Client-side session state -- the mobile equivalent of the web client's `useSession` Zustand
/// store. The token lives in the platform keystore/keychain via `flutter_secure_storage`, not
/// plain preferences, because a bearer token is a credential rather than a display setting.
class SessionState {
  const SessionState({this.token, this.user});

  final String? token;
  final AuthenticatedUser? user;

  bool get isSignedIn => token != null && user != null;

  bool hasPermission(String permission) => user?.hasPermission(permission) ?? false;
}

const _storage = FlutterSecureStorage();
const _tokenKey = "aegis.token";
const _userKey = "aegis.user";

class SessionNotifier extends Notifier<SessionState> {
  @override
  SessionState build() {
    // Fired once at construction; the actual restored state lands asynchronously once secure
    // storage responds, same shape as the web client's zustand `persist` hydration.
    _restore();
    return const SessionState();
  }

  Future<void> _restore() async {
    final token = await _storage.read(key: _tokenKey);
    final userJson = await _storage.read(key: _userKey);

    if (token != null && userJson != null) {
      state = SessionState(
        token: token,
        user: AuthenticatedUser.fromJson(_decodeUser(userJson)),
      );
    }
  }

  Future<void> signIn(AuthenticationResult result) async {
    await _storage.write(key: _tokenKey, value: result.accessToken);
    await _storage.write(key: _userKey, value: _encodeUser(result.user));
    state = SessionState(token: result.accessToken, user: result.user);
  }

  Future<void> signOut() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _userKey);
    state = const SessionState();
  }
}

final sessionProvider = NotifierProvider<SessionNotifier, SessionState>(SessionNotifier.new);

String _encodeUser(AuthenticatedUser user) => jsonEncode(user.toJson());

Map<String, dynamic> _decodeUser(String encoded) => jsonDecode(encoded) as Map<String, dynamic>;
