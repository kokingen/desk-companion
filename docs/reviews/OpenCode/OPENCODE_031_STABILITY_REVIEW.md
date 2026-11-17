# Desk Companion 0.3.1 stability observer review

Author: OpenCode (test-tools sub-agent)
Date: 2026-11-17
Scope: `tools/Measure-CompanionStability.ps1` only, plus one authorized short real preflight. Production C#/Android/UI, the installed APK/EXE, and other agents' docs/packaging were not modified.

## Script changes

`tools/Measure-CompanionStability.ps1`

- Reads the final `BluetoothStatus` lifetime fields `totalSentFrames`, `totalSentBytes`, and `connections` and stores them on every row.
- Computes interval send rate from the lifetime counters (`sendHz`, `sendBytesPerSecond`), not the per-connection `sentFrames`/`sentBytes`.
- Detects a new connection when `connections` increases even if the reset connection's per-connection `sentFrames` already exceeds the old value. A sent-frame decrease is also accepted as a change signal, but the row gets one `bluetoothConnectionChange` increment with reason text; it cannot also be counted as a separate reset.
- Skips send-rate/connection comparison when the agent session changed, so a process-lifetime counter reset is not misreported as a normal connection change.
- Tracks agent session restarts and agent PID restarts separately; both are invalidating criteria. CPU accumulation is not carried across a PID change.
- Adds delta `persistenceFailures` to progress and initial/final/delta values to summary, and makes any non-zero delta invalid.
- Attempts a fresh final phone status, final history, final histogram, final native state/Bluetooth status, and final Agent CPU at the end. Failures are listed in `finalizationErrors`; a failed final phone keeps the last observed phone status but marks `finalPhoneFresh=false`.
- Separates completed observation from a valid continuous run:
  - `observationCompleted` = stopwatch elapsed reached requested hours.
  - `validContinuousRun` = every entry in `criteria` is true.
  - `criteria` and `thresholds` are written to `summary.json`; progress also reports both flags and required thresholds.
- Exact invalidation thresholds used: requested hours >= 24, phone monotonic counter span >= 24 h, observation gap > max(90, 3 x interval) seconds, native sample age outside +/-1000 ms, Bluetooth phone-message age > 3000 ms, no observation/phone/gap errors, no stale native/phone counters, no Bluetooth outage, no session/PID/phone restarts, stable `persistenceFailures`, no connection changes, no lifetime-counter resets, history integrity `ok`, final phone and CPU recorded.
- Phone observation is a separate `try` block from native/Bluetooth observation. An ADB/phone failure increments `phoneObservationFailures` and keeps already-captured native/Bluetooth evidence on the row; it does not increment `bluetoothAbsentChecks`, which is driven only by native `connected`/phone-message-age evidence.
- Gaps and frequencies use `[Diagnostics.Stopwatch]` elapsed time. The phone observation span uses the phone's monotonic `elapsedMs`.
- Preserved the scoped `PowerCreateRequest`/`PowerSetRequest`/`PowerClearRequest`/`CloseHandle` sequence and the `finally` cleanup. Moved request creation after `start.json` so a start-record failure cannot leave a held request. No global power plan/config edits.

One environment fix: `Get-FileHash` is unavailable in this shell (`Microsoft.PowerShell.Utility` lacks it here), so the start record now hashes the EXE with inline .NET SHA-256.

## Actual preflight record

Command executed after `PARSE OK`:

```powershell
& tools\Measure-CompanionStability.ps1 `
  -InstallRoot <workspace>\outputs\DeskCompanion-installed `
  -AdbPath <workspace>\work\tools\android-sdk\platform-tools\adb.exe `
  -Hours 0.002 -IntervalSeconds 10 -KeepSystemAwake
```

- First invocation aborted before sampling because of the missing `Get-FileHash`; its log directory `release-stability-20261117-135825` contains no observations. This was fixed as above.
- Second invocation completed: `release-stability-20261117-135925`, 1 checkpoint, 0 errors, 0 phone-observation failures, `observationCompleted=true`, `validContinuousRun=false`.
- Observed: actual 0.002184 h, phone counter span 0.001888 h, final phone `persistenceFailures` 0 (delta 0), final history integrity `ok`, final CPU pid 8968 at 9843.75 ms, average Agent CPU 0.84%.
- The row recorded `totalSentFrames=1376`, `totalSentBytes=761442`, `connections=1`; final Bluetooth status recorded `totalSentFrames=1419`, `totalSentBytes=788954`, `connections=2`. The link was down at the single checkpoint (`bluetoothAbsentChecks=1`, mode `stop-wait-v1`) and authenticated at finalization (`latest-window-v2`), which is a real observed reconnect.
- `validContinuousRun=false` is correct and expected: requested < 24 h, phone span < 24 h, and a real Bluetooth-absent checkpoint.
- The 10 s interval produced only one checkpoint for 7.2 s, so the live interval send-rate and connection-change branches were not exercised by this preflight. Their logic was reviewed statically against the observed counter shapes.

## Residual limitations

- No 24 h / 24.01 h acceptance run was performed; the preflight is not a stability acceptance result.
- Because only one checkpoint existed, interval `sendHz`/`sendBytesPerSecond` and the `connections` change detector were not executed live. No controlled reconnect was induced (radios may not be toggled).
- `connections` and lifetime send counters are process-lifetime values. An Agent restart resets them; the script invalidates on session/PID change and separately reports lifetime-counter resets.
- ADB/phone diagnostics require the foreground receiver path. Failures are observation failures, not proof of a Bluetooth outage, and are reported separately.
- History integrity is checked by the existing phone diagnostic; no database was extracted or cleared.
- The script writes measurement logs under `InstallRoot\logs` by design; no app files, settings, histories, or installed binaries were altered.
