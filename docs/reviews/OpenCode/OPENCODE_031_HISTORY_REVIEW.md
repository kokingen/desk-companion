# Desk Companion 0.3.1 Preview - authenticated history resume review

Author: OpenCode (coding sub-agent)
Date: 2026-11-17
Scope: one incremental Bluetooth task-history resume change. Existing working-tree changes were preserved.

## Constraints observed

- No deployment, service/process stop, device interaction, Windows setting change, dependency installation, private-data read, or unrelated file change.
- The 25 ms sender timer, crypto framing/counters, pairing, tokens, certificate pinning, security configuration, schema version, and old history rows were not changed.
- The live app was not restarted or inspected; the parent remains responsible for rebuild, test, and deployment.

## What changed

### Windows agent

`agent/Max3.Agent/BluetoothTransport.cs`

- The v2 hello now advertises `historyResume="event-id-v1"`.
- The phone's authenticated challenge may select `historyResume="event-id-v1"` and return `historyAfterId`.
- The selected feature and the exact cursor are appended to the existing handshake/key-derivation context as
  `|history-id-v1|<id>`. Both the phone proof and the PC proof use this context, so the ID cannot be changed in transit.
- The returned cursor is rejected unless it is at most 128 characters and contains only ASCII letters, digits,
  `:`, `-`, or `_`; the empty cursor is accepted.
- A peer that does not select the feature produces exactly the previous context and keeps the previous v1/v2 behavior.
- The negotiated cursor is mapped to an initial sequence and passed into `WriteStates`. Unknown or pruned IDs map to 0.

`agent/Max3.Agent/CodexEvents.cs`

- Added `CursorFor(eventId)`: maps a retained event identity to its retained sequence, or returns 0 when the ID is
  missing, unknown, or pruned.
- The mapping deliberately uses the earliest retained occurrence of a repeated identity. If an identity unexpectedly
  repeats, this can replay later known events, but it cannot skip distinct events between the two occurrences.

`agent/Max3.Tests/HistoryResumeTests.cs`, `agent/Max3.Tests/Program.cs`

- Focused checks for ID mapping, unknown/pruned fallback, repeated IDs, cursor bounds/characters, and exact
  legacy-versus-selected context suffixes.

### Android

`android/.../BluetoothLink.kt`

- If the PC offers the capability, the phone reads its checkpoint through the caller-supplied bounded reader and
  returns the selected capability plus `historyAfterId`.
- Only a valid stored cursor is returned; an absent/invalid/timed-out read returns the valid empty cursor, which makes
  the PC restart from 0 rather than skip history.
- The same selected feature and exact cursor are appended to the phone's proof/key-derivation context.
- The optional history fields are omitted entirely when the PC did not offer the capability, preserving the exact old
  handshake shape.
- After authentication, each decoded state receives local `historyPeer=<paired PC fingerprint>`. The value is assigned
  by `BluetoothLink`, not copied from the payload.

`android/.../CompanionActivity.kt`

- The checkpoint reader uses the existing single-writer `io` executor with a 1 second bounded wait. Any failure or
  timeout becomes the empty cursor.
- Wi-Fi states have any `historyPeer` field removed, so provenance is only accepted from the authenticated Bluetooth
  path.
- Existing cache, listener, generation ownership, delivery, and persistence behavior was retained.

`android/.../CompanionStore.kt`

- Added a checkpoint scoped by paired PC certificate fingerprint in the existing `settings` table.
- The checkpoint is the last event ID in an authenticated `eventHistory` batch, and is written in the same SQLite
  transaction as those event inserts. A crash therefore cannot expose the checkpoint without the corresponding rows,
  or the rows without the checkpoint.
- The live `ai` field, an ACK, and an empty/uncommitted batch never advance the checkpoint.
- Schema version and old histories are unchanged.

## Critical review

### Data loss

