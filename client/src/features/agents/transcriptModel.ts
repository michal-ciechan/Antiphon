import type { TranscriptEntryDto } from '../../api/sessions'

// Pure transcript model: turn grouping, working/idle, per-turn metrics, merge and formatting.
// Lives outside SessionTranscriptPanel.tsx so the component file only exports components
// (react-refresh); the tests import from here. isWorking and its exclusion helpers are one of the
// THREE lockstep working-rule implementations (server IsWorkingAsync, this, runner
// TranscriptWorkingState) — change them together or not at all.

export interface Turn {
  key: string
  prompt?: TranscriptEntryDto
  title?: string
  items: TranscriptEntryDto[] // thinking / tool calls / assistant text, in order
  ended?: TranscriptEntryDto
}

// Group the flat entry stream into turns. A new turn starts at each user prompt; tool results are
// folded into their originating tool call (matched by toolUseId) at render time.
// Exported for tests.
export function buildTurns(entries: TranscriptEntryDto[]): Turn[] {
  const turns: Turn[] = []
  let current: Turn | null = null

  for (const e of entries) {
    if (e.kind === 'UserPrompt') {
      // The interrupt marker is the END of the running turn (no TurnEnd is ever written for it —
      // mirror of the server's IsWorkingAsync), not a new prompt.
      if (isInterruptPrompt(e) && current && !current.ended) {
        current.ended = e
        continue
      }
      current = { key: `turn-${e.sequence}`, prompt: e, items: [] }
      turns.push(current)
      continue
    }
    if (!current) {
      current = { key: 'turn-pre', items: [] }
      turns.push(current)
    }
    if (e.kind === 'TurnTitle') {
      if (!current.title) current.title = e.text ?? undefined
      continue
    }
    if (e.kind === 'TurnEnd') {
      current.ended = e
      continue
    }
    current.items.push(e)
  }

  return turns
}

// Claude writes this as a USER message when a turn is aborted (Esc / rejected tool call); such a
// turn produces NO TurnEnd, so the marker IS its end (mirror of the server's IsWorkingAsync).
const INTERRUPTED_PREFIX = '[Request interrupted'

export function isInterruptPrompt(e: TranscriptEntryDto): boolean {
  return e.kind === 'UserPrompt' && (e.text ?? '').trimStart().startsWith(INTERRUPTED_PREFIX)
}

// Local slash-commands (/model, /clear, /status …) write their invocation + output into the JSONL
// as USER messages wrapped in these tags, with NO TurnEnd (no API call happens). Housekeeping, not
// work — counting them as activity showed a phantom permanently-working agent and stranded
// WhenIdle deliveries (live miss 2026-07-31; mirror of the server's IsWorkingAsync).
function isLocalCommandRecord(e: TranscriptEntryDto): boolean {
  if (e.kind !== 'UserPrompt') return false
  const t = (e.text ?? '').trimStart()
  return t.startsWith('<command-name>') || t.startsWith('<local-command-stdout>')
}

// A MANUAL /compact runs only BETWEEN turns, so its boundary is the previous turn's end — nothing
// else ever will be, since compaction makes no API call (live miss 2026-08-11, CARD-0041). An AUTO
// boundary fires mid-turn and stays housekeeping. Mirror of TranscriptKinds.ManualCompactMarker /
// CompactionContinuationPromptPrefix; the continuation prompt is compaction's own synthetic USER
// record — nobody typed it, and counting it as activity read "working" forever.
const MANUAL_COMPACT_MARKER = '(manual)'
const CONTINUATION_PREFIX = 'This session is being continued from a previous conversation'

function isManualCompactBoundary(e: TranscriptEntryDto): boolean {
  return e.kind === 'CompactBoundary' && (e.text ?? '').includes(MANUAL_COMPACT_MARKER)
}

function isCompactionContinuation(e: TranscriptEntryDto): boolean {
  return e.kind === 'UserPrompt' && (e.text ?? '').trimStart().startsWith(CONTINUATION_PREFIX)
}

// Idle once the latest meaningful entry is a TurnEnd; working while activity outranks the last end.
// CompactBoundary is idle-time housekeeping, not activity (mirror of the server's IsWorkingAsync —
// counting it would show a phantom "working" agent after every compaction), and a MANUAL one is a
// turn END. Interrupt markers are turn ENDS — counting them as activity showed a phantom "working"
// agent forever after an interrupt.
// SessionRestartBoundary (server-synthesized on relaunch of a mid-turn transcript) is a turn END too.
// Exported for tests: the exclusion list must stay in lockstep with the server.
export function isWorking(entries: TranscriptEntryDto[]): boolean {
  let lastActivitySeq = 0
  let lastEndSeq = 0
  let lastActivityTs: number | null = null
  let lastEndTs: number | null = null
  for (const e of entries) {
    const t = e.timestamp ? Date.parse(e.timestamp) : null
    if (
      e.kind === 'TurnEnd' ||
      e.kind === 'SessionRestartBoundary' ||
      isManualCompactBoundary(e) ||
      isInterruptPrompt(e)
    ) {
      lastEndSeq = Math.max(lastEndSeq, e.sequence)
      if (t !== null) lastEndTs = lastEndTs === null ? t : Math.max(lastEndTs, t)
    } else if (
      e.kind !== 'TurnTitle' &&
      e.kind !== 'CompactBoundary' &&
      !isCompactionContinuation(e) &&
      !isLocalCommandRecord(e)
    ) {
      lastActivitySeq = Math.max(lastActivitySeq, e.sequence)
      if (t !== null) lastActivityTs = lastActivityTs === null ? t : Math.max(lastActivityTs, t)
    }
  }
  if (lastActivitySeq <= lastEndSeq) return false
  // Sequences are ARRIVAL-ordered: a catch-up sync can backfill stale pre-gap activity ABOVE an
  // already-persisted TurnEnd (mirror of the server's IsWorkingAsync; live miss 2026-08-08).
  // Record timestamps survive that reordering — when they prove all activity predates the last
  // end, the session is idle. Equal timestamps keep the sequence verdict.
  if (lastActivityTs !== null && lastEndTs !== null && lastActivityTs < lastEndTs) return false
  return true
}

