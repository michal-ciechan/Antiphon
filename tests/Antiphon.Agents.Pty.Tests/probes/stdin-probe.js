#!/usr/bin/env node
// A Node peer for the pty input path — the half no CI test covered.
//
// The existing fake Claude is a .NET console program: it reads stdin through
// Console/ReadFile and receives everything. The real Claude is Node, whose stdin on
// Windows goes through libuv's TTY reader (ReadConsoleInputW -> UTF-8), a completely
// different consumer of the same ConPTY. This probe is that consumer, and nothing more:
// it accumulates every byte it is given and reports what it got, so a shortfall here is
// the runtime/console losing bytes, and no shortfall here means the loss is in the TUI's
// own JavaScript.
//
// Protocol: write a body built of "L%04d " marked lines, then the sentinel line
// "PROBE-REPORT". The probe prints a one-line JSON summary and a per-chunk log.
//
// Env:
//   PROBE_RAW=0            leave stdin in cooked mode (default 1 = setRawMode(true))
//   PROBE_BLOCK_MS=<n>     spin the event loop for n ms on every data event (models a
//                          TUI that renders between reads and drains the pipe in bursts)
//   PROBE_CHUNKLOG=0       suppress the per-chunk lines
//   PROBE_DECSET_2004=1    ask the terminal for bracketed paste (ESC[?2004h) the way every real
//                          TUI does (Ink/Claude Code do it on mount). CARD-0030: conhost only
//                          FORWARDS ESC[200~/ESC[201~ to a client that has requested the mode —
//                          without this the probe is a false negative on "do the markers arrive".
//   PROBE_OUT=<path>       also append every report line to this file. Needed when the probe runs
//                          under a real terminal (Windows Terminal), where stdout is a screen and
//                          not something a harness can read back.

const raw = process.env.PROBE_RAW !== '0';
const blockMs = Number(process.env.PROBE_BLOCK_MS || 0);
const chunkLog = process.env.PROBE_CHUNKLOG !== '0';
const decset2004 = process.env.PROBE_DECSET_2004 === '1';
const outFile = process.env.PROBE_OUT || '';
const fs = outFile ? require('fs') : null;

const out = (s) => {
  process.stdout.write(s + '\r\n');
  if (fs) { try { fs.appendFileSync(outFile, s + '\n'); } catch { /* screen is enough */ } }
};

if (raw && process.stdin.isTTY) process.stdin.setRawMode(true);
if (decset2004) process.stdout.write('\x1b[?2004h');
process.stdin.resume();

let acc = Buffer.alloc(0);
let chunkCount = 0;
let byteTotal = 0;
const sizes = [];
const ticks = [];
const gaps = [];
let firstAt = 0;
let lastAt = 0;

// Which event-loop turn each chunk landed in. Chunks sharing a turn are what a React/Ink
// consumer would batch into ONE state update — the difference between "n appends" and
// "n racing appends over the same stale snapshot".
let tickId = 0;
let tickPending = false;
function currentTick() {
  if (!tickPending) {
    tickPending = true;
    setImmediate(() => { tickPending = false; tickId++; });
  }
  return tickId;
}

out(`PROBE-READY raw=${raw} isTTY=${!!process.stdin.isTTY} blockMs=${blockMs} decset2004=${decset2004} pid=${process.pid}`);

// A truncated delivery can lose the PROBE-REPORT sentinel itself, and then the harness waits
// forever on a report that is never coming. Quiet-triggered reporting makes "what arrived" always
// observable: set PROBE_QUIET_MS and the probe reports that long after the last byte.
const quietMs = Number(process.env.PROBE_QUIET_MS || 0);
let quietTimer = null;

process.stdin.on('data', (d) => {
  const now = Date.now();
  if (!firstAt) firstAt = now;
  chunkCount++;
  byteTotal += d.length;
  sizes.push(d.length);
  gaps.push(lastAt ? now - lastAt : 0);
  lastAt = now;
  ticks.push(currentTick());
  acc = Buffer.concat([acc, d]);
  if (chunkLog) out(`CHUNK ${chunkCount} len=${d.length} total=${byteTotal} t=${now - firstAt}`);
  if (blockMs > 0) {
    const until = Date.now() + blockMs;
    while (Date.now() < until) { /* spin: no reads happen while we render */ }
  }
  if (quietMs > 0) {
    if (quietTimer) clearTimeout(quietTimer);
    quietTimer = setTimeout(() => { quietTimer = null; if (byteTotal > 0) report(); }, quietMs);
  }
  if (acc.includes('PROBE-REPORT')) report();
});

function report() {
  const text = acc.toString('utf8');
  // Which marked lines arrived? A gap names the exact span that vanished.
  const seen = new Set();
  for (const m of text.matchAll(/L(\d{4}) /g)) seen.add(Number(m[1]));
  const max = seen.size ? Math.max(...seen) : -1;
  const missing = [];
  for (let i = 0; i <= max; i++) if (!seen.has(i)) missing.push(i);
  const runs = [];
  for (const i of missing) {
    const last = runs[runs.length - 1];
    if (last && last[1] === i - 1) last[1] = i;
    else runs.push([i, i]);
  }
  // How many chunks landed in the busiest single event-loop turn.
  const perTick = new Map();
  for (const t of ticks) perTick.set(t, (perTick.get(t) || 0) + 1);
  out('PROBE-SUMMARY ' + JSON.stringify({
    bytes: byteTotal,
    chunks: chunkCount,
    turns: perTick.size,
    maxChunksPerTurn: perTick.size ? Math.max(...perTick.values()) : 0,
    sizes: sizes.slice(0, 64),
    distinctSizes: [...new Set(sizes)].sort((a, b) => a - b).slice(0, 24),
    linesSeen: seen.size,
    highestLine: max,
    missingCount: missing.length,
    missingRuns: runs.slice(0, 20),
    hasPasteStart: text.includes('[200~'),
    hasPasteEnd: text.includes('[201~'),
    crCount: (text.match(/\r/g) || []).length,
    // Raw evidence, not a boolean: what the first/last bytes of the delivery actually were. A
    // stripped ESC[200~ and a delivered one look identical in a summary that only counts lines.
    headHex: acc.subarray(0, 24).toString('hex'),
    tailHex: acc.subarray(Math.max(0, acc.length - 24)).toString('hex'),
    gaps: gaps.slice(0, 64),
    spanMs: lastAt && firstAt ? lastAt - firstAt : 0,
  }));
  acc = Buffer.alloc(0);
  chunkCount = 0;
  byteTotal = 0;
  sizes.length = 0;
  ticks.length = 0;
  gaps.length = 0;
  firstAt = 0;
  lastAt = 0;
}

process.stdin.on('end', () => out('PROBE-END'));
