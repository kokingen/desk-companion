# Contributing

Thank you for taking the time to improve Desk Companion. The project is currently a **0.3.1 Preview**; please keep changes small, reproducible and explicit about what has and has not been tested.

## Before opening a pull request

1. Read the [README](README.md), [delivery/build notes](docs/031-DELIVERY.md), [validation record](docs/VALIDATION.md), [privacy notes](PRIVACY.md) and [security boundaries](SECURITY.md).
2. For a bug, include the exact app version, Windows/Android version, device model, reproduction steps and expected/actual behaviour.
3. For a larger feature, open an issue first so its scope and compatibility impact can be discussed.

## Development guidelines

- Keep Windows Agent, Android client and shared protocol changes scoped and documented.
- Preserve per-field timestamps; do not present stale or missing metrics as fresh values.
- Preserve authentication, encryption, pairing, private-data permissions and local-only management boundaries.
- Do not add telemetry, public network endpoints, remote command execution or automatic system-security changes without a separately reviewed design.
- Keep dependency lock files and verification metadata in sync with dependency changes.
- Preserve upstream copyright and third-party license notices. See [Sources](docs/SOURCES.md) and [Third-party notices](THIRD_PARTY_NOTICES.md).

## Build and validation

Use the toolchain versions and locked dependency procedures documented in [031-DELIVERY.md](docs/031-DELIVERY.md). Report the exact commands and environment used.

When describing test results, distinguish:
- source compilation from test execution;
- synthetic/UI checks from physical-device tests;
- Agent process restart from Windows OS reboot;
- instrumented FPS from normal-use FPS;
- completed checks from **NOT TESTED** or cancelled checks.

Do not describe an unexecuted or cancelled test as passing. The current preview's known limitations are listed in the [README](README.md#known-limitations) and [VALIDATION.md](docs/VALIDATION.md).

## Keep private data out of Git

Never commit or attach:
- Windows `private-data`, management tokens, pairing keys or certificate private keys;
- pairing links/codes, device serials, Bluetooth MAC addresses or personal IP/host details;
- Android app databases, raw diagnostic logs or personal screenshots;
- Codex prompts, message bodies, command arguments or private task identifiers;
- build output, caches, local environment files or unrelated Git history.

Use minimal, redacted excerpts only. A hashed identifier may still be pseudonymous personal data.

## Pull requests

- Explain the user-visible behaviour and the reason for the change.
- List the files/components affected and tests actually run.
- Call out compatibility, protocol, migration, privacy and security implications.
- Include screenshots only when they are genuine captures of the changed application and have been checked for secrets and personal information.
