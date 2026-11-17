# Independent review — supplied short measurement (REAL_SHORT_TEST.json)

Provenance: this is `analyze-real-measurement.mjs` **output** supplied with the review request. I did
not run the analyzer, recompute hashes, or re-join the raw FrameTimeline/SurfaceFlinger/CSV files (no
shell in this session). The analyzer's joins and its `fps()`, `missedRefresh` and jank definitions are
taken as supplied. Per the request, the capture predates the last UI optimization, so nothing below is
evidence about the current 0.3.1 APK.

## What the capture says (71.9 s Windows observation)

| Metric | Value | Reading |
|---|---|---|
| receiveHz | 9.92 | ~0.8 % below the 10 Hz target; not a 10.0 Hz measurement |
| physicalNetworkHz | 9.66 | NIC samples arrive slower than state frames (delta encoding) |
| physicalAgeMs P95 / P99 | 295.13 / 420.40 | P95 point estimate passes 300 ms by 4.87 ms |
| upper P95 / P99 (age + clock error) | 300.63 / 427.08 | P95 **fails** once the 12.5 ms uncertainty is included |
| clock uncertainty P95 | 12.5 ms | small relative to the margin, but larger than the P95 margin itself |
| FPS avg / 1 % low | 59.54 / 34.36 | near-60 average with occasional ~29 ms frames |
| missedRefresh / droppedBuffer | 0.74 % / 0 % | no buffer drops |
| jank | 51.36 % | frequent deadline misses, inconsistent-looking against 0.74 % missed refresh |
| peakOutstanding | 3 | matches negotiated `maxInFlight=3` |
| meanWireBytesPerState | 525.3 | includes route frames, not telemetry-only size |
| disconnectedChecks | 0 | no link drop in this window |
| battery temperature | 33.0 °C, 14 samples | flat; too short/insensitive for thermal inference |

## Honest interpretation

- **Latency is borderline, not a pass.** The target is P95 ≤ 300 ms. The plain P95 is 295.1 ms, and the
  uncertainty-inclusive P95 is 300.63 ms. With a single 72 s run and no confidence interval, the correct
  statement is "within ~5 ms of the target, upper bound over it, acceptance undetermined". Do not quote
  295 ms as a pass; do not quote 300.6 ms as a failure of the design — it is a failure of the evidence.
- **P99 passes either way** (420.4 / 427.1 ms vs 500 ms), so the tail budget is not what is at risk.
- **Rates:** 9.92 Hz receive and 9.97 Hz native-network window are close to 10 Hz but under it; CPU at
  4 Hz and RAM at 2 Hz are expected because state frames delta-encode slow fields. A 10 Hz state rate
  therefore does not mean 10 Hz CPU/RAM freshness; the UI's per-field age budgets (1 s CPU, 1.5 s RAM)
  are the right test, and this capture does not report them per field.
- **FPS/jank need care.** `missedRefresh` is normalized against an assumed 60 Hz, not the capture's
  reported refresh rate, so it understates misses on a higher-rate panel. A 51 % jank figure beside
  0.74 % missed refresh most likely means many frames exceed the deadline slightly while buffers are
  never dropped; the 34.36 fps 1 % low confirms a handful of long frames. Without the analyzer's raw
  jank rule and the panel rate, this cannot be called a regression or a pass.
- **The 5-minute windows are not a stability test.** `firstFiveMinutes` and `lastFiveMinutes` equal the
  whole run because the capture is shorter than five minutes; any drift, warm-up or stationarity claim
  drawn from those objects is invalid.
- **Thermal evidence is absent.** One flat 33.0 °C battery series over 72 s says nothing about sustained
  load, and no CPU thermal-zone series is present.
- **Trace health:** `config_write_into_file_no_flush=1` means the atrace config may not have been
  flushed, so `DC_*` per-frame markers can be incomplete. Pipeline joins that depend on those markers
  should be treated as unconfirmed even though the CSV rows exist.

## What the analyzer cannot see about this change

`CompanionPerformance.kt:90` records `eventHistoryCount` and `historyLastSequence` only. There is no
resume cursor, checkpoint value, commit result, or reconnect counter in `pipeline.csv`, so this tooling
cannot demonstrate that the new history-resume path works or that a checkpoint was committed. The
diagnostics `start` command also caps captures at 3600 s (`:88`), so the required 30-minute and ≥24-hour
runs cannot be produced by the shipped control.

## Evidence that would settle it

1. Re-run the analyzer on the current APK for ≥30 min and ≥24 h, and report P95/P99 both with and
   without clock uncertainty, plus the panel refresh rate.
2. Report per-field freshness (CPU/RAM/NIC age) rather than only the state rate.
3. Add resume fields (cursor sent, checkpoint after commit, batch first/last sequence, reconnect count)
   to `pipeline.csv`, then run reconnect cycles with a deliberately filled backlog.
4. Inject a single dropped state frame (or kill the phone process mid-batch) and confirm no event is
   skipped — this is the P1 scenario in `FREEBUFF_FINAL_REVIEW.md` and is currently untested.

## Limits

No analyzer execution, no APK install, no device run, no long capture, and no verification of the JSON's
derivation from raw traces. All statements above are conditional on the supplied file being the
analyzer output for the stated build.
