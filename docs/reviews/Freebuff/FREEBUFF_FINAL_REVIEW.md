# Independent review — authenticated history resume (0.3.1 candidate)

Scope: static review of the `history-resume/` snapshot (7 source files + OpenCode report), superseding
the matching originals. Method: reading only — no build, test, emulator/device or hash check (shell
unavailable). OpenCode's executed-test claims are reported as claims, not reproduced.

## Confirmed P1 — silent, permanent history loss on one dropped frame

The resume design has no way to notice a gap, and it advances its durable cursor past one.

- `agent/Max3.Agent/BluetoothTransport.cs:101` sends one history event per frame in v2
  (`history.Take(1)`), and `:110` advances the in-memory cursor immediately after
  `channel.Write(...)` completes — i.e. on send, not on phone commit.
- `agent/Max3.Agent/LatestFrameWindow.cs:24-30` acknowledges cumulatively (`TakeWhile(s<=sequence)`)
  and never retransmits; a middle frame that is lost is simply covered by the next ACK. The only
  failure signal is the 1500 ms window expiry, which kills the whole connection.
- `CompanionStore.kt:57-71` then stores, atomically, the ID of the **last event of whatever batch was
  received**. It does not record the first/last sequence of a contiguous run, does not compare against
  the previous checkpoint, and never learns that an earlier batch was skipped.

Concrete sequence: batch `[e5]` is dropped; the phone keeps `e4`; batch `[e6]` arrives, commits, and
the checkpoint becomes `e6`. On reconnect `CursorFor("e6")`=6, so `e5` is never sent again and is
absent forever — data loss, not replay, and distinct from the journal-reset case OpenCode
acknowledges. Any lost frame while the link survives triggers it; RFCOMM is link-reliable, so the
real-world rate is untested, but there is no defence and no detection. Fix: checkpoint only a
contiguous, commit-acknowledged batch ("prevId..lastId", reject forward jumps).

## Confirmed P2

1. **Checkpoint read can always fall back to full replay.** `CompanionActivity.kt:105` blocks the
   Bluetooth accept thread on `io.submit{historyCheckpoint}...get(1, SECONDS)`; the same single-writer
   executor runs `store.accept` and a 5 s `backupHistory` (`:44`). Under backlog/backup the 1 s wait
   expires and `BluetoothLink.kt:106` substitutes `""`, replaying retained history. Bounded (no loss),
   but the "resume" property silently degrades whenever persistence is busy.
2. **The new tests are not demonstrably wired.** `HistoryResumeTests.cs` exists, but this snapshot has
   no `agent/Max3.Tests/Program.cs` or `.csproj`; `CodexEvents`/`CursorFor` appear only in the snapshot
   directory. OpenCode says Program.cs was changed; it is absent from `SOURCE_MANIFEST.json`, so the
   claimed "83 checks" cannot be tied to the snapshot. It also admits the final
   `|history-id-v1|` label change compiled but never executed (app-control block), so the exact
   context used in the reviewed code has no passing run behind it.
3. **Resume drain rate is very slow.** One event per ~100 ms frame at 9.9 Hz means a 2000-event retained
   journal takes ~200 s to replay, and the checkpoint lags that whole time. A reconnect during backlog
   restarts from the old checkpoint and replays again (dedup keeps it correct, bandwidth only).
4. **Downgrade is cheap.** The PC hello is still outside the authenticated transcript, so an active
   attacker can strip `historyResume` and force legacy full replay. Bandwidth-only, as OpenCode states,
   but the same class of issue should be fixed by authenticating the hello.
5. **Long-capture evidence is impossible with this build.** `CompanionPerformance.kt:88` bounds
   `seconds` to `1..3600`, so the required 30-minute and ≥24-hour captures cannot be produced through
   the diagnostics control as shipped.

## Hypotheses (not confirmed)

- Repeated identity after a journal reset can skip genuinely new events observed before the repeat;
  `CursorFor` uses the earliest retained occurrence (`CodexEvents.cs:40-45`) and `Remember` only guards
  the immediately previous ID (`:47`). OpenCode flags this; no generation marker exists.
- The 128-char cursor bound is ample for current IDs (`desktop:<24hex>`, `<12hex>:<ms>:<state>`), but a
  future ID shape exceeding it would be rejected by the PC while the phone silently sends it.
- The `historyPeer`/fingerprint scoping is safe against a re-pair race (stale writes land under the old
  key), but that path has never been exercised.

## Verified sound (static reading)

- Context binding is symmetric: `BluetoothTransport.cs:71` and `BluetoothLink.kt:107` append the same
  `|history-id-v1|<id>`, both sides key the proofs on it, and the fields are omitted entirely when the
  phone did not offer the feature — so old/new combinations keep the legacy handshake.
- Cursor validation matches on both sides (≤128, ASCII `[A-Za-z0-9:_-]`, empty accepted;
  `BluetoothTransport.cs:21-24`, `HistoryResume.isValid` in `CompanionStore.kt:9-15`).
- `historyPeer` is assigned by `BluetoothLink` from the authenticated pairing, and Wi-Fi states have it
  stripped in `acceptState`; the PC payload cannot self-assert provenance.
- Checkpoint and backlog rows share one transaction (`CompanionStore.kt:60-71`); the live `ai` field
  and ACKs cannot advance it; unknown/pruned IDs map to 0 rather than skipping.
- Lifecycle is unchanged: `stop()` bumps the generation and closes the socket; the handshake read is
  bounded; no SQLite call is made directly on the Bluetooth thread.

## Untested / limits

No `dotnet`, `gradle`, test assembly, analyzer, APK, phone, PC, reconnect or SQLite instrumentation ran
here; crashes, rollback and downgrade are argued from code. Files outside the snapshot (`WireState`,
`AgentStream`/`AgentLink`, `Program.cs`) were not re-reviewed and are assumed unchanged. OpenCode's
app-control (`0x800711C7`) explanation and its 83-check count could not be verified.
