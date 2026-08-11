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

const raw = process.env.PROBE_RAW !== '0';
const blockMs = Number(process.env.PROBE_BLOCK_MS || 0);
const chunkLog = process.env.PROBE_CHUNKLOG !== '0';

const out = (s) => process.stdout.write(s + '\r\n');

if (raw && process.stdin.isTTY) process.stdin.setRawMode(true);
process.stdin.resume();

let acc = Buffer.alloc(0);
let chunkCount = 0;
let byteTotal = 0;
const sizes = [];
const ticks = [];
let firstAt = 0;

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

out(`PROBE-READY raw=${raw} isTTY=${!!process.stdin.isTTY} blockMs=${blockMs} pid=${process.pid}`);

process.stdin.on('data', (d) => {
  const now = Date.now();
  if (!firstAt) firstAt = now;
  chunkCount++;
  byteTotal += d.length;
  sizes.push(d.length);
  ticks.push(currentTick());
  acc = Buffer.concat([acc, d]);
  if (chunkLog) out(`CHUNK ${chunkCount} len=${d.length} total=${byteTotal} t=${now - firstAt}`);
  if (blockMs > 0) {
    const until = Date.now() + blockMs;
    while (Date.now() < until) { /* spin: no reads happen while we render */ }
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
  }));
  acc = Buffer.alloc(0);
  chunkCount = 0;
  byteTotal = 0;
  sizes.length = 0;
  ticks.length = 0;
  firstAt = 0;
}

process.stdin.on('end', () => out('PROBE-END'));
