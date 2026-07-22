import { describe, expect, it } from 'vitest'
import type { TranscriptEntryDto } from '../../api/sessions'
import { isWorking } from './SessionTranscriptPanel'

function entry(sequence: number, kind: string): TranscriptEntryDto {
  return {
    sequence,
    kind,
    uuid: null,
    parentUuid: null,
    timestamp: null,
    role: null,
    text: null,
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
})
