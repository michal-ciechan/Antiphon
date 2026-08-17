import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import type { CardDto } from '../../api/boards'
import {
  AWAY_LAST_SEEN_KEY,
  computeAwayDelta,
  firstSentence,
  readLastSeen,
  stampLastSeen,
} from './awayDelta'

const NOW = Date.parse('2026-08-17T12:00:00Z')
const LAST_SEEN = '2026-08-17T09:12:00Z'

function task(overrides: Partial<AgentTaskSummaryDto> = {}): AgentTaskSummaryDto {
  return {
    id: '11111111-0000-0000-0000-000000000001',
    rootTaskId: '11111111-0000-0000-0000-000000000001',
    parentTaskId: null,
    depth: 0,
    title: 'CARD-0067 - reply durability',
    kind: 'Worker',
    role: 'Code',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Succeeded',
    workspace: 'Shared',
    workingDirectory: 'C:\\src\\antiphon',
    repoPath: null,
    worktreePath: null,
    worktreeBranch: null,
    scopeGlob: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-17T08:00:00Z',
    dispatchedAt: '2026-08-17T08:00:05Z',
    completedAt: '2026-08-17T10:30:00Z',
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 0.5,
    costPricingVersion: 2,
    subtreeCostUsd: 2.75,
    childCount: 0,
    expectedDurationMinutes: 30,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
}

function card(overrides: Partial<CardDto> = {}): CardDto {
  return {
    id: 'c1',
    boardId: 'b1',
    boardColumnId: 'col1',
    ownerSessionId: null,
    currentWorktreeId: null,
    assignedAgentId: null,
    assignedAgentName: null,
    agentQueuePosition: null,
    activeWorkflowRunId: null,
    workflowRunStatus: null,
    currentWorkflowStageName: null,
    identifier: 'CARD-0067',
    title: 'reply durability',
    description: '',
    priority: 0,
    labels: [],
    status: 'Done',
    concurrencyToken: 'ct',
    createdAt: '2026-08-16T08:00:00Z',
    updatedAt: '2026-08-17T10:00:00Z',
    startedAt: null,
    completedAt: null,
    terminalReason: null,
    sessions: [],
    revisionCount: 1,
    archivedAt: null,
    archivedReason: null,
    archivedBy: null,
    ...overrides,
  }
}

describe('computeAwayDelta', () => {
  it('keeps only tasks that settled inside the window', () => {
    const delta = computeAwayDelta(
      [
        task({ id: 'in', completedAt: '2026-08-17T10:30:00Z' }),
        task({ id: 'before', completedAt: '2026-08-17T09:00:00Z' }),
        task({ id: 'running', status: 'Working', completedAt: null }),
        task({ id: 'never', completedAt: null, status: 'Canceled' }),
      ],
      [],
      LAST_SEEN,
      NOW,
    )
    expect(delta.settledTasks.map((t) => t.id)).toEqual(['in'])
    expect(delta.firstVisit).toBe(false)
    expect(delta.sinceUtc).toBe(new Date(Date.parse(LAST_SEEN)).toISOString())
  })

  it('excludes check-interpretation tasks — machinery, not delegated work', () => {
    const delta = computeAwayDelta(
      [task({ id: 'check', role: 'Check' }), task({ id: 'real' })],
      [],
      LAST_SEEN,
      NOW,
    )
    expect(delta.settledTasks.map((t) => t.id)).toEqual(['real'])
  })

  it('first visit falls back to a 24h window and says so', () => {
    const delta = computeAwayDelta(
      [
        task({ id: 'recent', completedAt: '2026-08-16T13:00:00Z' }),
        task({ id: 'stale', completedAt: '2026-08-16T11:00:00Z' }),
      ],
      [],
      null,
      NOW,
    )
    expect(delta.firstVisit).toBe(true)
    expect(delta.sinceUtc).toBe('2026-08-16T12:00:00.000Z')
    expect(delta.settledTasks.map((t) => t.id)).toEqual(['recent'])
  })

  it('sorts settled tasks newest first', () => {
    const delta = computeAwayDelta(
      [
        task({ id: 'older', completedAt: '2026-08-17T10:00:00Z' }),
        task({ id: 'newer', completedAt: '2026-08-17T11:00:00Z' }),
      ],
      [],
      LAST_SEEN,
      NOW,
    )
    expect(delta.settledTasks.map((t) => t.id)).toEqual(['newer', 'older'])
  })

  it('classifies card changes, and done beats started for a card that did both', () => {
    const delta = computeAwayDelta(
      [],
      [
        card({ id: 'both', startedAt: '2026-08-17T09:30:00Z', completedAt: '2026-08-17T11:00:00Z' }),
        card({ id: 'started', startedAt: '2026-08-17T10:00:00Z' }),
        card({ id: 'old', startedAt: '2026-08-17T08:00:00Z' }),
        card({ id: 'untouched' }),
      ],
      LAST_SEEN,
      NOW,
    )
    expect(delta.cardChanges.map((c) => [c.card.id, c.change])).toEqual([
      ['both', 'done'],
      ['started', 'started'],
    ])
  })

  it('sums the OWN cost of settled work — a parent and child both settling never double-count', () => {
    const delta = computeAwayDelta(
      [
        task({ id: 'parent', costUsd: 0.5, subtreeCostUsd: 2.75 }),
        task({ id: 'child', parentTaskId: 'parent', costUsd: 2.25, subtreeCostUsd: 2.25 }),
      ],
      [],
      LAST_SEEN,
      NOW,
    )
    expect(delta.settledSpendUsd).toBeCloseTo(2.75)
  })
})

describe('firstSentence', () => {
  it('takes the report’s outcome line, stopping at the first sentence end', () => {
    expect(
      firstSentence('Landed both slices; suite green. Details follow.\nMore prose here.'),
    ).toBe('Landed both slices; suite green.')
  })

  it('a line without terminal punctuation is used whole', () => {
    expect(firstSentence('Shipped the reply route\nrest of report')).toBe('Shipped the reply route')
  })

  it('caps runaway lines with an ellipsis', () => {
    const long = 'x'.repeat(200)
    const result = firstSentence(long)!
    expect(result.length).toBeLessThanOrEqual(140)
    expect(result.endsWith('…')).toBe(true)
  })

  it('null and blank reports yield null', () => {
    expect(firstSentence(null)).toBeNull()
    expect(firstSentence('   ')).toBeNull()
  })
})

describe('last-seen storage edge', () => {
  it('round-trips a stamp and rejects garbage', () => {
    const store = new Map<string, string>()
    const storage = {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, value),
    }
    expect(readLastSeen(storage)).toBeNull()
    stampLastSeen(storage, '2026-08-17T12:00:00.000Z')
    expect(store.get(AWAY_LAST_SEEN_KEY)).toBe('2026-08-17T12:00:00.000Z')
    expect(readLastSeen(storage)).toBe('2026-08-17T12:00:00.000Z')
    store.set(AWAY_LAST_SEEN_KEY, 'not-a-date')
    expect(readLastSeen(storage)).toBeNull()
  })

  it('never reaches for a clock of its own', () => {
    // The module is pure by construction: the caller passes `now`. This pin exists so a future
    // convenience defaulting to Date.now() inside the calculation fails loudly.
    const spy = vi.spyOn(Date, 'now')
    computeAwayDelta([task({})], [card({})], LAST_SEEN, NOW)
    expect(spy).not.toHaveBeenCalled()
    spy.mockRestore()
  })
})
