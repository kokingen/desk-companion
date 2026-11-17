#!/usr/bin/env node
// Generic Desk Companion performance analyzer.
// All inputs are caller-provided. Trace Processor is an external official measurement
// tool and is never bundled; use --tp or the TRACE_PROCESSOR environment variable.
import fs from 'node:fs';
import {execFileSync} from 'node:child_process';
import path from 'node:path';
import process from 'node:process';

const USAGE = `Usage:
  node analyze-trace.mjs --capture-dir <dir> --trace <trace> [options]

Required:
  --capture-dir <dir>   Directory containing pipeline.csv/frames.csv/thermal.csv/start.json/summary.json
  --trace <file>        Perfetto trace file to query

Options:
  --windows <file>      Windows observer JSONL (observe-windows.mjs output)
  --tp <file>           trace_processor_shell executable; falls back to TRACE_PROCESSOR
  --out <file>          Output analysis JSON (default: <capture-dir>/analysis.json)
  --package <id>        Android package filter (default: legacy companion id)
  --refresh-hz <hz>     Refresh rate used only for missed-refresh accounting (default: 60)
  --help                Show this help

An FPS-only trace reports pipeline as NOT MEASURED, never as zero application updates.
This tool reports observations only; it does not define pass/fail thresholds.`;

function fail(message) {
  console.error(message);
  process.exit(2);
}

function parseArgs(argv) {
  const opts = {
    packageName: 'net.oxog.phonebox.deskpanel.max3',
    refreshHz: 60,
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const next = () => {
      const value = argv[++i];
      if (value === undefined) throw Error(`Missing value for ${arg}`);
      return value;
    };
    if (arg === '--capture-dir') opts.captureDir = next();
    else if (arg === '--trace') opts.trace = next();
    else if (arg === '--windows') opts.windows = next();
    else if (arg === '--tp') opts.tp = next();
    else if (arg === '--out') opts.out = next();
    else if (arg === '--package') opts.packageName = next();
    else if (arg === '--refresh-hz') opts.refreshHz = Number(next());
    else if (arg === '--help' || arg === '-h') opts.help = true;
    else throw Error(`Unknown argument: ${arg}`);
  }
  return opts;
}

let options;
try {
  options = parseArgs(process.argv.slice(2));
} catch (error) {
  fail(`${error.message}\n\n${USAGE}`);
}
if (options.help) {
  console.log(USAGE);
  process.exit(0);
}
if (!options.captureDir) fail(`--capture-dir is required.\n\n${USAGE}`);
if (!options.trace) fail(`--trace is required.\n\n${USAGE}`);
if (!options.packageName || !/^[A-Za-z0-9_.]+$/.test(options.packageName)) {
  fail('--package must contain only letters, digits, underscore and dot characters.');
}
if (!Number.isFinite(options.refreshHz) || options.refreshHz <= 0) {
  fail('--refresh-hz must be a positive number.');
}

const captureDir = path.resolve(options.captureDir);
const trace = path.resolve(options.trace);
if (!fs.existsSync(captureDir) || !fs.statSync(captureDir).isDirectory()) fail(`Capture directory not found: ${captureDir}`);
if (!fs.existsSync(trace) || !fs.statSync(trace).isFile()) fail(`Trace file not found: ${trace}`);
const tp = options.tp || process.env.TRACE_PROCESSOR;
if (!tp) fail('Trace Processor path is required: pass --tp <file> or set TRACE_PROCESSOR. Trace Processor is not bundled.');
const tpPath = path.resolve(tp);
if (!fs.existsSync(tpPath) || !fs.statSync(tpPath).isFile()) fail(`Trace Processor executable not found: ${tpPath}`);
const outFile = options.out ? path.resolve(options.out) : path.join(captureDir, 'analysis.json');
const windowsFile = options.windows ? path.resolve(options.windows) : null;
if (windowsFile && !fs.existsSync(windowsFile)) fail(`Windows observation file not found: ${windowsFile}`);

const csv = (text) => {
  const lines = text.trim().split(/\r?\n/);
  if (!lines.length || !lines[0]) return [];
  const parse = (x) => [...x.matchAll(/(?:^|,)(?:"((?:[^"]|"")*)"|([^,]*))/g)].map((m) => (m[1] ?? m[2]).replaceAll('""', '"'));
  const keys = parse(lines.shift());
  return lines.filter(Boolean).map((line) => Object.fromEntries(parse(line).map((value, index) => [keys[index], value])));
};
const read = (name) => fs.existsSync(path.join(captureDir, name)) ? csv(fs.readFileSync(path.join(captureDir, name), 'utf8')) : [];
const readJson = (name) => fs.existsSync(path.join(captureDir, name)) ? JSON.parse(fs.readFileSync(path.join(captureDir, name), 'utf8')) : null;
const quant = (values, q) => {
  values = values.filter(Number.isFinite).sort((a, b) => a - b);
  return values.length ? values[Math.ceil(values.length * q) - 1] : null;
};
const stats = (values) => {
  values = values.filter(Number.isFinite);
  return {
    n: values.length,
    mean: values.length ? values.reduce((s, x) => s + x, 0) / values.length : null,
    p50: quant(values, 0.5),
    p95: quant(values, 0.95),
    p99: quant(values, 0.99),
    min: values.length ? values.reduce((s, x) => Math.min(s, x), Infinity) : null,
    max: values.length ? values.reduce((s, x) => Math.max(s, x), -Infinity) : null,
  };
};

