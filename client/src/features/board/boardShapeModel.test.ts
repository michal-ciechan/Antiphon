import { describe, expect, it } from 'vitest'
import type { BoardColumnDto, BoardDetailDto, CardDto } from '../../api/boards'
import snapshot from '../../test/fixtures/antiphon-board-2026-08-13.json'
import {
  BAND_THRESHOLD,
  EMPTY_FILTER,
  buildBoardShape,
  canMoveTo,
  canReopenFrom,
  cardMatchesFilter,
  defaultExpandedState,
  describeSignal,
  hasLiveSession,
  legalMoveTargets,
  priorityBands,
  spineNeighbours,
} from './boardShapeModel'

// The Antiphon board exactly as fetched on 2026-08-13 (descriptions truncated to 160 chars to
// keep the fixture readable; nothing else altered): 45 cards — Backlog 31, In Progress 1,
// Review 0, Done 13. The empty state and the 31-card state are the board, not hypotheticals.
const board = snapshot as unknown as BoardDetailDto
const NOW = new Date('2026-08-13T12:00:00Z')

function state(shape: ReturnType<typeof buildBoardShape>, key: string) {
  const found = shape.states.find((item) => item.stateKey === key)
  if (!found) throw new Error(`no state ${key}`)
  return found
}

describe('buildBoardShape — the real board', () => {
  const shape = buildBoardShape(board, EMPTY_FILTER, NOW)

  it('counts every state, including the empty one', () => {
    expect(shape.states.map((item) => [item.stateKey, item.filteredCount])).toEqual([
      ['backlog', 31],
      ['in-progress', 1],
      ['review', 0],
      ['done', 13],
    ])
    expect(shape.totalCount).toBe(45)
  })

  it('renders the priority mix within each state', () => {
    expect(state(shape, 'backlog').priorityMix).toEqual([8, 10, 11, 2])
    expect(state(shape, 'backlog').p0Count).toBe(8)
    expect(state(shape, 'done').priorityMix).toEqual([9, 3, 1])
    expect(state(shape, 'review').priorityMix).toEqual([])
  })

  it('reports the OLDEST card by creation, which is card age and not time in state', () => {
    expect(state(shape, 'backlog').oldest?.identifier).toBe('CARD-0002')
    expect(state(shape, 'done').oldest?.identifier).toBe('CARD-0001')
    expect(state(shape, 'review').oldest).toBeNull()
  })

  it('orders a state P0 first, then oldest first', () => {
    const backlog = state(shape, 'backlog').cards
    expect(backlog[0].identifier).toBe('CARD-0019')
    expect(backlog.slice(0, 8).every((card) => card.priority === 0)).toBe(true)
    for (let index = 1; index < backlog.length; index += 1) {
      expect(backlog[index - 1].priority).toBeLessThanOrEqual(backlog[index].priority)
    }
  })

  it('collects the board label vocabulary', () => {
    expect(shape.labels).toHaveLength(50)
    expect(shape.labels).toContain('ux')
    expect(shape.labels[0] < shape.labels[1]).toBe(true)
  })

  it('derives one signal line per state by rule', () => {
    expect(describeSignal(state(shape, 'review').signal)).toBe('—')
    expect(describeSignal(state(shape, 'backlog').signal)).toBe('8 P0 · oldest #2 · 3d')
    expect(describeSignal(state(shape, 'in-progress').signal)).toBe('oldest #42 · 0d')
    expect(describeSignal(state(shape, 'done').signal)).toBe('last closed 2026-08-13')
  })

  it('sees no live session anywhere — every session on this board has failed or stopped', () => {
    expect(shape.states.every((item) => item.liveSessionCount === 0)).toBe(true)
  })
})