/** Token totals + wall-clock for one turn, plus the idle gap since the previous turn ended. */
export interface TurnMetrics {
  /** Distinct API calls in the turn (assistant entries grouped by apiCallId). */
  apiCalls: number
  inputTokens: number
  outputTokens: number
  cacheReadTokens: number
  cacheCreationTokens: number
  /** Prompt → turn end (ms); null when timestamps are missing or the turn is still running. */
  durationMs: number | null
  /** Time the session sat idle between the previous turn's end and this prompt (ms). */
  idleBeforeMs: number | null
}

export const ts = (e: TranscriptEntryDto | undefined): number | null =>
  e?.timestamp ? Date.parse(e.timestamp) : null

/**
 * Compute per-turn metrics. Entries of one API call share apiCallId and repeat IDENTICAL usage,
 * so usage is counted ONCE per distinct apiCallId — summing per entry would overcount.
 * Exported for tests.
 */
export function computeTurnMetrics(turns: Turn[]): TurnMetrics[] {
  let prevEnd: number | null = null
  return turns.map((turn) => {
    const seen = new Set<string>()
    let input = 0
    let output = 0
    let cacheRead = 0
    let cacheCreate = 0
    const all = [...turn.items, ...(turn.ended ? [turn.ended] : [])]
    for (const e of all) {
      if (!e.apiCallId || seen.has(e.apiCallId)) continue
      seen.add(e.apiCallId)
      input += e.inputTokens ?? 0
      output += e.outputTokens ?? 0
      cacheRead += e.cacheReadTokens ?? 0
      cacheCreate += e.cacheCreationTokens ?? 0
    }

    const start = ts(turn.prompt) ?? ts(all[0])
    const end = ts(turn.ended)
    const durationMs = start != null && end != null && end >= start ? end - start : null
    const idleBeforeMs =
      prevEnd != null && start != null && start >= prevEnd ? start - prevEnd : null
    prevEnd = end

    return {
      apiCalls: seen.size,
      inputTokens: input,
      outputTokens: output,
      cacheReadTokens: cacheRead,
      cacheCreationTokens: cacheCreate,
      durationMs,
      idleBeforeMs,
    }
  })
}

/**
 * Merge incoming transcript entries into the current list. Dedup is by LINE UUID (+kind), never by
 * sequence, and live sequences are REBASED past the loaded max — the mirror of the server's
 * persistence dedup, and for the same reason: live SignalR payloads carry the runner tailer's
 * sequence, which restarts at 1 on every session relaunch / re-tail, while the HTTP backlog
 * carries rebased stored sequences. Sequence-dedup silently dropped every live entry for a session
 * with prior relaunches (live miss 2026-07-30: the full-screen files dock never showed new turns),
 * and without the rebase a colliding live sequence sorts into the middle of history.
 * Returns the new sorted list, or null when nothing was new. Exported for tests.
 */
export function mergeTranscriptEntries(
  prev: TranscriptEntryDto[],
  incoming: TranscriptEntryDto[],
  seen: Set<string>,
  counter: { maxSeq: number },
  rebaseLive: boolean,
): TranscriptEntryDto[] | null {
  const keyOf = (e: TranscriptEntryDto) => (e.uuid ? `${e.uuid}:${e.kind}` : `seq:${e.sequence}`)
  const fresh: TranscriptEntryDto[] = []
  for (const e of incoming) {
    const key = keyOf(e)
    if (seen.has(key)) continue
    seen.add(key)
    const seq = rebaseLive && e.sequence <= counter.maxSeq ? counter.maxSeq + 1 : e.sequence
    counter.maxSeq = Math.max(counter.maxSeq, seq)
    fresh.push(seq === e.sequence ? e : { ...e, sequence: seq })
  }
  if (fresh.length === 0) return null
  return [...prev, ...fresh].sort((a, b) => a.sequence - b.sequence)
}

/** "850ms" / "12.4s" / "1m 05s" / "1h 02m". Exported for tests. */
export function formatDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)}s`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ${String(Math.round(s % 60)).padStart(2, '0')}s`
  return `${Math.floor(m / 60)}h ${String(m % 60).padStart(2, '0')}m`
}

/** "412" / "1.2k" / "3.4M". Exported for tests. */
export function formatTokens(n: number): string {
  if (n < 1000) return String(n)
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`
  return `${(n / 1_000_000).toFixed(2)}M`
}
