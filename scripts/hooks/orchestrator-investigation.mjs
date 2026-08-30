/**
 * CARD-0247 S1: pure classifier + transcript walker for orchestrator investigation.
 * No I/O. The hook wrapper (S2) reads stdin/transcript/state and calls classify().
 */

export const R = 3;
export const N_REPORT = 25;
export const N_DISPATCH = 10;

export const SOURCE_ROOTS = [
  'server/',
  'src/',
  'tests/',
  'scripts/',
  'client/src/',
  'Antiphon.AppHost/',
];

const EXCLUDED_PREFIXES = [
  'docs/',
  '.antiphon/',
  'scratchpad/',
  'memory/',
];

const READ_VERBS = new Set([
  'cat', 'grep', 'rg', 'head', 'tail', 'get-content', 'select-string', 'gc',
]);

const NEVER_READ_BINS = new Set([
  'git', 'dotnet', 'npm', 'npx', 'docker', 'psql', 'curl', 'wget',
]);

const SOURCE_ROOT_TOKENS = new Set([
  'server', 'src', 'tests', 'scripts', 'client/src', 'Antiphon.AppHost',
  './server', './src', './tests', './scripts', './client/src',
]);

export const NUDGE_CONTEXT =
  '[antiphon-orchestrator] This is the 3rd consecutive source read with no delegate dispatched and no report naming these files. Diagnosis is a Debug delegate, not an inline read: pwsh -NoProfile -File scripts/delegate.ps1 -Role Debug -Goal "…" — and take its answer. If you are verifying a delegate\'s named claim, carry on; this note will not repeat for this run.';

/**
 * @param {object} input hook stdin JSON (S0 contract)
 * @param {string} transcriptTail JSONL text, already read by the caller
 * @param {object|null} state previous classify() state, or null
 * @returns {{ nudged: boolean, reason: string, classification: string, runLength: number, hookOutput: object|null, state: object }}
 */
export function classify(input, transcriptTail, state) {
  const baseState = freezeState(state);
  const allowNothing = (reason, classification = 'other', runLength = 0, nextState = baseState) => ({
    nudged: false,
    reason,
    classification,
    runLength,
    hookOutput: null,
    state: nextState,
  });

  try {
    if (!input || typeof input !== 'object') {
      return allowNothing('parse-error');
    }
    if (nonEmpty(input.agent_id)) {
      return allowNothing('subagent', 'subagent');
    }

    const call = classifyCall(input);
    if (!call.isSourceRead) {
      return allowNothing('not-source-read', call.kind);
    }

    const events = parseTranscript(typeof transcriptTail === 'string' ? transcriptTail : '');
    const currentId = typeof input.tool_use_id === 'string' ? input.tool_use_id : null;
    let idx = currentId ? lastIndexByToolUseId(events, currentId) : -1;
    if (idx < 0) {
      events.push(toolEventFromInput(input, call));
      idx = events.length - 1;
    }

    const current = events[idx];
    const identifiers = current.identifiers ?? identifiersFromCall(input.tool_name, input.tool_input);

    let lastReport = null;
    let lastReportToolDistance = Infinity;
    let lastDispatch = null;
    let lastDispatchToolDistance = Infinity;
    let runLength = 1;
    let runStartedId = current.toolUseId ?? currentId;
    let toolsSeen = 0;
    let runOpen = true;

    for (let i = idx - 1; i >= 0; i--) {
      const ev = events[i];
      if (ev.type === 'source-read' || ev.type === 'other-tool' || ev.type === 'dispatch') {
        toolsSeen += 1;
      }
      if (ev.type === 'report' && !lastReport) {
        lastReport = ev;
        lastReportToolDistance = toolsSeen;
      }
      if (ev.type === 'dispatch' && !lastDispatch) {
        lastDispatch = ev;
        lastDispatchToolDistance = toolsSeen;
      }
      if (!runOpen) continue;
      if (ev.type === 'human' || ev.type === 'report' || ev.type === 'dispatch') {
        runOpen = false;
        continue;
      }
      if (ev.type === 'source-read') {
        runLength += 1;
        runStartedId = ev.toolUseId ?? runStartedId;
      }
    }

    const namedInReport = lastReport
      ? setsOverlap(identifiers, lastReport.identifiers ?? [])
      : false;

    if (namedInReport) {
      return allowNothing('named-in-report', 'verification', runLength, baseState);
    }
    if (lastReport && lastReportToolDistance <= N_REPORT) {
      return allowNothing('recent-report', 'verification', runLength, baseState);
    }
    if (lastDispatch && lastDispatchToolDistance <= N_DISPATCH) {
      return allowNothing('recent-dispatch', 'verification', runLength, baseState);
    }
    const stateForRun = {
      ...baseState,
      runStartedId,
      nudgedForRunStartedId: baseState.nudgedForRunStartedId === runStartedId
        ? baseState.nudgedForRunStartedId
        : null,
    };

    if (runLength < R) {
      return allowNothing('run-too-short', 'source-read', runLength, stateForRun);
    }

    const already = stateForRun.nudgedForRunStartedId
      && stateForRun.nudgedForRunStartedId === runStartedId;
    if (already) {
      return allowNothing('already-nudged', 'investigation', runLength, stateForRun);
    }

    return {
      nudged: true,
      reason: 'investigation',
      classification: 'investigation',
      runLength,
      hookOutput: {
        hookSpecificOutput: {
          hookEventName: 'PreToolUse',
          permissionDecision: 'allow',
          additionalContext: NUDGE_CONTEXT,
        },
      },
      state: {
        sessionId: input.session_id ?? baseState.sessionId ?? null,
        runStartedId,
        nudgedForRunStartedId: runStartedId,
      },
    };
  } catch {
    return allowNothing('parse-error');
  }
}