describe('buildBoardShape — under filters', () => {
  it('keeps every state rendered and reports n of m', () => {
    const shape = buildBoardShape(board, { ...EMPTY_FILTER, query: 'pty' }, NOW)

    expect(shape.states).toHaveLength(4)
    expect(shape.filteredCount).toBe(11)
    expect([state(shape, 'backlog').filteredCount, state(shape, 'backlog').totalCount]).toEqual([5, 31])
    expect([state(shape, 'in-progress').filteredCount, state(shape, 'in-progress').totalCount]).toEqual([0, 1])
    expect([state(shape, 'done').filteredCount, state(shape, 'done').totalCount]).toEqual([6, 13])
  })

  it('a filtered-empty state signals empty, not stale', () => {
    const shape = buildBoardShape(board, { ...EMPTY_FILTER, query: 'pty' }, NOW)
    expect(describeSignal(state(shape, 'in-progress').signal)).toBe('—')
  })

  it('filters by priority and by label (AND)', () => {
    const p0 = buildBoardShape(board, { ...EMPTY_FILTER, priorities: [0] }, NOW)
    expect(p0.filteredCount).toBe(17)
    expect(state(p0, 'backlog').filteredCount).toBe(8)

    const ux = buildBoardShape(board, { ...EMPTY_FILTER, labels: ['ux'] }, NOW)
    expect(ux.filteredCount).toBe(8)

    const uxBoard = buildBoardShape(board, { ...EMPTY_FILTER, labels: ['ux', 'board'] }, NOW)
    expect(uxBoard.filteredCount).toBeLessThan(8)
  })

  it('finds a card by every form of its identifier', () => {
    for (const form of ['#42', '42', 'CARD-0042', 'card-42']) {
      const shape = buildBoardShape(board, { ...EMPTY_FILTER, query: form }, NOW)
      expect(state(shape, 'in-progress').cards.map((card) => card.identifier)).toEqual(['CARD-0042'])
    }
  })
})

describe('card matching', () => {
  const card: CardDto = {
    ...board.columns[0].cards[0],
    identifier: 'CARD-0007',
    title: 'Surface launch errors',
    description: 'The queue drops the reason',
    labels: ['reliability', 'ui'],
    priority: 1,
  }

  it('matches title, description and labels case-insensitively', () => {
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, query: 'LAUNCH' })).toBe(true)
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, query: 'drops the reason' })).toBe(true)
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, query: 'reliab' })).toBe(true)
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, query: 'nowhere' })).toBe(false)
  })

  it('combines filters conjunctively', () => {
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, query: 'launch', priorities: [0] })).toBe(false)
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, query: 'launch', priorities: [1] })).toBe(true)
    expect(cardMatchesFilter(card, { ...EMPTY_FILTER, labels: ['ui', 'missing'] })).toBe(false)
  })
})

describe('live sessions', () => {
  const base = board.columns[0].cards[0]

  it('counts a running session as live', () => {
    const card: CardDto = {
      ...base,
      sessions: [{
        id: 'session-1',
        definitionName: 'claude',
        agentKind: 'ClaudeCode',
        status: 'Running',
        cwd: 'C:/src/Antiphon',
        createdAt: '2026-08-13T00:00:00Z',
        startedAt: '2026-08-13T00:00:00Z',
        lastSeenAt: '2026-08-13T00:00:00Z',
        endedAt: null,
        exitCode: null,
        failureReason: null,
      }],
    }
    expect(hasLiveSession(card)).toBe(true)

    const shape = buildBoardShape(
      { ...board, columns: [{ ...board.columns[0], cards: [card] } as BoardColumnDto] },
      EMPTY_FILTER,
      NOW,
    )
    expect(describeSignal(shape.states[0].signal)).toBe('● 1 running')
  })

  it('does not count a finished session, however it ended', () => {
    const failed = board.columns[3].cards.find((card) => card.sessions.length > 0)
    expect(failed).toBeDefined()
    expect(hasLiveSession(failed as CardDto)).toBe(false)
  })

  it('counts an owner claim whose session is not in the list', () => {
    expect(hasLiveSession({ ...base, ownerSessionId: 'ghost', sessions: [] })).toBe(true)
  })
})

