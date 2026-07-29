import { describe, expect, it } from 'vitest'
import type { TranscriptEntryDto } from '../../api/sessions'
import { isWorking } from './SessionTranscriptPanel'

function entry(sequence: number, kind: string, text: string | null = null): TranscriptEntryDto {
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
  }
}

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
