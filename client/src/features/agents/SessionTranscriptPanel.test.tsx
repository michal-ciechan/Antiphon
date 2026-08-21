import { describe, expect, it } from 'vitest'
import type { TranscriptEntryDto } from '../../api/sessions'
import {
  buildTurns,
  computeTurnMetrics,
  formatDuration,
  formatTokens,
  isWorking,
  mergeTranscriptEntries,
} from './transcriptModel'

function entry(
  sequence: number,
  kind: string,
  text: string | null = null,
  extra: Partial<TranscriptEntryDto> = {},
): TranscriptEntryDto {
  return {
    sequence,
    kind,
    uuid: null,
    parentUuid: null,
    timestamp: null,
    role: null,
    text,
    toolName: null,
    toolInput: null,
    toolUseId: null,
    toolIsError: null,
    stopReason: kind === 'TurnEnd' ? 'end_turn' : null,
    ...extra,
  }
}

const at = (seconds: number) => new Date(Date.UTC(2026, 6, 30, 10, 0, seconds)).toISOString()

// Compaction's own synthetic USER record (CARD-0041); the full wording is pinned by the headed
// canary, the prefix is what the rule matches.
const CONTINUATION =
  'This session is being continued from a previous conversation that ran out of context. ' +
  'The conversation is summarized below:'