const traceQueryErrors = [];
function sanitizeError(message) {
  return String(message)
    .split(trace).join('<trace>')
    .split(captureDir).join('<capture-dir>')
    .split(tpPath).join('<trace-processor>');
}
function query(sql, name) {
  const out = execFileSync(tpPath, ['query', '--no-progress', '--format', 'csv', '--max-rows', '0', '--max-bytes', '0', trace, sql], {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  fs.writeFileSync(path.join(captureDir, name), out);
  return csv(out);
}
function tryQuery(sql, name) {
  try {
    return query(sql, name);
  } catch (error) {
    traceQueryErrors.push({query: name, error: sanitizeError(error.message || error)});
    return [];
  }
}

const packageName = options.packageName;
const frames = tryQuery(`WITH app AS (SELECT a.* FROM actual_frame_timeline_slice a JOIN process p USING(upid) WHERE p.name='${packageName}' AND a.layer_name LIKE '%CompanionActivity%'),sf AS (SELECT a.display_frame_token AS token,MAX(a.ts+a.dur) AS presentNs FROM actual_frame_timeline_slice a JOIN process p USING(upid) WHERE p.name='/system/bin/surfaceflinger' AND a.dur>0 GROUP BY a.display_frame_token) SELECT app.ts AS appStartNs,app.dur AS appDurationNs,app.display_frame_token AS token,sf.presentNs,app.present_type AS presentType,app.jank_type AS jankType FROM app LEFT JOIN sf ON app.display_frame_token=sf.token ORDER BY app.ts`, 'presented-frames.csv');
const markers = tryQuery("SELECT name,ts FROM slice WHERE name GLOB 'DC_*' ORDER BY ts", 'markers.csv');
const traceHealth = tryQuery("SELECT name,severity,value FROM stats WHERE value!=0 AND (severity IN ('error','data_loss') OR name LIKE '%drop%' OR name LIKE '%overrun%')", 'trace-health.csv');

// These optional CSVs come from the caller's capture tooling. Missing files mean the
// associated phase was not instrumented, not that its counters were zero.
const pipeline = read('pipeline.csv');
const rx = pipeline.filter((row) => row.stage === 'receive');
const dom = pipeline.filter((row) => row.stage === 'dom');
const visual = pipeline.filter((row) => row.stage === 'visual');
const numbers = (values) => values.map(Number);
const unique = (rows) => [...new Map(rows.map((row) => [row.session + ':' + row.sequence, row])).values()];
const rate = (rows) => rows.length > 1 ? (rows.length - 1) * 1e9 / (Number(rows.at(-1).bootNs) - Number(rows[0].bootNs)) : null;
const key = (row) => row.session + ':' + row.sequence + ':' + row.receivedAtBootMs;
const rxMap = new Map(rx.map((row) => [key(row), row]));
const markMap = new Map(markers.map((marker) => [marker.name, Number(marker.ts)]));
const offsets = visual.filter((v) => markMap.has('DC_VISUAL_' + v.sequence)).map((v) => markMap.get('DC_VISUAL_' + v.sequence) - Number(v.bootNs));
const offset = quant(offsets, 0.5) ?? 0;
const displayFrames = [...new Map(frames.filter((frame) => frame.presentNs !== '[NULL]' && Number(frame.presentNs) > 0 && frame.presentType !== 'Dropped Frame').map((frame) => [frame.token, frame])).values()].sort((a, b) => Number(a.presentNs) - Number(b.presentNs));
const eligible = frames.filter((frame) => Number(frame.presentNs) > 0 && frame.presentType !== 'Dropped Frame');
function nextFrame(ts) {
  let lo = 0;
  let hi = eligible.length;
  while (lo < hi) {
    const mid = (lo + hi) >>> 1;
    if (Number(eligible[mid].appStartNs) < ts) lo = mid + 1;
    else hi = mid;
  }
  return eligible[lo];
}
const presentMap = new Map();
let unmatched = 0;
for (const v of visual) {
  const receive = rxMap.get(key(v));
  if (!receive || !receive.ageMs || !Number.isFinite(Number(receive.ageMs))) continue;
  const mark = markMap.get('DC_VISUAL_' + v.sequence);
  if (!mark) {
    unmatched++;
    continue;
  }
  const frame = nextFrame(mark);
  if (!frame) {
    unmatched++;
    continue;
  }
  const age = Number(receive.ageMs) + (Number(frame.presentNs) - offset - Number(receive.bootNs)) / 1e6;
  presentMap.set(frame.token, {
    session: receive.session,
    sequence: Number(v.sequence),
    bootNs: Number(frame.presentNs) - offset,
    ageMs: age,
    upperMs: age + Number(receive.clockErrorMs),
    clockErrorMs: Number(receive.clockErrorMs),
    route: receive.route,
    rx: receive,
  });
}
const presented = [...presentMap.values()].sort((a, b) => a.bootNs - b.bootNs);
function fps(rows) {
  const times = rows.map((frame) => Number(frame.presentNs));
  const intervals = times.slice(1).map((time, index) => (time - times[index]) / 1e6).filter((interval) => interval > 0);
  const slow = [...intervals].sort((a, b) => b - a).slice(0, Math.max(1, Math.ceil(intervals.length * 0.01)));
  const refreshMs = 1000 / options.refreshHz;
  const missed = intervals.reduce((sum, interval) => sum + Math.max(0, Math.round(interval / refreshMs) - 1), 0);
  return {
    uniquePresentedFrames: rows.length,
    averageFps: times.length > 1 ? (times.length - 1) * 1e9 / (times.at(-1) - times[0]) : null,
    onePercentLowFps: slow.length ? 1000 / (slow.reduce((sum, interval) => sum + interval, 0) / slow.length) : null,
    missedRefreshPercent: intervals.length ? 100 * missed / (missed + intervals.length) : null,
    intervalMs: stats(intervals),
  };
}
const captureStart = readJson('start.json') ?? readJson('profile-metadata.json');
const captureSummary = readJson('summary.json');
const firstPresent = Number(displayFrames[0]?.presentNs);
const lastPresent = Number(displayFrames.at(-1)?.presentNs);
const battery = read('thermal.csv');
const frameMetrics = read('frames.csv');
function fieldFirstDisplay(field) {
  const seen = new Set();
  const out = [];
  for (const point of presented) {
    const receive = point.rx;
    if (receive[field + 'Valid'] !== 'true') continue;
    const id = receive.session + ':' + receive[field + 'Sequence'] + ':' + receive[field + 'SampledAtUnixMs'];
    if (seen.has(id)) continue;
    seen.add(id);
    const age = point.ageMs + Number(receive.sampledAtUnixMs) - Number(receive[field + 'SampledAtUnixMs']);
    out.push({bootNs: point.bootNs, age, upper: age + point.clockErrorMs});
  }
  return {
    measured: out.length > 0,
    uniqueValidSamples: out.length,
    newSampleDisplayHz: rate(out),
    firstDisplayAgeMs: stats(out.map((value) => value.age)),
    firstDisplayAgeUpperMs: stats(out.map((value) => value.upper)),
  };
}

const result = {
  scope: 'Real non-debuggable release APK, native Windows samples, app + SurfaceFlinger FrameTimeline joined by display token. Visual-ready callbacks alone are not physical presentation.',
  displayRefreshHz: captureStart?.refreshRateHz ?? null,
  captureDurationMs: captureSummary?.captureDurationMs ?? null,
  methods: {
    onePercentLow: '1000 divided by mean of slowest 1% positive inter-present intervals (ms)',
    missedRefresh: `missed ${options.refreshHz} Hz opportunities / (observed intervals + missed opportunities); separate from FrameTimeline dropped buffers`,
    physicalUpdate: 'first app frame beginning after each WebView visual-ready marker, joined to actual SurfaceFlinger display token; multiple updates in same display frame count once',
    age: 'Windows native network sample to conservative displayed frame, calibrated clock estimate; upper includes measured clock uncertainty',
    markerClockOffsetNs: offset,
  },
  pipeline: {
    receiveStates: rx.length,
    uniqueReceiveStates: unique(rx).length,
    receiveHz: rate(unique(rx)),
    domUpdates: unique(dom).length,
    domHz: rate(unique(dom)),
    visualReady: visual.length,
    physicalNetworkUpdates: presented.length,
    physicalNetworkHz: rate(presented),
    unmatchedVisualMarkers: unmatched,
    uncalibratedReceiveStates: rx.filter((row) => !Number.isFinite(Number(row.ageMs))).length,
    routes: [...new Set(rx.map((row) => row.route))],
    receiveAgeMs: stats(numbers(rx.map((row) => row.ageMs))),
    domAgeMs: stats(numbers(dom.map((row) => row.ageMs))),
    physicalAgeMs: stats(presented.map((row) => row.ageMs)),
    physicalAgeUpperMs: stats(presented.map((row) => row.upperMs)),
    clockUncertaintyMs: stats(numbers(rx.map((row) => row.clockErrorMs))),
    lastFiveMinutesPhysicalAgeMs: stats(presented.filter((row) => row.bootNs > presented.at(-1)?.bootNs - 300e9).map((row) => row.ageMs)),
    lastFiveMinutesPhysicalAgeUpperMs: stats(presented.filter((row) => row.bootNs > presented.at(-1)?.bootNs - 300e9).map((row) => row.upperMs)),
  },
  fps: {
    ...fps(displayFrames),
    droppedBufferPercent: frames.length ? 100 * frames.filter((frame) => frame.presentType === 'Dropped Frame').length / frames.length : null,
    jankPercent: frames.length ? 100 * frames.filter((frame) => frame.jankType !== 'None').length / frames.length : null,
    firstFiveMinutes: displayFrames.length ? fps(displayFrames.filter((frame) => Number(frame.presentNs) < firstPresent + 300e9)) : null,
    lastFiveMinutes: displayFrames.length ? fps(displayFrames.filter((frame) => Number(frame.presentNs) > lastPresent - 300e9)) : null,
    frameMetricsCallbacks: frameMetrics.length ? frameMetrics.length : null,
    frameMetricsCallbackDrops: frameMetrics.length ? frameMetrics.reduce((sum, row) => sum + Number(row.callbackDrops), 0) : null,
    measured: displayFrames.length > 0,
  },
  thermal: {
    sensor: 'Android ACTION_BATTERY_CHANGED battery temperature; not SoC temperature',
    batteryCelsius: stats(numbers(battery.map((row) => row.batteryCelsius))),
    first: battery[0],
    last: battery.at(-1),
  },
  traceHealth,
  traceQueryErrors,
};
result.pipeline.perField = {cpu: fieldFirstDisplay('cpu'), memory: fieldFirstDisplay('memory')};
if (!rx.length) {
  result.pipeline = {
    measured: false,
    reason: 'FPS-only phase: pipeline/visual fences were not recorded; this is not zero application updates',
  };
} else {
  result.pipeline.measured = true;
}
if (!displayFrames.length) {
  result.fps.reason = 'No SurfaceFlinger-presented companion frames were found; FPS is not measured, not zero.';
}
if (!frameMetrics.length) {
  result.fps.frameMetricsCallbacks = null;
  result.fps.frameMetricsCallbackDrops = null;
  result.fps.frameMetricsMeasured = false;
} else {
  result.fps.frameMetricsMeasured = true;
}

if (windowsFile) {
  const rows = fs.readFileSync(windowsFile, 'utf8').trim().split(/\r?\n/).map((line) => JSON.parse(line)).filter((row) => !row.error);
  if (!rows.length) {
    result.windows = {measured: false, reason: 'Windows observer file contained no successful observations'};
  } else {
    const first = rows[0];
    const last = rows.at(-1);
    const reset = rows.some((row, index) => index && row.bluetooth.sent < rows[index - 1].bluetooth.sent);
    const lifetime = Number.isFinite(first.bluetooth.totalSent) && Number.isFinite(last.bluetooth.totalSent) && first.session === last.session;
    const sent = lifetime ? last.bluetooth.totalSent - first.bluetooth.totalSent : reset ? null : last.bluetooth.sent - first.bluetooth.sent;
    const bytes = lifetime ? last.bluetooth.totalBytes - first.bluetooth.totalBytes : reset ? null : last.bluetooth.bytes - first.bluetooth.bytes;
    const nativeRate = (selector) => {
      const a = selector(first);
      const b = selector(last);
      return (b.sequence - a.sequence) * 1000 / (b.sampledAtUnixMs - a.sampledAtUnixMs);
    };
    result.windows = {
      measured: true,
      observationMs: last.atUnixMs - first.atUnixMs,
      nativeNetworkHz: nativeRate((row) => row),
      nativeCpuHz: nativeRate((row) => row.cpu),
      nativeMemoryHz: nativeRate((row) => row.memory),
      actualSentHz: sent === null ? null : sent * 1000 / (last.atUnixMs - first.atUnixMs),
      sendCounterSource: lifetime ? 'process-lifetime successful encrypted state writes' : reset ? 'unavailable: per-connection counter reset' : 'per-connection successful encrypted state writes; no observed reset',
      sentCounterReset: reset,
      connectionsDuringWindow: lifetime ? last.bluetooth.connections - first.bluetooth.connections : null,
      disconnectedChecks: rows.filter((row) => !row.bluetooth.connected).length,
      meanWireBytesPerState: sent > 0 ? bytes / sent : null,
      peakOutstanding: Math.max(...rows.map((row) => row.bluetooth.peak)),
    };
  }
}

fs.writeFileSync(outFile, JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2));
