/**
 * CARD-0247 S2 live probe — not part of test-hooks.ps1 (not a *.test.mjs).
 *
 * Exercises the committed .claude/settings.json command string against a real
 * `claude -p` in a throwaway sandbox whose path contains `/Antiphon/server/`
 * so S1's repoRelative() classifies the reads as source reads.
 *
 * Usage (from repo root):
 *   node scripts/hooks/__tests__/live-s2-probe.mjs
 *
 * Requires `claude` on PATH. Spends two Haiku print-mode turns.
 */
import { spawn } from 'node:child_process';
import {
  cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync,
  rmSync, writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, '..', '..', '..');
const settingsSrc = join(repoRoot, '.claude', 'settings.json');
const hookSrc = join(repoRoot, 'scripts', 'hooks');

function resolveClaude() {
  const pathEnv = process.env.PATH || '';
  const names = process.platform === 'win32'
    ? ['claude.exe', 'claude.cmd', 'claude.bat', 'cl.bat', 'cl.cmd']
    : ['claude'];
  for (const dir of pathEnv.split(process.platform === 'win32' ? ';' : ':')) {
    for (const name of names) {
      const candidate = join(dir, name);
      if (existsSync(candidate)) return candidate;
    }
  }
  return null;
}

function run(app, args, { cwd, env, timeoutMs }) {
  return new Promise((resolve, reject) => {
    const child = spawn(app, args, {
      cwd,
      env,
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
      shell: app.endsWith('.cmd') || app.endsWith('.bat'),
    });
    let stdout = '';
    let stderr = '';
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (c) => { stdout += c; });
    child.stderr.on('data', (c) => { stderr += c; });
    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`timeout ${timeoutMs}ms\nstdout:\n${stdout}\nstderr:\n${stderr}`));
    }, timeoutMs);
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code, stdout, stderr });
    });
    child.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });
}

function resultText(stdout) {
  const trimmed = stdout.trim();
  try {
    const parsed = JSON.parse(trimmed);
    return parsed.result || trimmed;
  } catch {
    const start = trimmed.indexOf('{');
    const end = trimmed.lastIndexOf('}');
    if (start >= 0 && end > start) {
      try {
        const parsed = JSON.parse(trimmed.slice(start, end + 1));
        return parsed.result || trimmed;
      } catch { /* fall through */ }
    }
    return trimmed;
  }
}