export function classifyCall(input) {
  const name = String(input?.tool_name ?? '');
  const rawInput = flattenToolInput(input?.tool_input);
  if (name === 'Read' || name === 'Grep' || name === 'Glob') {
    const pathish = toolPath(name, rawInput);
    if (isSourcePath(pathish) || (name !== 'Read' && commandNamesSource(pathish))) {
      return { isSourceRead: true, kind: 'source-read' };
    }
    return { isSourceRead: false, kind: 'other-tool' };
  }
  if (name === 'Bash' || name === 'PowerShell') {
    const command = shellCommand(rawInput);
    if (isDispatchCommand(command)) {
      return { isSourceRead: false, kind: 'dispatch' };
    }
    if (isNeverReadCommand(command)) {
      return { isSourceRead: false, kind: 'other-tool' };
    }
    if (isShellSourceRead(command)) {
      return { isSourceRead: true, kind: 'source-read' };
    }
    return { isSourceRead: false, kind: 'other-tool' };
  }
  if (name === 'Agent') {
    return { isSourceRead: false, kind: 'dispatch' };
  }
  return { isSourceRead: false, kind: 'other-tool' };
}

export function parseTranscript(tail) {
  const events = [];
  if (!tail) return events;
  for (const line of tail.split('\n')) {
    if (!line.trim()) continue;
    let rec;
    try { rec = JSON.parse(line); } catch { continue; }
    if (rec.type === 'assistant') {
      const content = rec.message?.content;
      if (!Array.isArray(content)) continue;
      for (const block of content) {
        if (!block || block.type !== 'tool_use') continue;
        const fakeInput = { tool_name: block.name, tool_input: block.input, tool_use_id: block.id };
        const call = classifyCall(fakeInput);
        const type = isDispatchTool(block) || call.kind === 'dispatch'
          ? 'dispatch'
          : call.isSourceRead ? 'source-read' : 'other-tool';
        events.push({
          type,
          toolUseId: block.id,
          toolName: block.name,
          identifiers: identifiersFromCall(block.name, block.input),
        });
      }
    } else if (rec.type === 'user') {
      if (isToolResultOnly(rec)) continue;
      const text = userText(rec);
      if (isReportText(text, rec)) {
        events.push({ type: 'report', text, identifiers: identifiersFromText(text) });
      } else if (isHumanPrompt(rec, text)) {
        events.push({ type: 'human', text });
      }
    }
  }
  return events;
}

