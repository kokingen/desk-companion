# OpenCode 0.3.1 packaging review

Scope of this agent: `tools/package-public.ps1`, new `tools/performance/*`, and this review file.
No production C#/Android/UI code, docs, other agent files, Git history, or release archives were
created or modified.

## Edits made

### `tools/package-public.ps1`

- Default `-Version` is now `0.3.1`; package branding remains Desk Companion 0.3.1 Preview.
  Legacy app ids and internal namespaces are left untouched for compatibility.
- Existing release outputs are refused before any write: runtime/source folders, both
  archives and `SHA256SUMS.txt`. This protects 0.3.0 backups and existing final folders.
  Use a fresh `-Output` for each version.
- Runtime bundle remains the Windows single-file EXE, release APK, flat user scripts,
  `licenses/`, and public docs. Added optional `docs/031-MEASUREMENT.md`,
  `docs/031-DELIVERY.md` and `tools/Measure-CompanionStability.ps1` when present.
  Optional generic `tools/performance/` helpers are included without captured data.
- Source export still uses `git ls-files -co --exclude-standard` and never exports `.git`.
  The existing top-level/legacy/script whitelist is preserved and extended with:
  - `docs/031-MEASUREMENT.md`
  - `docs/031-DELIVERY.md`
  - `tools/performance/`
  - `tools/Measure-CompanionStability.ps1`
- Source regex still includes all of `android/`, `agent/Max3.Agent/`,
  `agent/Max3.Tests/`, `tools/companion-ui/` (generated assets and lockfile included) and
  `licenses/`. New BluetoothTelemetry, LatestFrameWindow, history checkpoint/fence,
  JVM checks and their tests are therefore selected when present.
- Exclusion checks were tightened fail-closed for build/cache output, `private-data`, logs,
  raw traces/DBs, `.opencode-031-input`, staging/artifact folders, token/credential files,
  signing keys, archives, and known measurement CSV/JSON outputs. A selected file that
  matches an exclusion throws instead of being silently packaged.
- Public review records are copied only when explicitly named by `docs/reviews/WHITELIST.txt`
  or the `-ReviewFiles` parameter. The folder is never scanned automatically. Entries are
  normalized, confined to `docs/reviews/`, rejected on unsafe traversal, and must exist.
  The parent remains responsible for anonymization/approval.
- Per-file `MANIFEST.json` is written for both runtime and source trees with relative path,
  byte count and SHA-256. `SHA256SUMS.txt` now covers the runtime EXE, release APK, source
  ZIP and runtime ZIP, not archive hashes only.
- Final output message explicitly says no test acceptance is claimed and a parent
  secret/identity scan is required.

### `tools/performance/*`

Added generic, caller-driven helpers adapted from the allowed benchmark inputs:

- `analyze-trace.mjs`: requires `--capture-dir` and `--trace`; accepts `--windows`,
  `--out`, `--package`, `--refresh-hz`; resolves Trace Processor from `--tp` or
  `TRACE_PROCESSOR`. No bundled/downloaded Trace Processor and no hardcoded private paths.
  Preserves the parent semantics: SurfaceFlinger display-token join, `receivedAtBootMs`
  duplicate-receive key, new valid CPU/RAM first-display age, lifetime send counters,
  and `null` for old per-connection counters after a reset. FPS-only captures mark
  `pipeline` as `measured: false` with a reason instead of reporting zero application
  updates. Missing FrameMetrics is reported as not measured. Query errors are recorded,
  with input paths sanitized.
- `observe-windows.mjs`: caller provides token file, output file, endpoint, duration and
  interval. No private defaults; token is never written to output; output is exclusive.
- `observe-cpu.ps1`: caller provides agent EXE, output directory, token file, optional APK,
  tag, duration and endpoint. It only observes process CPU/memory and local API/BLE
  counters; it does not restart processes, change system settings, or define pass/fail.
- `capture-fps-70s.pbtxt` and `capture-fps-30min.pbtxt`: generic FrameTimeline capture
  configs, no device serial or output path. The legacy package id is retained in the
  atrace filter for compatibility.
- `README.md`: usage, semantics and limitations.

## Static checks performed

- PowerShell parser check passed for `tools/package-public.ps1` and
  `tools/performance/observe-cpu.ps1`.
- `node --check` passed for `tools/performance/analyze-trace.mjs` and
  `tools/performance/observe-windows.mjs`.
- `--help` invocations of both Node helpers exited 0.
- A read-only simulation of the package selection regex confirmed:
  - new BluetoothTelemetry, LatestFrameWindow, history and telemetry test files selected;
  - `tools/performance/*` and `tools/Measure-CompanionStability.ps1` selected;
  - `docs/031-MEASUREMENT.md` and `docs/031-DELIVERY.md` selected;
  - `.opencode-031-input`, private data, logs, DBs and raw capture artifacts not selected;
  - no selected source file hit the fail-closed exclusion pattern.
- Confirmed generated UI validator asset, companion UI lockfile and Gradle wrapper files are
  selected for the source snapshot.

No packaging command was executed. No archives, builds, deployments, device/process actions,
system changes or secret scans were performed by this agent.

## Limitations / parent actions

- Provide `-BuildDir` containing `windows/DeskCompanionAgent.exe` and
  `android/DeskCompanion.apk`; the script fails if either is missing.
- Use a fresh `-Output`; the script refuses to overwrite any existing version folder,
  archive or `SHA256SUMS.txt`.
- Populate `docs/reviews/WHITELIST.txt` (or pass `-ReviewFiles`) only with anonymized,
  approved public records. Use one filename per line, relative either to the repository or
  to `docs/reviews/`; `#` lines are comments. The directory is currently absent, so no
  review records are included yet.
- Run the parent's secret/identity scan on the prepared folders before publishing.
- Trace Processor is external; supply `--tp` or set `TRACE_PROCESSOR`.
- Long-duration and final performance evidence is not produced here. The 24-hour stability
  run and final physical performance evidence remain pending and are not claimed.
- The helper tools report observations only; they do not assert pass/fail or test acceptance.
