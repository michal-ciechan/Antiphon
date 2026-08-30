import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  classify,
  classifyCall,
  identifiersFromText,
  isSourcePath,
  R,
  N_REPORT,
  N_DISPATCH,
  NUDGE_CONTEXT,
} from '../orchestrator-investigation.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const card0246Tail = readFileSync(
  join(here, 'fixtures', 'cefed08a-card0246.jsonl'),
  'utf8',
);

const CARD0246 = {
  human: 'How’s it going? Maybe look into and fix 246',
  read1: 'toolu_01E7huJxoyU2upxTCfeX52wV', // grep AgentControlService.cs
  read2: 'toolu_014nNUGY1CqLwPSgDcFfU2mv', // Read AgentControlService.cs
  read3: 'toolu_01Gcqwng6fQ96CK7TbYsGoCo', // grep -rn server/  ← 3rd source read
  read4: 'toolu_018oHHZki6ivsFiSQRsYvv2Y', // malformed Read AgentTuiLaunchResolver
  read5: 'toolu_01EqRQuk2ojVbpTZVM2yZLot', // Read AgentTuiLaunchResolver
  git: 'toolu_018CdqGLaqBJPuruwUHKRgN4',
  read7: 'toolu_017CP8EXYM8VW9rFySF3oiGx',
  read8: 'toolu_01CzzFpnrhxEcoG6kR5hjZeK',
  read9: 'toolu_01NVmGKAdggBocKjNRTpmQ1r',
  read10: 'toolu_011h38GkMSAePmhyLeWZqWQW',
  edit: 'toolu_013TgzxVF8ENZk9Xh4Yh4cZ8',
};

describe('thresholds', () => {
  it('exports the plan constants, not magic numbers', () => {
    assert.equal(R, 3);
    assert.equal(N_REPORT, 25);
    assert.equal(N_DISPATCH, 10);
  });
});

describe('classifyCall / isSourcePath', () => {
  it('treats repo-relative and absolute Windows source paths as source reads', () => {
    assert.equal(isSourcePath('server/Application/Services/AgentControlService.cs'), true);
    assert.equal(
      isSourcePath('C:\\src\\Antiphon\\server\\Application\\Services\\AgentControlService.cs'),
      true,
    );
    assert.equal(isSourcePath('C:\\src\\Antiphon\\src\\Antiphon.Agents.Pty\\Foo.cs'), true);
    assert.equal(isSourcePath('C:\\src\\Antiphon\\docs\\orchestration-loop.md'), false);
    assert.equal(isSourcePath('C:\\src\\Antiphon\\.antiphon\\task.md'), false);
    assert.equal(
      isSourcePath('C:\\Users\\lndco\\AppData\\Local\\Temp\\claude\\C--src-Antiphon\\cefed08a\\scratchpad\\x.md'),
      false,
    );
  });

  it('counts grep -rn whose target is a source directory as a source read', () => {
    const r = classifyCall({
      tool_name: 'Bash',
      tool_input: {
        command: 'cd C:/src/Antiphon && grep -rn "class AgentLaunchResolution" server/ --include="*.cs"',
      },
    });
    assert.equal(r.isSourceRead, true);
  });

  it('never treats git as a source read', () => {
    const r = classifyCall({
      tool_name: 'Bash',
      tool_input: { command: 'cd C:/src/Antiphon && git status --short && git log -1 --oneline' },
    });
    assert.equal(r.isSourceRead, false);
  });

  it('never treats delegate.ps1 / card.ps1 / dotnet as source reads', () => {
    assert.equal(classifyCall({
      tool_name: 'PowerShell',
      tool_input: { command: 'pwsh -File scripts/delegate.ps1 -Role Debug -Title "x"' },
    }).kind, 'dispatch');
    assert.equal(classifyCall({
      tool_name: 'Bash',
      tool_input: { command: 'pwsh -File scripts/card.ps1 get CARD-0001' },
    }).isSourceRead, false);
    assert.equal(classifyCall({
      tool_name: 'Bash',
      tool_input: { command: 'dotnet build server/Antiphon.Server.csproj' },
    }).isSourceRead, false);
  });

  it('counts Read of a source file, including unparsed JSON that still names one', () => {
    assert.equal(classifyCall({
      tool_name: 'Read',
      tool_input: { file_path: 'C:\\src\\Antiphon\\server\\Application\\Services\\AgentControlService.cs' },
    }).isSourceRead, true);
    assert.equal(classifyCall({
      tool_name: 'Read',
      tool_input: {
        __unparsedToolInput: {
          raw: '{"file_path": "C:\\\\src\\\\Antiphon\\\\server\\\\Application\\\\Services\\\\AgentTuiLaunchResolver.cs", "offset": 30, 80}',
        },
      },
    }).isSourceRead, true);
  });
});

describe('identifiersFromText', () => {
  it('picks CARD-nnnn, paths, basenames and PascalCase class names', () => {
    const ids = identifiersFromText(
      'Fixed AgentControlService.cs (class AgentControlService) for CARD-0246',
    );
    assert.ok(ids.includes('CARD-0246'));
    assert.ok(ids.includes('AgentControlService.cs'));
    assert.ok(ids.includes('AgentControlService'));
  });
});