function toolEventFromInput(input, call) {
  const type = call.kind === 'dispatch' ? 'dispatch'
    : call.isSourceRead ? 'source-read' : 'other-tool';
  return {
    type,
    toolUseId: input.tool_use_id ?? null,
    toolName: input.tool_name,
    identifiers: identifiersFromCall(input.tool_name, input.tool_input),
  };
}

function isDispatchTool(block) {
  if (block?.name === 'Agent') return true;
  if (block?.name === 'Bash' || block?.name === 'PowerShell') {
    return isDispatchCommand(shellCommand(flattenToolInput(block.input)));
  }
  return false;
}

function isDispatchCommand(command) {
  if (!command) return false;
  if (!/delegate\.ps1/i.test(command)) return false;
  return /(?:^|[\s])-(?:Goal|GoalFile|Reply|Refine|Title)\b/.test(command);
}

function isNeverReadCommand(command) {
  if (!command) return false;
  const segments = splitShell(command);
  let sawRead = false;
  let sawNever = false;
  for (const seg of segments) {
    const head = pipelineHead(seg);
    const bin = firstBin(head);
    if (!bin || bin === 'cd') continue;
    if (NEVER_READ_BINS.has(bin)) sawNever = true;
    else if (isReadVerb(bin, head) || /delegate\.ps1|card\.ps1/i.test(head)) {
      if (/delegate\.ps1|card\.ps1/i.test(head)) sawNever = true;
      else sawRead = true;
    }
  }
  if (sawRead) return false;
  return sawNever;
}

function isShellSourceRead(command) {
  if (!command) return false;
  if (/delegate\.ps1|card\.ps1/i.test(command) && !isReadVerbCommand(command)) return false;
  const segments = splitShell(command);
  for (const seg of segments) {
    const head = pipelineHead(seg);
    const bin = firstBin(head);
    if (!bin || bin === 'cd') continue;
    if (NEVER_READ_BINS.has(bin)) continue;
    if (/delegate\.ps1|card\.ps1/i.test(head)) continue;
    if (!isReadVerb(bin, head)) continue;
    if (commandNamesSource(head) || isRecursiveGrepOnSourceRoot(head)) return true;
  }
  return false;
}

function isReadVerbCommand(command) {
  const segments = splitShell(command);
  return segments.some((seg) => {
    const head = pipelineHead(seg);
    return isReadVerb(firstBin(head), head);
  });
}

function isReadVerb(bin, segment) {
  if (!bin) return false;
  if (bin === 'sed') return /(^|\s)-n(\s|$)/.test(segment);
  return READ_VERBS.has(bin);
}

function isRecursiveGrepOnSourceRoot(segment) {
  const bin = firstBin(segment);
  if (bin !== 'grep' && bin !== 'rg') return false;
  const recursive = /(^|\s)(-r|-rn|-rI|-R|--recursive)(\s|$)/.test(segment)
    || (bin === 'rg' && !/(^|\s)--no-ignore/.test(segment) && commandNamesSource(segment));
  if (!recursive && bin === 'grep') return false;
  return commandNamesSource(segment) || hasSourceRootToken(segment);
}

