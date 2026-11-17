#!/usr/bin/env node
// Generic Windows-side observer for the Desk Companion local API.
// Caller provides the token file, output file and endpoint; no private paths are assumed.
import fs from 'node:fs';
import process from 'node:process';

const USAGE = `Usage:
  node observe-windows.mjs --token-file <file> --out <file> [options]

Options:
  --seconds <n>        Observation duration in seconds (default: 70)
  --interval-ms <n>    Target sample interval in milliseconds (default: 100)
  --base-url <url>     Local API base URL (default: http://127.0.0.1:18765)
  --timeout-ms <n>     Per-request timeout in milliseconds (default: 2500)
  --help               Show this help

The token is read once and is never written to the observation file.
The output file is created exclusively; an existing file is not overwritten.`;

function fail(message) {
  console.error(message);
  process.exit(2);
}

function parseArgs(argv) {
  const opts = {
    seconds: 70,
    intervalMs: 100,
    baseUrl: 'http://127.0.0.1:18765',
    timeoutMs: 2500,
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const next = () => {
      const value = argv[++i];
      if (value === undefined) throw Error(`Missing value for ${arg}`);
      return value;
    };
    if (arg === '--token-file') opts.tokenFile = next();
    else if (arg === '--out') opts.out = next();
    else if (arg === '--seconds') opts.seconds = Number(next());
    else if (arg === '--interval-ms') opts.intervalMs = Number(next());
    else if (arg === '--base-url') opts.baseUrl = next();
    else if (arg === '--timeout-ms') opts.timeoutMs = Number(next());
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
if (!options.tokenFile) fail(`--token-file is required.\n\n${USAGE}`);
if (!options.out) fail(`--out is required.\n\n${USAGE}`);
if (!Number.isFinite(options.seconds) || options.seconds <= 0) fail('--seconds must be a positive number.');
if (!Number.isFinite(options.intervalMs) || options.intervalMs < 1) fail('--interval-ms must be at least 1.');
if (!Number.isFinite(options.timeoutMs) || options.timeoutMs < 1) fail('--timeout-ms must be at least 1.');
if (!/^https?:\/\//i.test(options.baseUrl)) fail('--base-url must start with http:// or https://.');

let token;
try {
  token = fs.readFileSync(options.tokenFile, 'utf8').trim();
} catch (error) {
  fail(`Cannot read token file: ${error.message}`);
}
if (!token) fail('Token file is empty.');
const headers = {Authorization: 'Bearer ' + token};
const baseUrl = options.baseUrl.replace(/\/+$/, '');
if (fs.existsSync(options.out)) fail(`Output file already exists: ${options.out}`);
const file = fs.createWriteStream(options.out, {flags: 'wx'});
file.on('error', (error) => fail(`Cannot write output file: ${error.message}`));
let lastSequence = -1;
const until = performance.now() + options.seconds * 1000;

try {
  while (performance.now() < until) {
    const begin = performance.now();
    try {
      const paths = ['/api/state', '/api/bluetooth/status'];
      const [state, bluetooth] = await Promise.all(paths.map(async (pathName) => {
        const response = await fetch(baseUrl + pathName, {headers, signal: AbortSignal.timeout(options.timeoutMs)});
        if (!response.ok) throw Error('HTTP ' + response.status);
        return response.json();
      }));
      const metrics = state.metrics;
      file.write(JSON.stringify({
        atUnixMs: Date.now(),
        observerMonoMs: performance.now(),
        requestMs: performance.now() - begin,
        session: state.agentSessionId,
        sequence: metrics.sequence,
        sampledAtUnixMs: metrics.sampledAtUnixMs,
        monotonicMs: metrics.monotonicMs,
        cpu: metrics.cpu,
        memory: metrics.memory,
        network: metrics.interfaces.filter((item) => item.physical).map((item) => ({
          kind: item.kind,
          sequence: item.sequence,
          sampledAtUnixMs: item.sampledAtUnixMs,
          intervalMs: item.intervalMs,
          rxBytes: item.rxBytes,
          txBytes: item.txBytes,
          connected: item.connected,
          valid: item.valid,
          rawDownBps: item.rawDownBps,
          rawUpBps: item.rawUpBps,
        })),
        bluetooth: {
          connected: bluetooth.connected,
          mode: bluetooth.flowMode,
          sent: bluetooth.sentFrames,
          bytes: bluetooth.sentBytes,
          totalSent: bluetooth.totalSentFrames,
          totalBytes: bluetooth.totalSentBytes,
          connections: bluetooth.connections,
          ack: bluetooth.acknowledgedSequence,
          rttMs: bluetooth.ackRoundTripMs,
          inFlight: bluetooth.inFlight,
          peak: bluetooth.peakInFlight,
        },
      }) + '\n');
      lastSequence = metrics.sequence;
    } catch (error) {
      file.write(JSON.stringify({atUnixMs: Date.now(), error: String(error)}) + '\n');
    }
    await new Promise((resolve) => setTimeout(resolve, Math.max(1, options.intervalMs - (performance.now() - begin))));
  }
  await new Promise((resolve) => file.end(resolve));
  console.log(JSON.stringify({out: options.out, lastSequence}));
} catch (error) {
  file.end();
  fail(String(error));
}
