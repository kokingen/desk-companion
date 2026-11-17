# Desk Companion 0.3.1 persistence-failure fence review

Author: OpenCode (coding sub-agent)
Date: 2026-11-17
Scope: one focused correctness fix for a failed Android `store.accept` advancing past uncommitted history.

## Constraints observed

- No deployment, device/process control, Windows setting change, private-data read, or dependency installation.
- Existing working-tree work was preserved, including the Collector sample-start gate, CSS containment, and the
  visual-fence recording guard.
- Framing, HMAC/key-derivation contexts, checkpoint schema, resume mapping, and the wire protocol were not changed.
- No real phone/PC link, APK install, or full stability run was performed. No device result is claimed.

## The real gap

The alleged "missing middle RFCOMM frame" mechanism is not valid: RFCOMM is ordered/reliable and the encrypted
message counters reject a missing frame. The real related gap was that `CompanionActivity` wrapped every
`store.accept(state)` in `runCatching`. If one SQLite transaction failed, a later queued batch could commit and
advance the durable history checkpoint past the event whose transaction had failed.

## What changed

### `HistoryGenerationFence.kt` (new, independently testable)

- Constant-space, monotonic rejected-through generation fence.
- Only positive generations are rejected, so Wi-Fi persistence (`historyGeneration` absent or 0) is never skipped.
- Rejecting generation N keeps every older generation rejected; a delayed rejection cannot lower or re-admit older
  work, and newer generations remain admitted.

### `BluetoothLink.kt`

- Every authenticated Bluetooth connection receives a strictly increasing local `historyGeneration` assigned under
  the existing lifecycle lock. This counter is separate from the lifecycle `generation`, which is shared across
  reconnect iterations.
- Each decoded Bluetooth state gets local `historyGeneration` alongside local `historyPeer` after authentication.
  A value supplied on the wire is overwritten; it is never trusted.
- Added `rejectHistoryGeneration(epoch)`. It inspects the active epoch under the lifecycle lock and closes only the
  socket owned by that exact epoch. A delayed failure for an older epoch cannot close a newer connection.
- Existing stop/start/generation semantics, authentication, and `sendLock` ownership are unchanged.

### `CompanionActivity.kt`

- Added the shared `HistoryGenerationFence` on the existing single-writer IO executor.
- A queued persistence task reads the positive local `historyGeneration` and skips the task when it is already
  rejected through.
- If `store.accept` throws, the task first advances the fence to `max(rejectedThrough, epoch)`, records a
  non-sensitive persistence failure, then asks `BluetoothLink` to close only that matching connection. The next
  handshake reads the last committed durable checkpoint, so available history replays.
- Wi-Fi states have `historyGeneration` stripped exactly like `historyPeer`, so only the authenticated Bluetooth
  path can fence a generation.
- A failure after the history transaction has committed is still safe to reconnect because the checkpoint was
  committed atomically with the batch.

### `CompanionPerformance.kt`

- Added `persistenceFailures` to the existing DUMP-protected status.
- Added `persistenceFailure(error)` which increments the counter for any `store.accept` exception, including Wi-Fi.
  It logs only the exception class, never task text, credentials, or message contents.

## Auxiliary JVM checks

`HistoryGenerationChecks.kt` covers:

1. Failure in generation 1 rejects queued generation-1 work.
2. Generation 2 remains admitted.
3. Rejection-through generation 2 still rejects generation 1.
4. A stale generation-1 failure neither lowers the fence nor blocks generation 3.
5. Non-positive generations are never fenced.
6. Rejecting generation 3 rejects all older work while admitting generation 4.

These are synthetic fence checks, not hardware acceptance.

## Tests actually executed

1. Android compile of main and unit-test Kotlin sources:

   `gradlew.bat :app:compileDebugKotlin :app:compileDebugUnitTestKotlin --offline --console=plain --no-daemon`

   Environment: `JAVA_HOME=work/tools/java/jdk-17.0.21+9`,
   `ANDROID_HOME=work/tools/android-sdk`, `GRADLE_USER_HOME=work/tools/gradle`.

   Result: `BUILD SUCCESSFUL`. Only pre-existing deprecation warnings in `CompanionActivity.kt` and
   `CompanionPerformance.kt` were reported.

2. Executed the new fence checks directly with the compiled classes and cached Kotlin stdlib:

   `java -cp "<debug classes>;<debugUnitTest classes>;kotlin-stdlib-2.0.21.jar" net.oxog.phonebox.deskpanel.HistoryGenerationChecks`

   Result: `7 history-generation checks passed`.

3. `git diff --check`

   Result: no whitespace errors; only existing CRLF/LF conversion warnings.

Not executed here: real-device Bluetooth reconnect, Android SQLite instrumentation/fault injection, APK
installation/deployment, or a long-run stability capture. No such validation is claimed.