describe('isWorking', () => {
  it('reads working while activity outranks the last turn end', () => {
    expect(
      isWorking([entry(1, 'UserPrompt'), entry(2, 'AssistantText'), entry(3, 'TurnEnd'), entry(4, 'AssistantText')]),
    ).toBe(true)
  })

  it('reads idle once the last meaningful entry is a turn end', () => {
    expect(isWorking([entry(1, 'UserPrompt'), entry(2, 'AssistantText'), entry(3, 'TurnEnd')])).toBe(false)
  })

  // CARD-0006 pins this deliberately, because it looks like a bug and is not: a session with NO
  // transcript reads IDLE. Every launch depends on it — the boot prompt and launch note are queued
  // WhenIdle BEFORE any transcript exists — and since the runner now REFUSES to bind a transcript
  // it cannot prove is the session's, a transcript-less session is a state operators will actually
  // see. "Empty means working" would deadlock those sessions instead of merely degrading them.
  it('reads idle with no entries at all (a session whose transcript never bound)', () => {
    expect(isWorking([])).toBe(false)
  })

  // The PR 6 pair: a compaction after the last turn end must NOT read as working — the server's
  // IsWorkingAsync has the same exclusion, and both sides drifting apart shows phantom activity.
  it('ignores compact boundary entries (compaction is housekeeping, not work)', () => {
    expect(
      isWorking([entry(1, 'UserPrompt'), entry(2, 'AssistantText'), entry(3, 'TurnEnd'), entry(4, 'CompactBoundary')]),
    ).toBe(false)
  })

  it('still ignores turn titles', () => {
    expect(isWorking([entry(1, 'AssistantText'), entry(2, 'TurnEnd'), entry(3, 'TurnTitle')])).toBe(false)
  })

  it('ignores queued user prompts for working state', () => {
    expect(
      isWorking([
        entry(1, 'TurnEnd', null, { timestamp: at(10) }),
        entry(2, 'QueuedUserPrompt', 'a completion note accepted into the composer queue', { timestamp: at(20) }),
      ]),
    ).toBe(false)
  })

  // Live miss 2026-07-29: an interrupted turn (Esc / rejected tool call) writes the
  // "[Request interrupted..." USER marker and no TurnEnd — the marker is the turn's end. Counting
  // it as activity showed a phantom permanently-working agent and stranded WhenIdle deliveries.
  it('reads idle after an interrupt marker (aborted turns have no TurnEnd)', () => {
    expect(
      isWorking([
        entry(1, 'UserPrompt', 'do the thing'),
        entry(2, 'ToolCall'),
        entry(3, 'UserPrompt', '[Request interrupted by user for tool use]'),
      ]),
    ).toBe(false)
    expect(
      isWorking([entry(1, 'UserPrompt', 'count to 100'), entry(2, 'UserPrompt', '[Request interrupted by user]')]),
    ).toBe(false)
  })

  it('does not treat a real user message mentioning interruption as a turn end', () => {
    expect(
      isWorking([entry(1, 'TurnEnd'), entry(2, 'UserPrompt', 'why was my [Request interrupted] earlier?')]),
    ).toBe(true)
  })

  // Live miss 2026-07-31: /model (and /clear etc.) write <command-name>/<local-command-stdout>
  // USER records with NO TurnEnd — counting them as activity read "working" forever and stranded
  // a queued Telegram delivery. Must stay in lockstep with the server's IsWorkingAsync.
  it('ignores local slash-command records (/model, /clear) — housekeeping, not work', () => {
    expect(
      isWorking([
        entry(1, 'UserPrompt', 'hi'),
        entry(2, 'TurnEnd'),
        entry(3, 'UserPrompt', '<command-name>/model</command-name>\n<command-message>model</command-message>'),
        entry(4, 'UserPrompt', '<local-command-stdout>Set model to Opus 5</local-command-stdout>'),
      ]),
    ).toBe(false)
    expect(
      isWorking([entry(1, 'TurnEnd'), entry(2, 'UserPrompt', '<command-name>/clear</command-name>')]),
    ).toBe(false)
  })

  it('still treats a real prompt after a slash command as working', () => {
    expect(
      isWorking([
        entry(1, 'TurnEnd'),
        entry(2, 'UserPrompt', '<command-name>/clear</command-name>'),
        entry(3, 'UserPrompt', 'now do the thing'),
      ]),
    ).toBe(true)
  })

  // Live miss 2026-08-08: entries lost during a server restart were backfilled later and
  // sequence-rebased ABOVE the already-persisted TurnEnd — record timestamps prove they predate
  // it, so the session is idle. Must stay in lockstep with the server's IsWorkingAsync.
  it('reads idle when higher-sequence activity is older by timestamp than the last turn end', () => {
    expect(
      isWorking([
        entry(1, 'AssistantText', 'turn output', { timestamp: at(0) }),
        entry(2, 'TurnEnd', null, { timestamp: at(30) }),
        entry(3, 'ToolCall', 'backfilled', { timestamp: at(10) }),
        entry(4, 'ToolResult', 'backfilled', { timestamp: at(11) }),
      ]),
    ).toBe(false)
  })

  it('keeps reading working when the newest activity is genuinely newer than the last end', () => {
    expect(
      isWorking([
        entry(1, 'AssistantText', 'turn output', { timestamp: at(0) }),
        entry(2, 'TurnEnd', null, { timestamp: at(30) }),
        entry(3, 'UserPrompt', 'next piece of work', { timestamp: at(60) }),
      ]),
    ).toBe(true)
  })

  // Live miss 2026-08-11 (CARD-0041): a compacted session badged Working for two days. Two
  // post-compaction records escaped the exclusions — the RAW typed "/compact …" prompt (recorded
  // in addition to the <command-name> wrapper) and compaction's synthetic continuation prompt —
  // and no TurnEnd was ever coming. A MANUAL boundary is the turn's end; the continuation is not
  // activity. The real timestamps are non-monotonic (the boundary is stamped LATER than the
  // continuation that follows it), so the backfill override must not be what decides this.
  it('reads idle after a manual compaction (the real stored shape, real timestamps)', () => {
    expect(
      isWorking([
        entry(1, 'AssistantText', 'done', { timestamp: at(22) }),
        entry(2, 'TurnEnd', null, { timestamp: at(22) }),
        entry(3, 'UserPrompt', '/compact This session is being handed NEW work', { timestamp: at(430) }),
        entry(4, 'CompactBoundary', 'Context compacted (manual)', { timestamp: at(484) }),
        entry(5, 'UserPrompt', CONTINUATION, { timestamp: at(474) }),
        entry(6, 'UserPrompt', '<command-name>/compact</command-name>', { timestamp: at(430) }),
        entry(7, 'UserPrompt', '<local-command-stdout>Compacted</local-command-stdout>', { timestamp: at(484) }),
      ]),
    ).toBe(false)
  })

  it('ignores the compaction continuation prompt even when it is stamped after the boundary', () => {
    expect(
      isWorking([
        entry(1, 'TurnEnd', null, { timestamp: at(22) }),
        entry(2, 'CompactBoundary', 'Context compacted (manual)', { timestamp: at(484) }),
        entry(3, 'UserPrompt', CONTINUATION, { timestamp: at(510) }),
      ]),
    ).toBe(false)
  })

  // The deliberate non-exclusion: a prompt may legitimately begin with a slash, so raw text is
  // never matched — without a boundary to outrank it, it is real activity.
  it('still reads working on a raw /-prefixed prompt with no boundary', () => {
    expect(
      isWorking([entry(1, 'TurnEnd'), entry(2, 'UserPrompt', '/compact keep the API contract notes')]),
    ).toBe(true)
  })

  // Auto-compaction fires when a request starts over the context threshold — MID-turn. Treating
  // it as an end would badge a genuinely working agent idle.
  it('does not treat an auto compaction boundary as a turn end', () => {
    expect(
      isWorking([
        entry(1, 'TurnEnd', null, { timestamp: at(22) }),
        entry(2, 'UserPrompt', 'now do the big thing', { timestamp: at(60) }),
        entry(3, 'CompactBoundary', 'Context compacted (auto)', { timestamp: at(90) }),
        entry(4, 'UserPrompt', CONTINUATION, { timestamp: at(95) }),
      ]),
    ).toBe(true)
  })

  // A relaunch of a session whose process died mid-turn writes this server-synthesized record —
  // there is no TurnEnd and never will be, so the boundary IS the turn's end.
  it('reads idle after a session restart boundary', () => {
    expect(
      isWorking([
        entry(1, 'UserPrompt', 'do the thing', { timestamp: at(0) }),
        entry(2, 'ToolCall', null, { timestamp: at(1) }),
        entry(3, 'SessionRestartBoundary', 'Session relaunched', { timestamp: at(120) }),
      ]),
    ).toBe(false)
  })
})