function hasSourceRootToken(text) {
  for (const tok of tokenize(text)) {
    const cleaned = stripQuotes(tok).replace(/\\/g, '/').replace(/\/+$/, '');
    if (SOURCE_ROOT_TOKENS.has(cleaned)) return true;
    if (SOURCE_ROOT_TOKENS.has(cleaned.replace(/^\.\//, ''))) return true;
  }
  return false;
}

function commandNamesSource(text) {
  if (!text) return false;
  if (hasSourceRootToken(text)) return true;
  for (const tok of tokenize(text)) {
    const cleaned = stripQuotes(tok);
    if (!cleaned) continue;
    if (isSourcePath(cleaned)) return true;
  }
  return false;
}

function splitShell(command) {
  return splitOutsideQuotes(command, /&&|\|\||;/).map((s) => s.trim()).filter(Boolean);
}

function pipelineHead(segment) {
  return splitOutsideQuotes(segment, /\|/)[0].trim();
}

function splitOutsideQuotes(text, sep) {
  const out = [];
  let buf = '';
  let quote = null;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quote) {
      buf += ch;
      if (ch === quote && text[i - 1] !== '\\') quote = null;
      continue;
    }
    if (ch === '"' || ch === "'") {
      quote = ch;
      buf += ch;
      continue;
    }
    const rest = text.slice(i);
    const m = rest.match(sep);
    if (m && m.index === 0) {
      out.push(buf);
      buf = '';
      i += m[0].length - 1;
      continue;
    }
    buf += ch;
  }
  if (buf) out.push(buf);
  return out;
}

function firstBin(segment) {
  const tokens = tokenize(segment);
  for (const t of tokens) {
    if (t.includes('=') && !t.startsWith('-')) continue;
    const raw = stripQuotes(t);
    const base = raw.replace(/^.*[/\\]/, '').replace(/\.exe$/i, '');
    return base.toLowerCase();
  }
  return '';
}

function tokenize(s) {
  const out = [];
  const re = /"[^"]*"|'[^']*'|\S+/g;
  let m;
  while ((m = re.exec(s))) out.push(m[0]);
  return out;
}

function stripQuotes(t) {
  if ((t.startsWith('"') && t.endsWith('"')) || (t.startsWith("'") && t.endsWith("'"))) {
    return t.slice(1, -1);
  }
  return t;
}

