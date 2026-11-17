#!/usr/bin/env node
// Standalone acceptance checker for analyze-real-measurement.mjs output (Node built-ins only).
// Usage: node FREEBUFF_ACCEPTANCE_CHECKS.mjs <analysis.json> [stability-summary.json]
//
// Reads only the supplied file(s). It never re-derives metrics, never fills gaps, never infers a
// 24 h result from a shorter run. Rates are compared to their exact nominal target with the exact
// deviation shown; no tolerance is invented. Statuses: PASS, FAIL, NOT_TESTED, REVIEW.
// Exit: 0 = no FAIL, 1 = at least one FAIL, 2 = usage/parse error.

import fs from 'node:fs';

const [analysisPath, stabilityPath] = process.argv.slice(2);
if (!analysisPath) {
  console.error('usage: node FREEBUFF_ACCEPTANCE_CHECKS.mjs <analysis.json> [stability-summary.json]');
  process.exit(2);
}

let a;
try {
  a = JSON.parse(fs.readFileSync(analysisPath, 'utf8'));
} catch (e) {
  console.error(`cannot read analysis JSON ${analysisPath}: ${e.message}`);
  process.exit(2);
}

const results = [];
const num = (v) => (typeof v === 'number' && Number.isFinite(v) ? v : null);
const fmt = (v, d = 3) => (v === null ? 'n/a' : Number(v).toFixed(d));
const get = (obj, path) => path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), obj);
const add = (name, status, detail) => results.push({ name, status, detail });

function threshold(name, actual, cmp, bound, unit) {
  if (actual === null) return add(name, 'NOT_TESTED', `value absent (need ${bound}${unit})`);
  const ok = cmp === '<=' ? actual <= bound : actual >= bound;
  add(name, ok ? 'PASS' : 'FAIL', `actual ${fmt(actual)}${unit} ${cmp} ${bound}${unit}`);
}

function rate(name, actual, nominal) {
  if (actual === null) return add(name, 'NOT_TESTED', `rate absent (nominal ${nominal} Hz)`);
  const dev = actual - nominal;
  add(
    name,
    actual >= nominal ? 'PASS' : 'FAIL',
    `actual ${fmt(actual)} Hz, target ${nominal} Hz, deviation ${dev >= 0 ? '+' : ''}${fmt(dev)} Hz (${dev >= 0 ? '+' : ''}${fmt((dev / nominal) * 100, 2)}%)`
  );
}

// ---- pipeline presence and duration -------------------------------------------------------------
const p = a.pipeline ?? {};
const f = a.fps ?? {};
const w = a.windows ?? {};

add('analysis.scope', typeof a.scope === 'string' ? 'PASS' : 'NOT_TESTED', a.scope ?? 'scope string absent');

const observationMs = num(w.observationMs);
if (observationMs === null) add('duration.30min', 'NOT_TESTED', 'windows.observationMs absent');
else {
  add(
    'duration.30min',
    observationMs >= 1_800_000 ? 'PASS' : 'NOT_TESTED',
    `${fmt(observationMs / 1000, 1)} s observed (1800 s required); ${observationMs >= 1_800_000 ? 'meets' : 'does not meet'} the 30-minute minimum`
  );
}

// ---- counter uniqueness -------------------------------------------------------------------------
const rxTotal = num(p.receiveStates);
const rxUnique = num(p.uniqueReceiveStates);
if (rxTotal === null || rxUnique === null) add('pipeline.receiveUniqueness', 'NOT_TESTED', 'receiveStates/uniqueReceiveStates absent');
else {
  const dupes = rxTotal - rxUnique;
  add(
    'pipeline.receiveUniqueness',
    dupes === 0 ? 'PASS' : 'REVIEW',
    `${rxUnique} unique of ${rxTotal} receive rows (${dupes} duplicate session:sequence key${dupes === 1 ? '' : 's'}); ` +
      (dupes === 0 ? 'all rows distinct' : 'duplicates may be legitimate sequence reuse across reconnects and must be resolved by inspection')
  );
}
const dom = num(p.domUpdates);
const visual = num(p.visualReady);
if (dom === null || visual === null) add('pipeline.domCoverage', 'NOT_TESTED', 'domUpdates/visualReady absent');
else {
  const gap = visual - dom;
  add(
    'pipeline.domCoverage',
    gap <= 0 ? 'PASS' : 'REVIEW',
    `visualReady ${visual}, domUpdates ${dom}, difference ${gap}` + (gap > 0 ? ' (visual callbacks without a counted DOM update)' : '')
  );
}
const unmatched = num(p.unmatchedVisualMarkers);
if (unmatched === null) add('trace.unmatchedVisualMarkers', 'NOT_TESTED', 'field absent');
else add('trace.unmatchedVisualMarkers', unmatched === 0 ? 'PASS' : 'REVIEW', `${unmatched} visual marker(s) without a matched presented frame`);

const th = Array.isArray(a.traceHealth) ? a.traceHealth : null;
if (th === null) add('trace.traceHealth', 'NOT_TESTED', 'traceHealth absent');
else add('trace.traceHealth', th.length === 0 ? 'PASS' : 'REVIEW', th.length === 0 ? 'no error/data_loss/drop stats' : `${th.length} entry(ies): ${th.map((x) => x.name ?? JSON.stringify(x)).join(', ')}`);

// ---- bounded send window ------------------------------------------------------------------------
const peak = num(w.peakOutstanding);
threshold('window.peakOutstanding', peak, '<=', 3, ''); // the negotiated v2 bound is maxInFlight = 3

