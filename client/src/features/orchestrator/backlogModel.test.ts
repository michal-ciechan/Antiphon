import { describe, expect, it } from 'vitest'
import type { CardDto } from '../../api/boards'
import { QUADRANT_ORDER } from '../board/cardRanking'
import { BACKLOG_BOX_CAP, boardsPresent, groupBacklog, QUADRANT_HINTS } from './backlogModel'

function card(overrides: Partial<CardDto> = {}): CardDto {
  return {
    id: 'card-1',
    boardId: 'board-1',
    boardColumnId: 'column-backlog',
    ownerSessionId: null,
    currentWorktreeId: null,
    assignedAgentId: null,
    assignedAgentName: null,
    agentQueuePosition: null,
    activeWorkflowRunId: null,
    workflowRunStatus: null,
    currentWorkflowStageName: null,
    identifier: 'CARD-0001',
    title: 'A backlog card',
    description: '',
    importance: 'Normal',
    urgency: 'Normal',
    dueAt: null,
    urgentSince: null,
    effectiveUrgency: 'Normal',
    quadrant: 'Someday',
    rank: 10,
    labels: [],
    status: 'Backlog',
    concurrencyToken: 'token-1',
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    startedAt: null,
    completedAt: null,
    terminalReason: null,
    sessions: [],
    revisionCount: 0,
    archivedAt: null,
    archivedReason: null,
    archivedBy: null,
    ...overrides,
  }
}

describe('groupBacklog', () => {
  it('returns all four cells in QUADRANT_ORDER even when nothing is in them', () => {
    const boxes = groupBacklog([])
    expect(boxes.map((box) => box.quadrant)).toEqual([...QUADRANT_ORDER])
    expect(boxes.every((box) => box.cards.length === 0)).toBe(true)
    expect(boxes.map((box) => box.hint)).toEqual(QUADRANT_ORDER.map((cell) => QUADRANT_HINTS[cell]))
  })

  it('keeps an empty cell as [] rather than dropping it', () => {
    const boxes = groupBacklog([
      card({ id: 's1', quadrant: 'Schedule', rank: 7 }),
    ])
    expect(boxes.find((box) => box.quadrant === 'DoFirst')?.cards).toEqual([])
    expect(boxes.find((box) => box.quadrant === 'Clear')?.cards).toEqual([])
    expect(boxes.find((box) => box.quadrant === 'Someday')?.cards).toEqual([])
    expect(boxes.find((box) => box.quadrant === 'Schedule')?.cards).toHaveLength(1)
  })

  it('orders a cell by rank, then earliest dueAt, then oldest createdAt', () => {
    const boxes = groupBacklog([
      card({ id: 'late-rank', identifier: 'CARD-0013', quadrant: 'Someday', rank: 13, createdAt: '2026-01-01T00:00:00Z' }),
      card({ id: 'no-due', identifier: 'CARD-0010b', quadrant: 'Someday', rank: 10, dueAt: null, createdAt: '2026-01-01T00:00:00Z' }),
      card({ id: 'due-later', identifier: 'CARD-0010d', quadrant: 'Someday', rank: 10, dueAt: '2026-09-02T00:00:00Z', createdAt: '2026-01-02T00:00:00Z' }),
      card({ id: 'due-earlier', identifier: 'CARD-0010c', quadrant: 'Someday', rank: 10, dueAt: '2026-09-02T00:00:00Z', createdAt: '2026-01-01T00:00:00Z' }),
      card({ id: 'other-cell', identifier: 'CARD-0007', quadrant: 'Schedule', rank: 7 }),
    ])

    expect(boxes.find((box) => box.quadrant === 'Someday')?.cards.map((item) => item.id)).toEqual([
      'due-earlier',
      'due-later',
      'no-due',
      'late-rank',
    ])
    expect(boxes.find((box) => box.quadrant === 'Schedule')?.cards.map((item) => item.id)).toEqual(['other-cell'])
  })
})

describe('boardsPresent', () => {
  it('counts distinct board ids in the list', () => {
    expect(boardsPresent([])).toBe(0)
    expect(boardsPresent([card({ boardId: 'a' }), card({ id: 'c2', boardId: 'a' })])).toBe(1)
    expect(boardsPresent([card({ boardId: 'a' }), card({ id: 'c2', boardId: 'b' })])).toBe(2)
  })
})

describe('BACKLOG_BOX_CAP', () => {
  it('is twelve', () => {
    expect(BACKLOG_BOX_CAP).toBe(12)
  })
})
