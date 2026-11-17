# Desk Companion 0.3.1 — Final Independent Source Review

Reviewer role: independent, local, read-only final-source reviewer.
Scope: current working-tree source for the history-persistence-failure fence and the Bluetooth transport/counters. Files inspected:

- `agent/Max3.Agent/LatestFrameWindow.cs`
- `agent/Max3.Agent/BluetoothTransport.cs`
- `agent/Max3.Agent/BluetoothTelemetry.cs`
- `agent/Max3.Agent/BluetoothCrypto.cs` (transport counter/framing verification)
- `agent/Max3.Agent/CodexEvents.cs` (cursor producer verification)
- `android/.../BluetoothLink.kt`
- `android/.../CompanionActivity.kt`
- `android/.../CompanionStore.kt`
- `android/.../HistoryGenerationFence.kt`
- `android/.../BluetoothTelemetry.kt`

Method: static source reading only. No source edits, no build, no deploy, no device run, no fault injection, no OS changes, no dependency installs, no private config read.

## Result

No blocking defect was found in the reviewed history-fence/transport/counter scope within static inspection.

## Verified properties (with evidence)

1. Persistence failure is fenced to a locally-trusted connection generation.
   - `HistoryGenerationFence.kt:5-8` is a constant-space monotonic fence; `isRejected` only rejects `generation > 0 && generation <= rejectedThrough`, so it rejects the failed generation and older ones, never a newer one.
   - `CompanionActivity.kt:149-152` reads the generation from top-level state, checks `isRejected` before `store.accept`, and on any throw calls both `historyFence.reject(epoch)` and `bluetooth.rejectHistoryGeneration(epoch)`.
   - Rejection is per-connection and only closes the socket that produced the failure: `BluetoothLink.kt:84-88` returns `socket` only when `activeHistoryGeneration == epoch`.
   - `stop()` and a new accept clear the active generation: `BluetoothLink.kt:82` sets `activeHistoryGeneration = 0L`; `BluetoothLink.kt:102` sets `activeHistoryGeneration = 0L` under `lifecycle` at accept. Thus a late failure from a prior epoch cannot close a freshly accepted/authenticating socket.
   - The loop `finally` clears the active generation when it matches the finishing connection (`BluetoothLink.kt:147`).

2. Epoch is local provenance, overwritten after authentication, stripped from Wi-Fi.
   - The generation is assigned only after the encrypted keys are installed and the handshake proof is verified: `BluetoothLink.kt:120-122` (`historyEpoch = ++historyGeneration; activeHistoryGeneration = historyEpoch` inside `lifecycle`).
   - It is written onto the outgoing state only after `telemetry.decode`/`getJSONObject`, so any client-supplied top-level `historyGeneration`/`historyPeer` is overwritten: `BluetoothLink.kt:131`.
   - Wi-Fi source strips both fields before delivery/persistence: `CompanionActivity.kt:135`.
   - Therefore the epoch does not need a wire HMAC; it is not client-controlled.

3. The checkpoint is not advanced by a swallowed SQLite failure.
   - The backlog events and the settings cursor are written in one transaction with `setTransactionSuccessful()` only after both: `CompanionStore.kt:59-71`. Any throw before that rolls back both via `finally { db.endTransaction() }`.
   - The cursor is the last event in the authenticated batch, not the live `ai` field or ACK: `CompanionStore.kt:56`; it is validated with the same cursor rules before storage: `CompanionStore.kt:58`.
   - The next handshake reads the committed value through the single-writer IO executor: `CompanionActivity.kt:105-109` submits `store.historyCheckpoint(fingerprint)` to the FIFO `io` executor and waits; `CompanionStore.kt:43-48` reads committed `settings.value`; `BluetoothTransport.cs:65-71,85` consumes it via `ai.CursorFor(...)`, which returns 0 for an empty/unknown id (`CodexEvents.cs:40-44`). FIFO ordering means a queued failed `accept` (and its rollback) completes before this read.

4. ACK/ledger race is closed; lifetime counters are post-write and exclude control.
   - Registration precedes the encrypted write: `BluetoothTransport.cs:105` calls `window.Sent(sequence)` before `channel.Write` at `:106`, so a fast ACK cannot reference an unregistered frame.
   - Lifetime/per-connection frame and byte counters increment only after `channel.Write` returns (`BluetoothTransport.cs:106-108`); the `route` control frame is written at `:103` and is not counted.
   - Cumulative ACK is bounded by the window capacity: `LatestFrameWindow.cs:7,14,17` (capacity 3; admission rejects overflow) and the phone acks only the latest received sequence (`BluetoothLink.kt:131,163-167`). The stop-wait (older v1) path is retained via `Channel` at `BluetoothTransport.cs:109,123`.
   - Duplicate/delayed cumulative ACKs add no credit: `LatestFrameWindow.cs:21`.

5. No silent lost-middle-frame.
   - Both directions enforce strict monotonic encrypted counters: `BluetoothCrypto.cs:49-53` (`counter != received + 1`), and `BluetoothLink.kt:180-185` (`counter != expectedCounter`). Any loss/reorder breaks the connection rather than desynchronizing the window.
   - Telemetry positional layout matches 15 fields on both sides: `BluetoothTelemetry.cs:21-29` vs `BluetoothTelemetry.kt:13-32`.

## Non-blocking observations (not defects, no fix requested)

- `HistoryGenerationFence.kt:6-7` intentionally rejects all generations `<=` the failed one, which can drop an older-epoch state still queued in IO. This is safe because the committed SQLite cursor remains behind and the event is replayed (CONFLICT_IGNORE) on the next handshake. Informational only.
- `CompanionActivity.kt:145-147` advances the `lastPersistAt`/`lastTaskId` throttle when the IO job is scheduled, before `store.accept` runs. A fence-dropped job therefore still counts against the throttle. Effect is bounded to a throttling delay, is not checkpoint-related, and self-heals; not a correctness defect for the reviewed fence.
- Per-connection reset of `sentFrames` (`BluetoothTransport.cs:78`) means window rate measurements must use the lifetime `TotalSentFrames` (`BluetoothTransport.cs:13,29,108`), which the current source provides. Measurement accounting note, not a source bug.

## Evidence posture / non-claims

- `031-testing-scope-final.json`: `longRun24Hours = CANCELLED_BY_USER`, `final24HourRunStarted = false`, Windows OS restart/sleep-wake and final 30-minute FPS `NOT_TESTED`. No acceptance is claimed.
- `031-final-windows-analysis.json` is a short (~74.9 s) Windows observation of the delivered EXE (`connectionsDuringWindow = 0`, `peakOutstanding = 3`, `sendCounterSource = process-lifetime successful encrypted STATE writes`).
- `analysis.json` is a phone-side capture; its `windows` block reports a per-connection counter reset and `cpu`/`memory` `measured = false`, and it does not constitute acceptance of the fixed final candidate.
- Per instruction, the final long-run/final-candidate phone trace is treated as unretrieved and no acceptance is claimed.
- This review explicitly claims no hardware, fault-injection, OS restart/sleep-wake, or device-test results. Conclusions are limited to static source inspection of the listed files.
