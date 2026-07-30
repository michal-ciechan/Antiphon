import { describe, expect, it } from 'vitest'
import type { TranscriptEntryDto } from '../../api/sessions'
import {
  buildTurns,
  computeTurnMetrics,
  formatDuration,
  formatTokens,
  isWorking,
  mergeTranscriptEntries,
} from './SessionTranscriptPanel'

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

describe('isWorking', () => {
  it('reads working while activity outranks the last turn end', () => {
    expect(
      isWorking([entry(1, 'UserPrompt'), entry(2, 'AssistantText'), entry(3, 'TurnEnd'), entry(4, 'AssistantText')]),
    ).toBe(true)
  })

  it('reads idle once the last meaningful entry is a turn end', () => {
    expect(isWorking([entry(1, 'UserPrompt'), entry(2, 'AssistantText'), entry(3, 'TurnEnd')])).toBe(false)
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
