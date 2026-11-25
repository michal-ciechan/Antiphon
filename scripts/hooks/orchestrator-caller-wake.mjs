// Claude owns this asyncRewake child. Exit 2 supplies a system reminder even between turns.
import { randomUUID } from 'node:crypto';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const guid = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;

async function run() {
  const env = process.env;
  const standing = env.ANTIPHON_ORCHESTRATOR === '1' && !env.ANTIPHON_TASK_ID;
  if (!standing && env.ANTIPHON_TASK_KIND !== 'Orchestrator') return;
  if (!guid.test(env.ANTIPHON_AGENT_ID ?? '') || !env.ANTIPHON_TASK_TOKEN || !env.ANTIPHON_API) return;

  let input = '';
  for await (const chunk of process.stdin) input += chunk;
  const hook = JSON.parse(input);
  if (hook.agent_id || !guid.test(hook.session_id) || !['SessionStart', 'Stop'].includes(hook.hook_event_name)) return;
  if (hook.session_id.toLowerCase() !== env.ANTIPHON_SESSION_ID?.toLowerCase() || !env.ANTIPHON_SESSION_GENERATION) return;

  const url = new URL('/api/orchestrators/' + env.ANTIPHON_AGENT_ID + '/caller-wake', env.ANTIPHON_API);
  if (!['http:', 'https:'].includes(url.protocol)) return;
  url.searchParams.set('conversationId', hook.session_id);
  url.searchParams.set('generation', env.ANTIPHON_SESSION_GENERATION);

  // A fresh Stop, resume or compaction takes over from the previous hook for this provider
  // conversation. Atomic replacement also recovers from a killed hook on Windows.
  const dir = env.ANTIPHON_HOOK_STATE_DIR || path.join(homedir(), '.antiphon', 'hooks');
  await mkdir(dir, { recursive: true, mode: 0o700 });
  const marker = path.join(dir, 'caller-wake-' + hook.session_id + '.owner');
  const owner = randomUUID();
  await writeFile(marker + '.' + owner, owner, { flag: 'wx', mode: 0o600 });
  await rename(marker + '.' + owner, marker);

  const parent = process.ppid;
  // Async hooks have a finite provider deadline. A caller idle for the entire
  // fortnight needs one native turn to install its successor before Claude times it out.
  const renewAt = Date.now() + (14 * 24 * 60 - 5) * 60_000;
  while ((await readFile(marker, 'utf8')) === owner) {
    if (process.ppid !== parent) return;
    try { process.kill(parent, 0); } catch (error) { if (error.code === 'ESRCH') return; }
    try {
      const response = await fetch(url, {
        method: 'POST', headers: { 'X-Antiphon-Task-Token': env.ANTIPHON_TASK_TOKEN },
        redirect: 'error', signal: AbortSignal.timeout(10_000),
      });
      if (response.status === 401 || response.status === 403) return;
      if (response.ok) {
        const { reportTaskIds, shortfall, idle } = await response.json();
        const ids = Array.isArray(reportTaskIds) ? reportTaskIds.filter(id => typeof id === 'string' && guid.test(id)).slice(0, 16) : [];
        const messages = [];
        if (ids.length) {
          // A generated orchestrator workspace can sit outside the checkout that owns this hook.
          const windows = process.platform === 'win32';
          const launcher = fileURLToPath(new URL(windows ? '../antiphon.cmd' : '../antiphon', import.meta.url));
          const command = windows ? "& '" + launcher.replaceAll("'", "''") + "'" : "'" + launcher.replaceAll("'", "'\"'\"'") + "'";
          messages.push('Read the outstanding delegate reports.' + ids.map(id => '\n' + command + ' delegate status ' + id).join(''));
        }
        if (Number.isSafeInteger(shortfall) && shortfall > 0) messages.push('The keep-six shortfall is ' + shortfall + '. Read the current pipeline and fill the eligible slots or record a hold.');
        if (!messages.length && idle && Date.now() >= renewAt)
          messages.push('The caller notification watch is renewing. End this turn to re-arm the Stop hook.');
        if (messages.length && (await readFile(marker, 'utf8')) === owner) {
          // Each status command is a whole line the caller runs as given. Another notice on
          // that line would become extra CLI arguments and the report read would be refused.
          process.stderr.write('[Antiphon] ' + messages.join('\n') + '\n');
          process.exitCode = 2;
          return;
        }
      }
    } catch { /* The server may be restarting. The next poll uses the same bound credential. */ }
    await delay(60_000);
  }
}

run().catch(() => { /* Invalid input or local hook state cannot authorize a wake. */ });