// ---- rates vs exact nominal 10 Hz (and 4 Hz / 2 Hz slow samplers) -------------------------------
rate('rate.receive', num(p.receiveHz), 10);
rate('rate.physicalNetwork', num(p.physicalNetworkHz), 10);
rate('rate.nativeNetwork', num(w.nativeNetworkHz), 10);
rate('rate.actualSent', num(w.actualSentHz), 10);
rate('rate.nativeCpu', num(w.nativeCpuHz), 4);
rate('rate.nativeMemory', num(w.nativeMemoryHz), 2);

// ---- latency budgets ----------------------------------------------------------------------------
const p95 = num(get(p, 'physicalAgeMs.p95'));
const p99 = num(get(p, 'physicalAgeMs.p99'));
const u95 = num(get(p, 'physicalAgeUpperMs.p95'));
const u99 = num(get(p, 'physicalAgeUpperMs.p99'));
const ageN = num(get(p, 'physicalAgeMs.n'));
threshold('latency.p95.point', p95, '<=', 300, ' ms');
threshold('latency.p99.point', p99, '<=', 500, ' ms');
threshold('latency.p95.uncertaintyUpper', u95, '<=', 300, ' ms');
threshold('latency.p99.uncertaintyUpper', u99, '<=', 500, ' ms');
if (ageN === null || ageN === 0) add('latency.samples', 'NOT_TESTED', 'no presented physical samples');
else add('latency.samples', 'PASS', `${ageN} presented physical-age samples`);

// ---- frame rate against the reported panel rate -------------------------------------------------
const reportedHz = num(a.displayRefreshHz) ?? num(a.panelHz) ?? num(a.refreshHz) ?? num(get(a, 'methods.panelHz'));
const targetHz = reportedHz ?? 60;
const avgFps = num(f.averageFps);
if (avgFps === null) add('fps.averageVsRefresh', 'NOT_TESTED', 'fps.averageFps absent');
else {
  const assumed = reportedHz === null;
  const ok = avgFps >= targetHz;
  add(
    'fps.averageVsRefresh',
    ok && !assumed ? 'PASS' : ok ? 'REVIEW' : 'FAIL',
    `mean ${fmt(avgFps)} fps vs ${fmt(targetHz)} Hz ${assumed ? '(assumed 60 Hz; analysis reports no panel rate)' : '(reported panel rate)'}, deviation ${fmt(avgFps - targetHz)} fps`
  );
}
const missed = num(f.missedRefreshPercent);
if (missed !== null) add('fps.missedRefresh', 'REVIEW', `${fmt(missed)}% of 60 Hz opportunities missed (informational; no target specified)`);
if (num(f.droppedBufferPercent) !== null) add('fps.droppedBuffers', 'REVIEW', `${fmt(f.droppedBufferPercent)}% dropped buffers (informational)`);
if (num(f.onePercentLowFps) !== null) add('fps.onePercentLow', 'REVIEW', `${fmt(f.onePercentLowFps)} fps 1% low (informational; no target specified)`);
if (num(f.jankPercent) !== null) add('fps.jank', 'REVIEW', `${fmt(f.jankPercent)}% jank by FrameTimeline jank_type (not equal to deadline misses)`);

// ---- reconnect context --------------------------------------------------------------------------
if (num(w.disconnectedChecks) !== null)
  add('link.disconnectedChecks', 'REVIEW', `${w.disconnectedChecks} sampled check(s) with no Bluetooth connection; ${w.sentCounterReset === true ? 'send counter reset observed' : 'no send counter reset observed'}`);

// ---- optional 24 h stability summary (separate instrument, counters only) ----------------------
if (stabilityPath) {
  let s;
  try {
    s = JSON.parse(fs.readFileSync(stabilityPath, 'utf8'));
  } catch (e) {
    add('duration.24h', 'NOT_TESTED', `cannot read stability summary: ${e.message}`);
    s = null;
  }
  if (s) {
    const hours = num(s.actualHours);
    const complete = s.complete === true;
    const valid = s.validContinuousRun === true;
    if (hours === null) add('duration.24h', 'NOT_TESTED', 'summary has no actualHours');
    else
      add(
        'duration.24h',
        valid && complete && hours >= 24 ? 'PASS' : 'FAIL',
        `${fmt(hours, 2)} h (${complete ? 'complete' : 'incomplete'}), validContinuousRun=${valid}; counters/integrity/CPU only, physical FPS not certified`
      );
  }
} else {
  add('duration.24h', 'NOT_TESTED', 'no stability summary supplied; 24 h cannot be inferred from this analysis');
}

// ---- report -------------------------------------------------------------------------------------
const order = ['PASS', 'FAIL', 'REVIEW', 'NOT_TESTED'];
const counts = Object.fromEntries(order.map((k) => [k, results.filter((r) => r.status === k).length]));
const width = Math.max(...results.map((r) => r.name.length));
console.log(`FREEBUFF acceptance checks — ${analysisPath}\n`);
for (const r of results) console.log(`${r.status.padEnd(10)} ${r.name.padEnd(width)}  ${r.detail}`);
console.log(`\nsummary: ${order.map((k) => `${k}=${counts[k]}`).join(' ')}`);
console.log('note: PASS/FAIL are exact comparisons to the stated targets; REVIEW means "not a pass, needs human reading".');
console.log('\nJSON:\n' + JSON.stringify({ analysisPath, stabilityPath: stabilityPath ?? null, counts, results }, null, 2));

process.exit(counts.FAIL > 0 ? 1 : 0);