function flattenToolInput(toolInput) {
  if (!toolInput) return {};
  if (typeof toolInput === 'string') {
    try { return JSON.parse(toolInput); } catch { return { _raw: toolInput }; }
  }
  if (typeof toolInput === 'object' && toolInput.__unparsedToolInput?.raw) {
    const raw = toolInput.__unparsedToolInput.raw;
    try { return { ...JSON.parse(raw), _raw: raw }; } catch {
      const m = raw.match(/file_path["']?\s*:\s*["']([^"']+)["']/);
      return m ? { file_path: m[1].replace(/\\\\/g, '\\'), _raw: raw } : { _raw: raw };
    }
  }
  return toolInput;
}

function shellCommand(toolInput) {
  const obj = flattenToolInput(toolInput);
  if (typeof obj.command === 'string') return obj.command;
  if (typeof obj._raw === 'string') return obj._raw;
  return '';
}

function toolPath(toolName, toolInput) {
  const obj = flattenToolInput(toolInput);
  if (typeof obj.file_path === 'string') return obj.file_path;
  if (typeof obj.path === 'string') return obj.path;
  if (typeof obj.pattern === 'string') return obj.pattern;
  if (typeof obj.glob === 'string') return obj.glob;
  if (typeof obj._raw === 'string') return obj._raw;
  return JSON.stringify(obj);
}

export function isSourcePath(pathish) {
  if (!pathish || typeof pathish !== 'string') return false;
  const rel = repoRelative(pathish);
  const lower = rel.toLowerCase();
  if (!rel) return false;
  if (lower.includes('/scratchpad/') || lower.startsWith('scratchpad/')) return false;
  if (lower.includes('/.claude/') || lower.includes('.claude/')) return false;
  if (lower.includes('\\temp\\claude\\') || lower.includes('/temp/claude/')) return false;
  for (const ex of EXCLUDED_PREFIXES) {
    if (lower.startsWith(ex) || lower === ex.slice(0, -1)) return false;
  }
  for (const root of SOURCE_ROOTS) {
    if (rel.startsWith(root) || rel === root.slice(0, -1)) return true;
    if (lower.startsWith(root.toLowerCase())) return true;
  }
  return false;
}

function repoRelative(p) {
  const n = p.replace(/\\/g, '/').replace(/\/{2,}/g, '/');
  const lower = n.toLowerCase();
  const marker = '/antiphon/';
  const idx = lower.lastIndexOf(marker);
  const rel = idx >= 0 ? n.slice(idx + marker.length) : n.replace(/^\.\//, '');
  return rel.replace(/^\/+/, '');
}

function identifiersFromCall(toolName, toolInput) {
  const obj = flattenToolInput(toolInput);
  const parts = [];
  const command = shellCommand(obj);
  if (command) parts.push(command);
  const pathish = toolPath(toolName, obj);
  if (pathish) parts.push(pathish);
  return identifiersFromText(parts.join(' '));
}

export function identifiersFromText(text) {
  if (!text) return [];
  const found = new Set();
  const card = text.matchAll(/CARD-\d{4,}/gi);
  for (const m of card) found.add(m[0].toUpperCase());
  const paths = text.matchAll(
    /(?:server|src|tests|scripts|client\/src|Antiphon\.AppHost)[/\\][A-Za-z0-9_./\\-]+\.\w+/g,
  );
  for (const m of paths) {
    const norm = m[0].replace(/\\/g, '/');
    found.add(norm);
    const base = norm.split('/').pop();
    if (base) {
      found.add(base);
      found.add(base.replace(/\.[^.]+$/, ''));
    }
  }
  const bases = text.matchAll(/\b[A-Za-z0-9_.-]+\.(?:cs|ts|tsx|js|mjs|ps1|json)\b/g);
  for (const m of bases) {
    found.add(m[0]);
    found.add(m[0].replace(/\.[^.]+$/, ''));
  }
  const pascal = text.matchAll(/\b[A-Z][a-zA-Z0-9]{5,}(?:Tests)?\b/g);
  for (const m of pascal) found.add(m[0]);
  return [...found];
}

function setsOverlap(a, b) {
  if (!a?.length || !b?.length) return false;
  const B = new Set(b.map((x) => String(x).toLowerCase()));
  for (const x of a) {
    if (B.has(String(x).toLowerCase())) return true;
  }
  return false;
}

function isToolResultOnly(rec) {
  const content = rec?.message?.content;
  if (!Array.isArray(content) || content.length === 0) return false;
  return content.every((b) => b && (b.type === 'tool_result' || b.type === 'thinking'));
}

function userText(rec) {
  const content = rec?.message?.content;
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content.map((b) => (b?.type === 'text' ? (b.text || '') : '')).join('\n');
}

function isReportText(text, rec) {
  if (rec?.origin?.kind === 'task-notification' && /<tool-use-id>/.test(text)) {
    return false;
  }
  if (!text) return false;
  if (/\[task\s+[^\]]+\s+(done|blocked|failed)\]/i.test(text)) return true;
  if (/\[check\s+[^\]]+\s+#\d+\]/i.test(text)) return true;
  if (/<task-notification>/i.test(text) && !/<tool-use-id>/i.test(text)) return true;
  return false;
}

function isHumanPrompt(rec, text) {
  if (rec?.origin?.kind === 'human') return true;
  if (!text || typeof text !== 'string') return false;
  if (isReportText(text, rec)) return false;
  if (/^\s*<command-name>/i.test(text)) return false;
  if (/This session is being continued from a previous conversation/i.test(text)) return false;
  return true;
}

function lastIndexByToolUseId(events, id) {
  for (let i = events.length - 1; i >= 0; i--) {
    if (events[i].toolUseId === id) return i;
  }
  return -1;
}

function freezeState(state) {
  if (!state || typeof state !== 'object') {
    return { sessionId: null, runStartedId: null, nudgedForRunStartedId: null };
  }
  return {
    sessionId: state.sessionId ?? null,
    runStartedId: state.runStartedId ?? null,
    nudgedForRunStartedId: state.nudgedForRunStartedId ?? null,
  };
}

function nonEmpty(v) {
  return typeof v === 'string' && v.trim().length > 0;
}