describe('priority bands', () => {
  it('splits a 31-card state P0 first and drops empty bands', () => {
    const shape = buildBoardShape(board, EMPTY_FILTER, NOW)
    const backlog = state(shape, 'backlog')
    expect(backlog.filteredCount).toBeGreaterThan(BAND_THRESHOLD)

    const bands = priorityBands(backlog.cards)
    expect(bands.map((band) => [band.priority, band.cards.length])).toEqual([
      [0, 8], [1, 10], [2, 11], [3, 2],
    ])
    expect(priorityBands(state(shape, 'review').cards)).toEqual([])
  })
})

describe('default expansion', () => {
  it('opens the first non-empty non-terminal state after the first', () => {
    const shape = buildBoardShape(board, EMPTY_FILTER, NOW)
    expect(defaultExpandedState(shape.states)).toBe('in-progress')
  })

  it('falls back to the first populated state when nothing downstream has cards', () => {
    const backlogOnly = {
      ...board,
      columns: board.columns.map((column) =>
        column.stateKey === 'backlog' ? column : { ...column, cards: [] }),
    }
    const shape = buildBoardShape(backlogOnly, EMPTY_FILTER, NOW)
    expect(defaultExpandedState(shape.states)).toBe('backlog')
  })

  it('opens nothing on an empty board', () => {
    const empty = { ...board, columns: board.columns.map((column) => ({ ...column, cards: [] })) }
    expect(defaultExpandedState(buildBoardShape(empty, EMPTY_FILTER, NOW).states)).toBeNull()
  })
})

describe('legal move targets (lockstep with CardStateMachine)', () => {
  const backlogCard = board.columns[0].cards[0]
  const doneCard = board.columns[3].cards[0]

  it('offers every other state from a live state', () => {
    expect(legalMoveTargets(backlogCard, board.columns).map((column) => column.stateKey))
      .toEqual(['in-progress', 'review', 'done'])
  })

  it('offers nothing out of a terminal state', () => {
    expect(legalMoveTargets(doneCard, board.columns)).toEqual([])
    expect(canMoveTo('Done', 'Backlog')).toBe(false)
    expect(canMoveTo('Canceled', 'Review')).toBe(false)
    expect(canMoveTo('Review', 'Backlog')).toBe(true)
    expect(canMoveTo('Blocked', 'Done')).toBe(true)
  })

  // The case the lockstep claim above is easiest to break on: the server excludes self from every
  // row (`Without(self)`), so a same-status move is NOT legal. This board hides it - each column
  // carries a distinct status, so `legalMoveTargets`' column filter removes the only candidate -
  // hence the direct assertions, and a two-column board to prove the filter is not the guard.
  it('reopens only from Done or Canceled — every live status is refused', () => {
    expect(canReopenFrom('Done')).toBe(true)
    expect(canReopenFrom('Canceled')).toBe(true)
    expect(canReopenFrom('Backlog')).toBe(false)
    expect(canReopenFrom('InProgress')).toBe(false)
    expect(canReopenFrom('Review')).toBe(false)
    expect(canReopenFrom('Blocked')).toBe(false)
  })

  it('refuses a self-move, even between two columns sharing one status', () => {
    expect(canMoveTo('Backlog', 'Backlog')).toBe(false)
    expect(canMoveTo('InProgress', 'InProgress')).toBe(false)
    expect(canMoveTo('Done', 'Done')).toBe(false)

    const [triage, second] = [board.columns[0], { ...board.columns[1], cardStatus: 'Backlog' as const }]
    expect(legalMoveTargets(triage.cards[0], [triage, second]).map((column) => column.stateKey))
      .toEqual([])
  })
})

describe('spine neighbours', () => {
  const shape = buildBoardShape(board, EMPTY_FILTER, NOW)

  it('reports the states that feed a state and the ones it leads to', () => {
    const review = spineNeighbours(shape.states, 'review')
    expect(review.previous?.stateKey).toBe('in-progress')
    expect(review.next?.stateKey).toBe('done')
    expect(review.index).toBe(2)
  })

  it('has no neighbour past the ends of the spine', () => {
    expect(spineNeighbours(shape.states, 'backlog').previous).toBeNull()
    expect(spineNeighbours(shape.states, 'done').next).toBeNull()
    expect(spineNeighbours(shape.states, 'nope').current).toBeNull()
  })
})
