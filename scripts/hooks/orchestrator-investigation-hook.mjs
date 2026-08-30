/**
 * CARD-0247 S2: PreToolUse / SessionStart wrapper. Node on the hot path (no pwsh).
 *
 * Fail-open: every error path exits 0 with no stdout. classify() never returns deny.
 *
 * Transcript tail is 256 KB. S1's CARD-0246 fixture is 37 KB for a 12-tool-call cold
 * run; N_report=25 at that density is ~77 KB plus a 14 KB report (~91 KB). 256 KB is
 * ~2.8x that window and covers N_report=25 / N_dispatch=10 with headroom. A torn
 * first line from a mid-record cut is skipped by parseTranscript.
 */
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const TAIL_BYTES = 256 * 1024;

function failOpen() {
  process.exit(0);
}

function nonEmpty(v) {
  return typeof v === 'string' && v.trim().length > 0;
}

/**
 * Plan §3.1 environment discriminator. Order is load-bearing.
 * @param {object} input hook stdin JSON
 * @param {NodeJS.ProcessEnv} env
 * @returns {{ armed: boolean, reason: string }}
 */
export function applyDiscriminator(input, env) {
  if (nonEmpty(input?.agent_id)) return { armed: false, reason: 'subagent' };
  if (env.ANTIPHON_TASK_KIND === 'Orchestrator') return { armed: true, reason: 'task-kind-orchestrator' };
  if (nonEmpty(env.ANTIPHON_TASK_ID)) return { armed: false, reason: 'worker-task' };
  if (env.ANTIPHON_ORCHESTRATOR === '0') return { armed: false, reason: 'opt-out' };
  return { armed: true, reason: 'default-orchestrator' };
}

function stateDir() {
  return envOr(process.env.ANTIPHON_HOOK_STATE_DIR, path.join(os.tmpdir(), 'antiphon-hooks'));
}

function envOr(v, fallback) {
  return nonEmpty(v) ? v : fallback;
}

function safeSessionFile(sessionId) {
  const raw = nonEmpty(sessionId) ? sessionId : 'unknown';
  return `${raw.replace(/[^A-Za-z0-9._-]/g, '_')}.json`;
}

function loadState(sessionId) {
  try {
    const file = path.join(stateDir(), safeSessionFile(sessionId));
    if (!fs.existsSync(file)) return null;
    const parsed = JSON.parse(fs.readFileSync(file, 'utf8'));
    return parsed && typeof parsed === 'object' ? parsed : null;
  } catch {
    return null;
  }
}

function saveState(sessionId, state) {
  const dir = stateDir();
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, safeSessionFile(sessionId)), JSON.stringify(state));
}

function readTranscriptTail(filePath, maxBytes) {
  const fd = fs.openSync(filePath, 'r');
  try {
    const size = fs.fstatSync(fd).size;
    if (size <= 0) return '';
    const start = Math.max(0, size - maxBytes);
    const len = size - start;
    const buf = Buffer.alloc(len);
    fs.readSync(fd, buf, 0, len, start);
    let text = buf.toString('utf8');
    if (start > 0) {
      const nl = text.indexOf('\n');
      if (nl >= 0) text = text.slice(nl + 1);
    }
    return text;
  } finally {
    fs.closeSync(fd);
  }
}

function optionalLog(record) {
  const logPath = process.env.ANTIPHON_HOOK_LOG;
  if (!nonEmpty(logPath)) return;
  try {
    fs.appendFileSync(logPath, JSON.stringify(record) + '\n');
  } catch {
    /* never stderr */
  }
}

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  if (chunks.length === 0) return '';
  return Buffer.isBuffer(chunks[0]) ? Buffer.concat(chunks).toString('utf8') : chunks.join('');
}

async function main() {
  const started = Date.now();
  try {
    const raw = await readStdin();
    if (!raw.trim()) {
      failOpen();
      return;
    }
    let input;
    try {
      input = JSON.parse(raw);
    } catch {
      failOpen();
      return;
    }
    if (!input || typeof input !== 'object') {
      failOpen();
      return;
    }

    const gate = applyDiscriminator(input, process.env);
    optionalLog({
      receivedAt: new Date().toISOString(),
      ms: Date.now() - started,
      hookEventName: input.hook_event_name ?? null,
      toolName: input.tool_name ?? null,
      sessionId: input.session_id ?? null,
      ...gate,
    });
    if (!gate.armed) {
      failOpen();
      return;
    }

    const mod = await import('./orchestrator-investigation.mjs');

    if (input.hook_event_name === 'SessionStart') {
      if (typeof input.source === 'string' && input.source !== 'compact') {
        failOpen();
        return;
      }
      process.stdout.write(JSON.stringify({
        hookSpecificOutput: {
          hookEventName: 'SessionStart',
          additionalContext: mod.COMPACT_CONTEXT,
        },
      }));
      failOpen();
      return;
    }

    const transcriptPath = typeof input.transcript_path === 'string' ? input.transcript_path : '';
    if (!transcriptPath || !fs.existsSync(transcriptPath)) {
      failOpen();
      return;
    }

    let tail;
    try {
      tail = readTranscriptTail(transcriptPath, TAIL_BYTES);
    } catch {
      failOpen();
      return;
    }

    const sessionId = typeof input.session_id === 'string' ? input.session_id : 'unknown';
    const state = loadState(sessionId);
    const result = mod.classify(input, tail, state);
    try {
      saveState(sessionId, result.state);
    } catch {
      /* still fail-open on the nudge itself */
    }
    optionalLog({
      receivedAt: new Date().toISOString(),
      ms: Date.now() - started,
      sessionId,
      nudged: result.nudged,
      reason: result.reason,
      runLength: result.runLength,
      classification: result.classification,
    });
    if (result.nudged && result.hookOutput) {
      process.stdout.write(JSON.stringify(result.hookOutput));
    }
    failOpen();
  } catch {
    failOpen();
  }
}

const invoked = process.argv[1]
  && path.resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase();

if (invoked) {
  main().then(() => process.exit(0), () => process.exit(0));
}