- The cursor is committed atomically with the batch it describes. If the insert or settings write fails, the
  transaction rolls back and the PC will replay from the older cursor.
- Unknown/pruned IDs restart from 0. A failed checkpoint read also restarts from 0. Both cases may resend history but
  cannot skip available retained history.
- Repeated event identities map to the earliest retained occurrence, preferring possible replay over a possible skip.
- A checkpoint can move backward if the PC journal was reset and the PC legitimately replays older IDs. That causes
  extra replay, not permanent loss. Android event deduplication by `external_id` still applies.
- The requested ID-only cursor assumes events before a known ID in the current journal were already delivered. If a
  journal reset re-observes the same identity after genuinely new events, those earlier new events could be skipped.
  A journal-generation marker would be needed to distinguish that case; it was not added because it would enlarge the
  protocol and this task explicitly requested an ID-only cursor. Unknown/pruned IDs still restart from 0.

### Tampering and downgrade

- Feature selection, the exact cursor, and the resulting keys are bound into both authentication proofs. Changing or
  removing `historyAfterId` after the phone selected the feature causes proof failure; a selected feature without a
  valid ID is rejected rather than silently replayed.
- A peer that never selects the feature keeps the old context, as required for older builds.
- As with the existing flow-capability negotiation, the PC hello is not yet part of the authenticated transcript. An
  active attacker able to rewrite the hello could suppress the offer and force the old replay behavior. That downgrade
  is bandwidth-only: it cannot make the PC skip history, and the ID itself is still authenticated.
- Local tampering with the phone's private SQLite settings is outside this protocol boundary; it can force replay or
  an attempted early cursor, but cannot forge the authenticated handshake context without the existing token.

### Lifecycle

- The handshake never touches SQLite directly from the Bluetooth thread. It submits one read to the existing
  single-writer executor and waits at most one second, so a busy backup/writer delays or safely falls back instead of
  deadlocking the handshake.
- Pause/stop/close still close the owned socket and invalidate the generation; a concurrent checkpoint read completes
  or times out and the subsequent handshake I/O fails normally.
- The checkpoint is only advanced by committed Bluetooth history, never by delivery, display, ACK, or live AI state.

### Residual risk

- This was not validated on a real phone/PC link, and no Android SQLite transaction was executed in an instrumentation
  test. The parent must rebuild, run the full device regression, and verify reconnect behavior before relying on it.
- The Android Kotlin code was compiled, but the end-to-end handshake, checkpoint transaction, and reconnect path were
  not executed here.

## Tests actually executed

1. Windows/.NET regression suite, first run after the feature was implemented:

   `work\tools\dotnet\dotnet.exe run --project agent/Max3.Tests/Max3.Tests.csproj -c Release --no-restore`

   Result: `83 checks passed`, including the new history-ID mapping, cursor validation, and context-suffix checks.
   Only pre-existing `CA1416` platform warnings were reported. At that point the context label was still the
   provisional `event-id-v1` string.

2. Windows/.NET rebuild after aligning the context label to `|history-id-v1|<id>`:

   The build compiled successfully, but executing the rebuilt `Max3.Tests.dll` was blocked by Windows application
   control (`0x800711C7`). I did not change or bypass that policy. Therefore the final constant-only adjustment is
   compiled but was not executed by the test assembly here. The earlier 83-check run exercised the same mapping,
   validation, and suffix logic except for that literal label.

3. Android Kotlin compile, after the final change:

   `gradlew.bat :app:compileDebugKotlin --offline --console=plain`

   Result: `BUILD SUCCESSFUL`. Only pre-existing deprecation warnings in `CompanionActivity.kt` and
   `CompanionPerformance.kt` were reported.

4. `git diff --check`

   Result: no whitespace errors; only existing CRLF/LF conversion warnings.

Not executed here: real-device Bluetooth reconnect, Android SQLite instrumentation, APK installation/deployment, or a
full 24-hour stability run. No such validation is claimed.