describe('cefed08a other real cold runs (R = 3)', () => {
  const cases = [
    ['cefed08a-cold-assignment-policy.jsonl', 'toolu_01Mu7xMDYdwvm5Uq5Ka4Yv5v'],
    ['cefed08a-cold-cleanup-script.jsonl', 'toolu_01AdWdX1SYaXH6NzD1qBrpP5'],
    ['cefed08a-cold-reply-service.jsonl', 'toolu_012Ak142DDtDJnNmibpBg4Lx'],
  ];
  for (const [file, id] of cases) {
    it(`nudges once at ${id} in ${file}`, () => {
      const tail = readFileSync(join(here, 'fixtures', file), 'utf8');
      const { decisions } = replay(tail);
      const nudges = decisions.filter((d) => d.result.nudged);
      assert.equal(nudges.length, 1, `${file} must nudge once`);
      assert.equal(nudges[0].toolUseId, id);
      assert.equal(nudges[0].result.runLength, 3);
    });
  }
});

describe('cefed08a CARD-0246 real tail (seq 25340–25364)', () => {
  // Plan S1 row named seq 25348 as "the third read" under the old census that
  // labelled grep -rn server/ as OTHER. §3.2 counts that grep as a source read,
  // so the third is seq 25346 (toolu_01Gcqwng6fQ96CK7TbYsGoCo). Exactly one
  // nudge either way; git at 25353 still must not fire.
  it('nudges exactly once, at the third source read (grep -rn server/), never on git', () => {
    const { decisions } = replay(card0246Tail);
    const byId = Object.fromEntries(decisions.map((d) => [d.toolUseId, d]));

    assert.equal(byId[CARD0246.read1].result.nudged, false, 'read 1');
    assert.equal(byId[CARD0246.read1].result.reason, 'run-too-short');
    assert.equal(byId[CARD0246.read2].result.nudged, false, 'read 2');
    assert.equal(byId[CARD0246.read2].result.runLength, 2);

    const third = byId[CARD0246.read3];
    assert.equal(third.call.isSourceRead, true, 'grep -rn server/ is a source read');
    assert.equal(third.result.nudged, true, 'third source read must nudge');
    assert.equal(third.result.runLength, 3);
    assert.equal(third.result.hookOutput?.hookSpecificOutput?.permissionDecision, 'allow');
    assert.equal(third.result.hookOutput?.hookSpecificOutput?.hookEventName, 'PreToolUse');
    assert.equal(third.result.hookOutput?.hookSpecificOutput?.additionalContext, NUDGE_CONTEXT);

    assert.equal(byId[CARD0246.read4].result.nudged, false, '4th already nudged');
    assert.equal(byId[CARD0246.read4].result.reason, 'already-nudged');
    assert.equal(byId[CARD0246.read5].result.nudged, false);
    assert.equal(byId[CARD0246.git].call.isSourceRead, false, 'git is not a source read');
    assert.equal(byId[CARD0246.git].result.nudged, false);
    assert.equal(byId[CARD0246.git].result.reason, 'not-source-read');
    assert.equal(byId[CARD0246.read7].result.nudged, false, 'post-git read, same run');
    assert.equal(byId[CARD0246.read7].result.reason, 'already-nudged');
    assert.equal(byId[CARD0246.read8].result.nudged, false);
    assert.equal(byId[CARD0246.read9].result.nudged, false);
    assert.equal(byId[CARD0246.read10].result.nudged, false);
    assert.equal(byId[CARD0246.edit].result.nudged, false);

    const nudges = decisions.filter((d) => d.result.nudged);
    assert.equal(nudges.length, 1, 'exactly one nudge in the CARD-0246 run');
    assert.equal(nudges[0].toolUseId, CARD0246.read3);
  });
});

