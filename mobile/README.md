# Aegis Mobile

The field/mobile client for Aegis, with the same module coverage as the web app: assets,
incidents, work orders, and maintenance plans. Flutter, talking to the same ASP.NET Core API
as the web client — no separate backend, no demo mode. It always needs a live Aegis API to
sign in against.

## Running against a local API

Start `Aegis.Api` first (from the repository root):

```bash
dotnet run --project src/Aegis.Api
```

By default it listens on `http://localhost:5282`. The mobile app needs to reach that from
wherever it's actually running, which is a different host depending on the target:

| Target                        | Host to use instead of `localhost` |
| ------------------------------ | ----------------------------------- |
| Android emulator (default)     | `10.0.2.2` (already the default — no flag needed) |
| iOS simulator                  | `localhost` |
| Physical device (same Wi-Fi)   | your machine's LAN IP, e.g. `192.168.1.20` |

Override with `--dart-define` when the default doesn't apply:

```bash
flutter run --dart-define=API_BASE_URL=http://localhost:5282/api/v1        # iOS simulator
flutter run --dart-define=API_BASE_URL=http://192.168.1.20:5282/api/v1     # physical device
```

Sign in with an account already registered through the web app or the API's
`/auth/register` endpoint — there is no separate mobile-only account system.

## Architecture

Mirrors the web client's separation of concerns, adapted to Flutter's idioms:

- **`lib/models/`** — hand-written types matching the API's JSON shapes, the same role
  `web/src/lib/types.ts` plays. Kept in sync by hand, deliberately: a generated client would
  absorb a breaking API change silently instead of failing the build.
- **`lib/data/`** — `ApiClient` (a thin Dio wrapper translating failures into one `ApiException`
  shape), per-module repositories, and `SessionNotifier` (Riverpod) holding the signed-in user
  and access token, persisted via the platform keystore/keychain rather than plain preferences.
- **`lib/core/theme.dart`** — the web client's control-room colour tokens, ported to a
  `ThemeData` pair (dark default, light second theme) so the two clients read as one product.
- **`lib/features/<module>/`** — one folder per module, each with a list screen and the
  action sheets/dialogs for that module's write operations (report, triage, dispatch, assign,
  complete, generate, etc.).

## Commands

```bash
flutter pub get       # install dependencies
flutter analyze       # static analysis
flutter test          # widget tests
flutter run           # run on a connected device/emulator
```
