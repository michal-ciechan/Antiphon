import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  applyDiscriminator,
  TAIL_BYTES,
} from '../orchestrator-investigation-hook.mjs';
import {
  NUDGE_CONTEXT,
  COMPACT_CONTEXT,
  R,
} from '../orchestrator-investigation.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const wrapper = join(here, '..', 'orchestrator-investigation-hook.mjs');
const settingsPath = join(here, '..', '..', '..', '.claude', 'settings.json');
const card0246Tail = readFileSync(
  join(here, 'fixtures', 'cefed08a-card0246.jsonl'),
  'utf8',
);

describe('settings.json install', () => {
  const settings = JSON.parse(readFileSync(settingsPath, 'utf8'));

  it('keeps the original links block and adds hooks beside it', () => {
    assert.ok(Array.isArray(settings.links));
    assert.equal(settings.links[0].name, 'Antiphon');
    const aspire = settings.links[0].links.find((l) => l.name === 'Aspire (AppHost)');
    assert.ok(aspire, 'Aspire links group must survive');
    assert.ok(aspire.links.some((l) => l.name === 'Client' && l.url.includes('17203')));
    assert.ok(settings.hooks);
    assert.ok(settings.hooks.PreToolUse);
    assert.ok(settings.hooks.SessionStart);
  });

  it('PreToolUse matcher covers Read|Grep|Glob|Bash|PowerShell, node command, timeout 5', () => {
    const entry = settings.hooks.PreToolUse[0];
    assert.equal(entry.matcher, 'Read|Grep|Glob|Bash|PowerShell');
    const hook = entry.hooks[0];
    assert.equal(hook.type, 'command');
    assert.equal(hook.timeout, 5);
    assert.match(hook.command, /^node /);
    assert.doesNotMatch(hook.command, /pwsh|powershell/i);
    assert.match(hook.command, /orchestrator-investigation-hook\.mjs/);
    assert.match(hook.command, /\$\{CLAUDE_PROJECT_DIR\}/);
  });

  it('SessionStart matcher is compact and reuses the same wrapper', () => {
    const entry = settings.hooks.SessionStart[0];
    assert.equal(entry.matcher, 'compact');
    assert.equal(
      entry.hooks[0].command,
      settings.hooks.PreToolUse[0].hooks[0].command,
    );
  });
});

describe('tail budget vs S1 fixtures', () => {
  it('256 KB covers N_report=25 at CARD-0246 density with headroom', () => {
    assert.equal(TAIL_BYTES, 256 * 1024);
    // 37 211 bytes for a 12-tool-call cold run → ~77 KB at 25 tools + 14 KB report.
    assert.ok(card0246Tail.length < 40_000);
    const projected = Math.ceil(card0246Tail.length * (25 / 12)) + 14_000;
    assert.ok(projected < TAIL_BYTES, `projected ${projected} must fit in ${TAIL_BYTES}`);
  });
});

describe('applyDiscriminator (§3.1 order)', () => {
  const read = { tool_name: 'Read', tool_input: { file_path: 'server/Foo.cs' } };

  it('1. agent_id present → unarmed (subagent is the delegated reader)', () => {
    const g = applyDiscriminator({ ...read, agent_id: 'agt_1' }, {});
    assert.equal(g.armed, false);
    assert.equal(g.reason, 'subagent');
  });

  it('2. ANTIPHON_TASK_KIND=Orchestrator → armed even with TASK_ID set', () => {
    const g = applyDiscriminator(read, {
      ANTIPHON_TASK_KIND: 'Orchestrator',
      ANTIPHON_TASK_ID: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    });
    assert.equal(g.armed, true);
    assert.equal(g.reason, 'task-kind-orchestrator');
  });

  it('3. ANTIPHON_TASK_ID set on a worker → unarmed', () => {
    const g = applyDiscriminator(read, {
      ANTIPHON_TASK_KIND: 'Worker',
      ANTIPHON_TASK_ID: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    });
    assert.equal(g.armed, false);
    assert.equal(g.reason, 'worker-task');
  });

  it('4. ANTIPHON_ORCHESTRATOR=0 → unarmed', () => {
    const g = applyDiscriminator(read, { ANTIPHON_ORCHESTRATOR: '0' });
    assert.equal(g.armed, false);
    assert.equal(g.reason, 'opt-out');
  });

  it('5. otherwise → armed (operator / standing agent)', () => {
    const g = applyDiscriminator(read, {});
    assert.equal(g.armed, true);
    assert.equal(g.reason, 'default-orchestrator');
  });

  it('agent_id outranks Orchestrator kind', () => {
    const g = applyDiscriminator(
      { ...read, agent_id: 'agt_1' },
      { ANTIPHON_TASK_KIND: 'Orchestrator' },
    );
    assert.equal(g.armed, false);
    assert.equal(g.reason, 'subagent');
  });
});