describe('buildTurns', () => {
  it('folds the interrupt marker into the running turn as its end, not a new turn', () => {
    const turns = buildTurns([
      entry(1, 'UserPrompt', 'do the thing'),
      entry(2, 'ToolCall'),
      entry(3, 'UserPrompt', '[Request interrupted by user for tool use]'),
      entry(4, 'UserPrompt', 'ok try differently'),
    ])
    expect(turns).toHaveLength(2)
    expect(turns[0].ended?.text).toContain('[Request interrupted')
    expect(turns[1].prompt?.text).toBe('ok try differently')
  })
})

describe('computeTurnMetrics', () => {
  it('counts usage once per distinct apiCallId (lines of one call repeat identical usage)', () => {
    // One API call → thinking + 2 tool calls, all repeating the same usage, then a second call.
    const usage = { apiCallId: 'msg_1', inputTokens: 2, outputTokens: 372, cacheReadTokens: 100, cacheCreationTokens: 50 }
    const turns = buildTurns([
      entry(1, 'UserPrompt', 'go', { timestamp: at(0) }),
      entry(2, 'Thinking', 'hm', { ...usage, timestamp: at(2) }),
      entry(3, 'ToolCall', null, { ...usage, timestamp: at(2) }),
      entry(4, 'ToolCall', null, { ...usage, timestamp: at(2) }),
      entry(5, 'AssistantText', 'done', {
        apiCallId: 'msg_2',
        inputTokens: 5,
        outputTokens: 100,
        cacheReadTokens: 0,
        cacheCreationTokens: 0,
        timestamp: at(9),
      }),
      entry(6, 'TurnEnd', null, {
        apiCallId: 'msg_2',
        inputTokens: 5,
        outputTokens: 100,
        cacheReadTokens: 0,
        cacheCreationTokens: 0,
        timestamp: at(10),
      }),
    ])
    const [m] = computeTurnMetrics(turns)
    expect(m.apiCalls).toBe(2)
    expect(m.inputTokens).toBe(7)
    expect(m.outputTokens).toBe(472)
    expect(m.cacheReadTokens).toBe(100)
    expect(m.cacheCreationTokens).toBe(50)
    expect(m.durationMs).toBe(10_000)
  })

  it('computes the idle gap between the previous turn end and the next prompt', () => {
    const turns = buildTurns([
      entry(1, 'UserPrompt', 'one', { timestamp: at(0) }),
      entry(2, 'TurnEnd', null, { timestamp: at(5) }),
      entry(3, 'UserPrompt', 'two', { timestamp: at(65) }),
      entry(4, 'TurnEnd', null, { timestamp: at(70) }),
    ])
    const metrics = computeTurnMetrics(turns)
    expect(metrics[0].idleBeforeMs).toBeNull()
    expect(metrics[1].idleBeforeMs).toBe(60_000)
    expect(metrics[1].durationMs).toBe(5_000)
  })

  it('uses the interrupt marker timestamp as the end of an interrupted turn', () => {
    const turns = buildTurns([
      entry(1, 'UserPrompt', 'go', { timestamp: at(0) }),
      entry(2, 'ToolCall', null, { timestamp: at(3) }),
      entry(3, 'UserPrompt', '[Request interrupted by user]', { timestamp: at(8) }),
    ])
    const [m] = computeTurnMetrics(turns)
    expect(m.durationMs).toBe(8_000)
  })

  it('leaves duration null while a turn is still running', () => {
    const turns = buildTurns([
      entry(1, 'UserPrompt', 'go', { timestamp: at(0) }),
      entry(2, 'ToolCall', null, { timestamp: at(3) }),
    ])
    expect(computeTurnMetrics(turns)[0].durationMs).toBeNull()
  })
})