describe('verification vs investigation', () => {
  it('does not nudge when the read names a file the last report named', () => {
    const tail = jsonl([
      user('[task abcdef12 done] Updated AgentControlService.cs and covered AgentControlServiceTests'),
      ...fillerTools(30, 'git status'),
      assistantRead('toolu_named_1', 'C:\\src\\Antiphon\\server\\Application\\Services\\AgentControlService.cs'),
      assistantRead('toolu_named_2', 'C:\\src\\Antiphon\\tests\\Antiphon.Tests\\Application\\AgentControlServiceTests.cs'),
      assistantRead('toolu_named_3', 'C:\\src\\Antiphon\\server\\Application\\Services\\AgentControlService.cs'),
    ]);
    const { decisions } = replay(tail);
    assert.ok(decisions.every((d) => !d.result.nudged));
    assert.ok(decisions.some((d) => d.result.reason === 'named-in-report'));
  });

  it('does not nudge on three unnamed source reads within N_report of a delegate report', () => {
    const tail = jsonl([
      user('[task abcdef12 done] shipped CARD-0110 S2 migrate-once template'),
      assistantRead('toolu_near_1', 'C:\\src\\Antiphon\\server\\Application\\Services\\UnrelatedAlpha.cs'),
      assistantRead('toolu_near_2', 'C:\\src\\Antiphon\\server\\Application\\Services\\UnrelatedBeta.cs'),
      assistantRead('toolu_near_3', 'C:\\src\\Antiphon\\server\\Application\\Services\\UnrelatedGamma.cs'),
    ]);
    const { decisions } = replay(tail);
    assert.equal(decisions.at(-1).result.nudged, false);
    assert.equal(decisions.at(-1).result.reason, 'recent-report');
  });

  it('does not nudge on three source reads within N_dispatch of a delegate.ps1 launch', () => {
    const tail = jsonl([
      assistantBash('toolu_disp', 'pwsh -File scripts/delegate.ps1 -Role Debug -Title "look at Foo"'),
      assistantRead('toolu_d1', 'C:\\src\\Antiphon\\server\\Application\\Services\\UnrelatedAlpha.cs'),
      assistantRead('toolu_d2', 'C:\\src\\Antiphon\\server\\Application\\Services\\UnrelatedBeta.cs'),
      assistantRead('toolu_d3', 'C:\\src\\Antiphon\\server\\Application\\Services\\UnrelatedGamma.cs'),
    ]);
    const { decisions } = replay(tail);
    assert.equal(decisions.at(-1).result.nudged, false);
    assert.equal(decisions.at(-1).result.reason, 'recent-dispatch');
  });

  it('nudges a synthetic cold run of exactly R source reads after a human prompt', () => {
    const tail = jsonl([
      user('Please look into why launches fail', { kind: 'human' }),
      assistantRead('toolu_c1', 'C:\\src\\Antiphon\\server\\Application\\Services\\Foo.cs'),
      assistantRead('toolu_c2', 'C:\\src\\Antiphon\\server\\Application\\Services\\Bar.cs'),
      assistantRead('toolu_c3', 'C:\\src\\Antiphon\\server\\Application\\Services\\Baz.cs'),
    ]);
    const { decisions } = replay(tail);
    assert.equal(decisions[0].result.nudged, false);
    assert.equal(decisions[1].result.nudged, false);
    assert.equal(decisions[2].result.nudged, true);
    assert.equal(decisions[2].result.runLength, 3);
  });

  it('does not nudge a subagent call (agent_id present)', () => {
    const r = classify(
      readInput('toolu_sub', 'C:\\src\\Antiphon\\server\\Foo.cs', { agent_id: 'abc123' }),
      '',
      null,
    );
    assert.equal(r.nudged, false);
    assert.equal(r.reason, 'subagent');
  });

  it('fail-open: malformed stdin or transcript never throws and never nudges', () => {
    assert.equal(classify(null, 'not-json\n{', null).nudged, false);
    assert.equal(classify(null, 'not-json\n{', null).reason, 'parse-error');
    assert.equal(classify({ tool_name: 'Read' }, '{not json', null).nudged, false);
  });
});

function replay(tail) {
  const events = [];
  const records = [];
  for (const line of tail.split('\n')) {
    if (!line.trim()) continue;
    let rec;
    try { rec = JSON.parse(line); } catch { continue; }
    records.push(rec);
    if (rec.type !== 'assistant' || !Array.isArray(rec.message?.content)) continue;
    for (const b of rec.message.content) {
      if (b?.type === 'tool_use') events.push({ rec, block: b, lineOffset: records.length });
    }
  }
  let state = null;
  const decisions = [];
  for (const ev of events) {
    const prefix = records.slice(0, ev.lineOffset).map((r) => JSON.stringify(r)).join('\n');
    const input = {
      session_id: 'cefed08a-fd4a-42a0-8c76-0fbf82cf6b20',
      tool_name: ev.block.name,
      tool_input: ev.block.input,
      tool_use_id: ev.block.id,
      hook_event_name: 'PreToolUse',
    };
    const result = classify(input, prefix, state);
    state = result.state;
    decisions.push({
      toolUseId: ev.block.id,
      toolName: ev.block.name,
      call: classifyCall(input),
      result,
    });
  }
  return { decisions, state };
}

function jsonl(rows) {
  return rows.map((r) => JSON.stringify(r)).join('\n') + '\n';
}

function user(content, origin) {
  const rec = { type: 'user', message: { role: 'user', content } };
  if (origin) rec.origin = origin;
  return rec;
}

function assistantRead(id, filePath) {
  return {
    type: 'assistant',
    message: {
      role: 'assistant',
      content: [{ type: 'tool_use', id, name: 'Read', input: { file_path: filePath } }],
    },
  };
}

function assistantBash(id, command) {
  return {
    type: 'assistant',
    message: {
      role: 'assistant',
      content: [{ type: 'tool_use', id, name: 'Bash', input: { command } }],
    },
  };
}

function fillerTools(n, command) {
  const rows = [];
  for (let i = 0; i < n; i++) {
    rows.push(assistantBash(`toolu_fill_${i}`, command));
  }
  return rows;
}

function readInput(id, filePath, extra = {}) {
  return {
    session_id: 'test',
    tool_name: 'Read',
    tool_input: { file_path: filePath },
    tool_use_id: id,
    hook_event_name: 'PreToolUse',
    ...extra,
  };
}
