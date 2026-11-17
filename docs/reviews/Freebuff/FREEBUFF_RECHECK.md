# Recheck addendum — corrections accepted where the source supports them

Original `FREEBUFF_FINAL_REVIEW.md` and `FREEBUFF_MEASUREMENT_REVIEW.md` are unchanged. This addendum
supersedes two of their claims. Nothing here was executed: no build, test, analyzer or device run.

## 1. Missing middle frame — correction confirmed, P1 mechanism withdrawn

- `agent/Max3.Agent/BluetoothCrypto.cs` (`BluetoothSecureChannel.Read`) requires
  `counter==received+1` and otherwise throws `"Bluetooth replay/sequence mismatch"`;
  `history-resume/.../BluetoothLink.kt:172-176` does the same on the phone (`Replay/sequence mismatch`).
  A frame skipped in the middle therefore cannot be followed by a later application frame: the next
  `Read` either sees a non-successor counter, or the 4-byte length/frame boundary is already wrong and
  the read throws. RFCOMM is an ordered reliable stream, so byte loss that silently leaves the stream
  usable does not exist.
- `CompanionActivity.kt:138-141` enqueues `store.accept` for **every** state whose `eventHistory` is
  non-empty; the UI coalescing path (`pendingState`/`stateInFlight`) does not gate persistence.
- The cumulative window ACK (`LatestFrameWindow.Acknowledge`) releases credit, but a frame is only ever
  admitted to the window after a completed `Write`, and the phone only advances its receive counter
  after a successful decrypt. A dropped frame means the channel faults; the next connection starts from
  the last **committed** checkpoint, so events replay rather than disappear.

Conclusion: the "one dropped frame silently skips an event" mechanism is wrong. Retracted. My original
report should not be read as claiming that.

## 2. Storage failure — a real loss path exists in this snapshot

The surviving mechanism, verified here, is in the persistence layer, not the wire:

- `CompanionActivity.kt:140`: `io.execute{if(!destroyed)runCatching{store.accept(state)}}` — a failed
  batch is swallowed after `Log`/runCatching, and the single-threaded `io` executor continues with the
  batches queued behind it.
- `CompanionStore.kt:57-71`: the failed batch's transaction rolls back (no `setTransactionSuccessful`),
  but the next batch's `accept` writes **its** last event ID as the checkpoint. The rolled-back events
  are now behind the checkpoint and are skipped forever.

So P1 becomes: "a swallowed SQLite failure in one batch lets a later batch commit a checkpoint past the
lost events". That requires a real DB error (disk full, corruption, closed DB), not RFCOMM loss.

The proposed fix (authenticated monotonic connection epoch + single-writer reject-through fence, close
only the owned connection, next handshake reads last committed checkpoint) is not in this snapshot, so
it is reviewed as a design, not as code. It addresses the mechanism above if: (a) the epoch is bound
into the authenticated context, not client-supplied; (b) a failure rejects all pending batches with
`<=failedEpoch` before any later batch can commit; (c) only the socket that owned that epoch is closed;
(d) the handshake ignores in-memory cursors and reads the committed checkpoint. Stale-failure/newer-
connection and busy-DB rejection both need tests; no fault injection is claimed.

## 3. Capture duration — correction confirmed

1800 s ≤ 3600 s (`CompanionPerformance.kt:88`), so the 30-minute capture is reachable; my earlier
statement that both required captures were impossible was wrong. The ≥24 h run uses
`Measure-CompanionStability.ps1`, a different instrument: it polls loopback `/api/state`,
`/api/bluetooth/status`, phone receive/DOM counters, SQLite integrity every 30 min, battery, hourly
histograms, and native process CPU/RSS, and sets `validContinuousRun` only at ≥24 h with zero errors,
stale samples, gaps, restarts and counter resets. It explicitly cannot certify physical FPS or latency.
The 7.6 s preflight is not acceptance evidence, and I do not infer a 24 h result from it.

## 4. physicalNetworkHz — correction confirmed

`analyze-real-measurement.mjs` builds one entry per SurfaceFlinger display token (`presentMap.set(f.token,...)`)
and rates it over `presentNs`: it counts unique new network **states attached to a physical presentation**,
not NIC sampling. `BluetoothTelemetry.cs:6-8` confirms every packet carries a new native network sample,
with only unchanged CPU/RAM/disk/WLAN/task records omitted. My "9.66 Hz means NIC sampling is slower"
reading was therefore wrong. The value is still below the nominal 10 Hz.

## 5. Trace diagnostic — correction confirmed

`config_write_into_file_no_flush` describes trace-processor memory growth without periodic flush; it is
not evidence of lost markers. The 30-minute trace reports `traceHealth: []` and
`unmatchedVisualMarkers: 0`; the short capture reported 4 edge-unmatched visual markers explicitly. My
inference that `DC_*` joins were unconfirmed because of this stat is withdrawn. Marker coverage remains
a per-capture measured quantity, and the short capture's 4 unmatched markers stand as a real gap.

## 6. Jank breakdown — correction confirmed

Supplied breakdown: 2119 Buffer Stuffing, 18 App Deadline Missed, 1 App Deadline Missed+Buffer Stuffing,
2 Display HAL+App Deadline Missed, 2027 None. Total 4167; deadline-affected 21 (0.50 %); jank total 2140
(51.35 %, consistent with the reported 51.36 %). Equating all 51 % with deadline misses was wrong; it is
overwhelmingly buffer stuffing. A 60.000004 Hz panel (parent-provided; not a field in the JSON) makes
the analyzer's 60 Hz normalization appropriate for this device. `missedRefresh` and 1 % low remain
separate measured quantities.

## 7. Preliminary 30-minute data — inspected, not acceptance

`REAL_THIRTY_MINUTES.json` shows a real reconnect (`sentCounterReset: true`, `disconnectedChecks: 21`)
and is **not all-passing**: physical age point P95 291.13 ms (pass) but upper P95 307.29 ms (>300),
point P99 461.74 / upper 480.86 ms (pass ≤500); `averageFps` 59.40; `receiveHz` 9.66, `physicalNetworkHz`
9.46; 1 % low 30.28 fps; jank 41.09 % with 0 % dropped buffers; peak outstanding 3. `REAL_THIRTY_CPU.json`
reports 0.457 % average CPU over 1805 s (includes observer request work). These are preliminary and
pre-latest-performance-change; I do not present them as acceptance.

Test wiring is corrected: `history-resume/agent/Max3.Tests/Program.cs` calls `HistoryResumeTests.Run`
and `Max3.Tests.csproj` exists, so the "no caller" P2 is withdrawn. The tests still have no executed
result in this session; the OpenCode execution block remains an honest limitation.

## Residual issues unchanged

Downgrade-by-hello-stripping (bandwidth only), the ≤1 s checkpoint read falling back to replay under a
busy DB, the ~1 event/frame drain rate, and the absence of any durable marker of the last acknowledged
batch on the PC side. The new epoch fence addresses the loss path but the PC still advances its
in-memory cursor on write, so correctness depends on the phone refusing forward commits.