describe('mergeTranscriptEntries', () => {
  // The live-miss scenario (2026-07-30): a session with prior relaunches has stored sequences far
  // ahead of the runner tailer's per-lifetime numbering, so live payload sequences COLLIDE with
  // already-loaded ones. Dedup is by line uuid; colliding live sequences are rebased past the max.
  it('accepts a live entry whose runner sequence collides with a loaded stored sequence', () => {
    const seen = new Set<string>()
    const counter = { maxSeq: 0 }
    const backlog = mergeTranscriptEntries(
      [],
      [
        entry(240, 'UserPrompt', 'old', { uuid: 'u-old-1' }),
        entry(241, 'TurnEnd', null, { uuid: 'u-old-2' }),
      ],
      seen,
      counter,
      false,
    )!
    // Live push: the runner's current tailer generation numbers this line 180 — already "seen" as
    // a sequence, but a brand-new line uuid. It must append AFTER the backlog, not vanish.
    const next = mergeTranscriptEntries(
      backlog,
      [entry(180, 'UserPrompt', 'new prompt', { uuid: 'u-new-1' })],
      seen,
      counter,
      true,
    )
    expect(next).not.toBeNull()
    expect(next!.map((e) => e.text)).toEqual(['old', null, 'new prompt'])
    expect(next![2].sequence).toBe(242)
  })

  it('drops entries whose uuid+kind was already merged and returns null when nothing is new', () => {
    const seen = new Set<string>()
    const counter = { maxSeq: 0 }
    const first = mergeTranscriptEntries(
      [],
      [entry(1, 'AssistantText', 'hi', { uuid: 'u1' })],
      seen,
      counter,
      false,
    )!
    expect(
      mergeTranscriptEntries(first, [entry(7, 'AssistantText', 'hi', { uuid: 'u1' })], seen, counter, true),
    ).toBeNull()
  })

  it('keeps distinct kinds from the same line uuid (text + turn end share a uuid)', () => {
    const seen = new Set<string>()
    const counter = { maxSeq: 0 }
    const next = mergeTranscriptEntries(
      [],
      [entry(1, 'AssistantText', 'done', { uuid: 'u1' }), entry(2, 'TurnEnd', null, { uuid: 'u1' })],
      seen,
      counter,
      false,
    )
    expect(next).toHaveLength(2)
  })
})

describe('formatters', () => {
  it('formats durations across magnitudes', () => {
    expect(formatDuration(850)).toBe('850ms')
    expect(formatDuration(12_400)).toBe('12.4s')
    expect(formatDuration(65_000)).toBe('1m 05s')
    expect(formatDuration(3_720_000)).toBe('1h 02m')
  })

  it('formats token counts', () => {
    expect(formatTokens(412)).toBe('412')
    expect(formatTokens(1_234)).toBe('1.2k')
    expect(formatTokens(3_400_000)).toBe('3.40M')
  })
})