function hookLog(path) {
  if (!existsSync(path)) return [];
  return readFileSync(path, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
}

function childEnv(overrides) {
  const env = { ...process.env };
  // Neutralize nested-Claude markers (S0 HeadedSafeEnv) and this worker's identity
  // so the probe session is an armed operator-shaped session unless we say otherwise.
  env.CLAUDE_CODE_CHILD_SESSION = '';
  env.CLAUDE_CODE_SESSION_ID = '';
  env.CLAUDE_CODE_BRIDGE_SESSION_ID = '';
  delete env.ANTIPHON_TASK_ID;
  delete env.ANTIPHON_TASK_KIND;
  delete env.ANTIPHON_ORCHESTRATOR;
  Object.assign(env, overrides);
  return env;
}

async function oneTurn({ sandbox, settingsPath, env, sessionId, prompt }) {
  const claude = resolveClaude();
  if (!claude) throw new Error('claude not on PATH');
  const args = [
    '--dangerously-skip-permissions',
    '--strict-mcp-config',
    '--settings', settingsPath,
    '--session-id', sessionId,
    '--model', 'haiku',
    '--allowedTools', 'Read',
    '--max-turns', '10',
    '--output-format', 'json',
    '-p',
    prompt,
  ];
  return run(claude, args, { cwd: sandbox, env, timeoutMs: 180_000 });
}

function writeSandbox(token) {
  // repoRelative() takes the path AFTER the last `/antiphon/` segment. The fake
  // root must therefore BE that segment so `server/` and `tests/` sit at the
  // classifier's source roots.
  const sandbox = join(tmpdir(), 'Antiphon');
  mkdirSync(join(sandbox, 'server'), { recursive: true });
  mkdirSync(join(sandbox, 'tests'), { recursive: true });
  mkdirSync(join(sandbox, 'scripts', 'hooks'), { recursive: true });
  mkdirSync(join(sandbox, '.claude'), { recursive: true });

  const alpha = `ALPHA-${token}`;
  const beta = `BETA-${token}`;
  const gamma = `GAMMA-${token}`;
  writeFileSync(join(sandbox, 'server', 'Alpha.cs'), `// {alpha}\nnamespace Probe;\nclass Alpha {{}}\n`.replace('{alpha}', alpha));
  writeFileSync(join(sandbox, 'server', 'Beta.cs'), `// {beta}\nnamespace Probe;\nclass Beta {{}}\n`.replace('{beta}', beta));
  writeFileSync(join(sandbox, 'tests', 'Gamma.cs'), `// {gamma}\nnamespace Probe;\nclass Gamma {{}}\n`.replace('{gamma}', gamma));
  writeFileSync(join(sandbox, 'CLAUDE.md'), 'Throwaway CARD-0247 S2 probe. Follow the user prompt exactly.\n');
  cpSync(join(hookSrc, 'orchestrator-investigation.mjs'), join(sandbox, 'scripts', 'hooks', 'orchestrator-investigation.mjs'));
  cpSync(join(hookSrc, 'orchestrator-investigation-hook.mjs'), join(sandbox, 'scripts', 'hooks', 'orchestrator-investigation-hook.mjs'));
  const settings = readFileSync(settingsSrc, 'utf8');
  const settingsPath = join(sandbox, 'settings.json');
  writeFileSync(settingsPath, settings);
  writeFileSync(join(sandbox, '.claude', 'settings.json'), settings);
  return { sandbox, settingsPath, alpha, beta, gamma };
}

const prompt = (alpha, beta, gamma) =>
  `Using the Read tool (do not guess), read these three files in order: server/Alpha.cs, server/Beta.cs, tests/Gamma.cs. `
  + `After all three reads, reply with four lines: the token from Alpha.cs, the token from Beta.cs, the token from Gamma.cs, `
  + `then either the exact [antiphon-orchestrator] note if one was added to your context or the word NONE. `
  + `Tokens look like ${alpha} / ${beta} / ${gamma}.`;

async function main() {
  const token = Math.random().toString(16).slice(2, 10).toUpperCase();
  const { sandbox, settingsPath, alpha, beta, gamma } = writeSandbox(token);
  const logArmed = join(sandbox, `hook-armed-${token}.jsonl`);
  const logWorker = join(sandbox, `hook-worker-${token}.jsonl`);
  const report = { sandbox, alpha, beta, gamma, armed: null, worker: null };

  console.log(`SANDBOX ${sandbox}`);
  console.log(`SETTINGS command: ${JSON.parse(readFileSync(settingsPath, 'utf8')).hooks.PreToolUse[0].hooks[0].command}`);

  const armed = await oneTurn({
    sandbox,
    settingsPath,
    sessionId: crypto.randomUUID(),
    env: childEnv({ ANTIPHON_HOOK_LOG: logArmed }),
    prompt: prompt(alpha, beta, gamma),
  });
  report.armed = {
    exit: armed.code,
    text: resultText(armed.stdout),
    hook: hookLog(logArmed),
    stderrTail: armed.stderr.slice(-1500),
  };
  console.log('ARMED EXIT', armed.code);
  console.log('ARMED TEXT\n', report.armed.text);
  console.log('ARMED HOOK', JSON.stringify(report.armed.hook, null, 2));

  const worker = await oneTurn({
    sandbox,
    settingsPath,
    sessionId: crypto.randomUUID(),
    env: childEnv({
      ANTIPHON_HOOK_LOG: logWorker,
      ANTIPHON_TASK_ID: `s2-worker-${token}`,
      ANTIPHON_TASK_KIND: 'Worker',
    }),
    prompt: prompt(alpha, beta, gamma),
  });
  report.worker = {
    exit: worker.code,
    text: resultText(worker.stdout),
    hook: hookLog(logWorker),
    stderrTail: worker.stderr.slice(-1500),
  };
  console.log('WORKER EXIT', worker.code);
  console.log('WORKER TEXT\n', report.worker.text);
  console.log('WORKER HOOK', JSON.stringify(report.worker.hook, null, 2));

  const armedNudged = report.armed.hook.some((r) => r.nudged === true);
  const armedReadRan = (report.armed.text || '').includes(alpha)
    && (report.armed.text || '').includes(beta)
    && (report.armed.text || '').includes(gamma);
  const workerSilent = report.worker.hook.every((r) => r.armed === false || r.nudged !== true);
  const workerReadRan = (report.worker.text || '').includes(alpha);

  const verdict = {
    armedExit0: report.armed.exit === 0,
    workerExit0: report.worker.exit === 0,
    armedReadRan,
    workerReadRan,
    armedNudged,
    workerSilent,
    armedMentionsNudge: /\[antiphon-orchestrator\]/.test(report.armed.text || ''),
    workerMentionsNudge: /\[antiphon-orchestrator\]/.test(report.worker.text || ''),
  };
  console.log('VERDICT', JSON.stringify(verdict, null, 2));

  const out = join(repoRoot, '.antiphon', 'task-75ca975b-live-s2.json');
  try {
    writeFileSync(out, JSON.stringify({ verdict, report }, null, 2));
    console.log('WROTE', out);
  } catch {
    const fallback = join(tmpdir(), 'task-75ca975b-live-s2.json');
    writeFileSync(fallback, JSON.stringify({ verdict, report }, null, 2));
    console.log('WROTE', fallback);
  }

  const ok = verdict.armedExit0 && verdict.workerExit0 && verdict.armedReadRan
    && verdict.armedNudged && verdict.workerSilent && !verdict.workerMentionsNudge;
  process.exit(ok ? 0 : 1);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