describe('wrapper process (fail-open, nudge, compact)', () => {
  it('malformed stdin exits 0 with empty stdout and empty stderr', async () => {
    const r = await invoke('not-json{', {});
    assert.equal(r.code, 0);
    assert.equal(r.stdout, '');
    assert.equal(r.stderr, '');
  });

  it('missing transcript exits 0 with no output when armed', async () => {
    const r = await invoke(JSON.stringify({
      hook_event_name: 'PreToolUse',
      session_id: 's2-missing',
      tool_name: 'Read',
      tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Foo.cs' },
      tool_use_id: 'toolu_missing',
      transcript_path: join(tmpdir(), 'antiphon-hooks-no-such-transcript.jsonl'),
    }), {});
    assert.equal(r.code, 0);
    assert.equal(r.stdout, '');
    assert.equal(r.stderr, '');
  });

  it('worker TASK_ID skips classify and writes nothing', async () => {
    const dir = mkdtempSync(join(tmpdir(), 's2-hook-'));
    try {
      const transcript = join(dir, 't.jsonl');
      writeFileSync(transcript, coldRunTranscript(3));
      const r = await invoke(JSON.stringify({
        hook_event_name: 'PreToolUse',
        session_id: 's2-worker',
        tool_name: 'Read',
        tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Application\\Services\\Baz.cs' },
        tool_use_id: 'toolu_c3',
        transcript_path: transcript,
      }), { ANTIPHON_TASK_ID: 'task-worker' });
      assert.equal(r.code, 0);
      assert.equal(r.stdout, '');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('subagent agent_id writes nothing even on a cold run', async () => {
    const dir = mkdtempSync(join(tmpdir(), 's2-hook-'));
    try {
      const transcript = join(dir, 't.jsonl');
      writeFileSync(transcript, coldRunTranscript(3));
      const r = await invoke(JSON.stringify({
        hook_event_name: 'PreToolUse',
        session_id: 's2-sub',
        agent_id: 'agt_sub',
        tool_name: 'Read',
        tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Application\\Services\\Baz.cs' },
        tool_use_id: 'toolu_c3',
        transcript_path: transcript,
      }), {});
      assert.equal(r.code, 0);
      assert.equal(r.stdout, '');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('armed cold run of R source reads nudges once with allow + NUDGE_CONTEXT', async () => {
    const dir = mkdtempSync(join(tmpdir(), 's2-hook-'));
    try {
      const transcript = join(dir, 't.jsonl');
      writeFileSync(transcript, coldRunTranscript(R));
      const stateDir = join(dir, 'state');
      mkdirSync(stateDir);
      const r = await invoke(JSON.stringify({
        hook_event_name: 'PreToolUse',
        session_id: 's2-armed',
        tool_name: 'Read',
        tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Application\\Services\\Baz.cs' },
        tool_use_id: 'toolu_c3',
        transcript_path: transcript,
      }), { ANTIPHON_HOOK_STATE_DIR: stateDir });
      assert.equal(r.code, 0);
      assert.equal(r.stderr, '');
      const out = JSON.parse(r.stdout);
      assert.equal(out.hookSpecificOutput.hookEventName, 'PreToolUse');
      assert.equal(out.hookSpecificOutput.permissionDecision, 'allow');
      assert.equal(out.hookSpecificOutput.additionalContext, NUDGE_CONTEXT);
      const saved = JSON.parse(readFileSync(join(stateDir, 's2-armed.json'), 'utf8'));
      assert.equal(saved.nudgedForRunStartedId, 'toolu_c1');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('second call on the same run does not nudge again', async () => {
    const dir = mkdtempSync(join(tmpdir(), 's2-hook-'));
    try {
      const transcript = join(dir, 't.jsonl');
      writeFileSync(transcript, coldRunTranscript(R));
      const stateDir = join(dir, 'state');
      mkdirSync(stateDir);
      const input = {
        hook_event_name: 'PreToolUse',
        session_id: 's2-once',
        tool_name: 'Read',
        tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Application\\Services\\Baz.cs' },
        tool_use_id: 'toolu_c3',
        transcript_path: transcript,
      };
      const first = await invoke(JSON.stringify(input), { ANTIPHON_HOOK_STATE_DIR: stateDir });
      assert.ok(first.stdout.includes('antiphon-orchestrator'));
      const second = await invoke(JSON.stringify({
        ...input,
        tool_use_id: 'toolu_c4',
        tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Application\\Services\\Qux.cs' },
      }), { ANTIPHON_HOOK_STATE_DIR: stateDir });
      assert.equal(second.code, 0);
      assert.equal(second.stdout, '');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('SessionStart compact re-injects COMPACT_CONTEXT when armed', async () => {
    const r = await invoke(JSON.stringify({
      hook_event_name: 'SessionStart',
      source: 'compact',
      session_id: 's2-compact',
    }), {});
    assert.equal(r.code, 0);
    const out = JSON.parse(r.stdout);
    assert.equal(out.hookSpecificOutput.hookEventName, 'SessionStart');
    assert.equal(out.hookSpecificOutput.additionalContext, COMPACT_CONTEXT);
    assert.ok(!('permissionDecision' in (out.hookSpecificOutput)));
  });

  it('SessionStart compact is silent for a worker', async () => {
    const r = await invoke(JSON.stringify({
      hook_event_name: 'SessionStart',
      source: 'compact',
      session_id: 's2-compact-worker',
    }), { ANTIPHON_TASK_ID: 'task-worker' });
    assert.equal(r.code, 0);
    assert.equal(r.stdout, '');
  });

  it('finishes well under the 5s hook timeout on the CARD-0246 fixture', async () => {
    const dir = mkdtempSync(join(tmpdir(), 's2-hook-'));
    try {
      const transcript = join(dir, 't.jsonl');
      writeFileSync(transcript, card0246Tail);
      const t0 = Date.now();
      const r = await invoke(JSON.stringify({
        hook_event_name: 'PreToolUse',
        session_id: 'cefed08a-fd4a-42a0-8c76-0fbf82cf6b20',
        tool_name: 'Bash',
        tool_input: {
          command: 'cd C:/src/Antiphon && grep -rn "class AgentLaunchResolution" server/',
        },
        tool_use_id: 'toolu_01Gcqwng6fQ96CK7TbYsGoCo',
        transcript_path: transcript,
      }), {});
      const ms = Date.now() - t0;
      assert.equal(r.code, 0);
      assert.ok(ms < 2000, `wrapper took ${ms}ms; 5s timeout would be tight`);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});

function coldRunTranscript(n) {
  const files = ['Foo.cs', 'Bar.cs', 'Baz.cs', 'Qux.cs'];
  const rows = [
    {
      type: 'user',
      origin: { kind: 'human' },
      message: { role: 'user', content: 'Please look into why launches fail' },
    },
  ];
  for (let i = 0; i < n; i++) {
    rows.push({
      type: 'assistant',
      message: {
        role: 'assistant',
        content: [{
          type: 'tool_use',
          id: `toolu_c${i + 1}`,
          name: 'Read',
          input: {
            file_path: `C:\\src\\Antiphon\\server\\Application\\Services\\${files[i]}`,
          },
        }],
      },
    });
  }
  return rows.map((r) => JSON.stringify(r)).join('\n') + '\n';
}

function invoke(stdin, extraEnv) {
  return new Promise((resolve, reject) => {
    const env = { ...process.env };
    // This suite often runs inside a worker delegate; those identity vars would
    // unarm the hook (rule 3) and hide every armed-path assertion.
    delete env.ANTIPHON_TASK_ID;
    delete env.ANTIPHON_TASK_KIND;
    delete env.ANTIPHON_ORCHESTRATOR;
    Object.assign(env, extraEnv);
    const child = spawn(process.execPath, [wrapper], {
      env,
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    });
    let stdout = '';
    let stderr = '';
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (c) => { stdout += c; });
    child.stderr.on('data', (c) => { stderr += c; });
    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`wrapper hung after 5s. stdout=${stdout} stderr=${stderr}`));
    }, 5000);
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code, stdout, stderr });
    });
    child.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
    child.stdin.end(stdin);
  });
}
