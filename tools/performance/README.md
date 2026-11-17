# Performance measurement helpers

These helpers are generic, caller-driven tools for measuring the Desk Companion preview.
They do not contain captured data, device serials, credentials or private paths, and they
do not define pass/fail thresholds.

Trace Processor is an external official measurement tool. It is not bundled or downloaded
by this repository. Provide the executable with `--tp <path>` or the `TRACE_PROCESSOR`
environment variable.

## Prerequisites

- Node.js 18 or newer (for `fetch`, `AbortSignal.timeout` and `Array.prototype.at`).
- An external `trace_processor_shell` build from the official Perfetto project.
- A capture directory containing whichever optional CSVs your capture tooling produced:
  `pipeline.csv`, `frames.csv`, `thermal.csv`, `start.json`, `summary.json`.
- A Perfetto trace captured with `capture-fps-70s.pbtxt` or `capture-fps-30min.pbtxt`,
  or an equivalent config that records `android.surfaceflinger.frametimeline`.

The capture configs use the legacy companion package id in `atrace_apps` for
compatibility. Adjust that filter if you measure a differently branded build.

## Analyze a trace

```powershell
node tools/performance/analyze-trace.mjs `
  --capture-dir <capture-directory> `
  --trace <trace-file> `
  --tp <trace-processor-shell> `
  --windows <observe-windows-output.jsonl> `
  --out <analysis.json>
```

`--windows` is optional. `--tp` may be omitted when `TRACE_PROCESSOR` is set. Package
selection defaults to the legacy companion id and can be overridden with `--package`.

### Reported semantics

- FPS comes from app FrameTimeline slices joined to SurfaceFlinger display tokens.
- Physical network-update and age measurements join WebView visual-ready markers to the
  first app frame beginning after each marker and then to its displayed SurfaceFlinger
  frame. Multiple updates in the same display frame count once.
- Pipeline de-duplication keys include `receivedAtBootMs` to avoid ambiguous duplicate
  receives.
- CPU and memory first-display ages use only new valid samples.
- Windows send counters use process-lifetime totals when available. If an older agent
  exposes only per-connection counters and a reset was observed, the result is `null`;
  the analyzer does not invent a number.
- An FPS-only trace marks `pipeline` as `measured: false` with a reason. It is never
  reported as zero application updates.
- Missing `frames.csv` (FrameMetrics) is reported as not measured, not zero callbacks.
- Optional query failures are listed in `traceQueryErrors` rather than silently treated
  as zero measurements.

## Observe the Windows process and local API

```powershell
node tools/performance/observe-windows.mjs `
  --token-file <admin-token-file> `
  --out <observations.jsonl> `
  --seconds 70
```

The token is read once and never written to the output. The output file is created
exclusively and is not overwritten.

```powershell
powershell -File tools/performance/observe-cpu.ps1 `
  -AgentExe <DeskCompanionAgent.exe> `
  -OutDir <observation-directory> `
  -TokenFile <admin-token-file> `
  -ApkPath <DeskCompanion.apk> `
  -Tag <label> `
  -Seconds 1805
```

The CPU wrapper records process CPU/memory start and end snapshots plus the Windows JSONL
observations. It only observes; it does not pass or fail a run, restart processes or
change system settings.

## Notes

- Run the parent's secret/identity scan before publishing any measurement output.
- Keep raw traces, databases and observation logs out of the public package.
- These tools report what was observed. They do not certify test acceptance.
