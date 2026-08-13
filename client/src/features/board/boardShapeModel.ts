import type { BoardColumnDto, BoardDetailDto, CardDto, CardStatus } from '../../api/boards'
import { displayIdentifier, matchesIdentifierQuery } from '../../shared/cardIdentifier'

/**
 * The pure core of the board state graph (feature 011 §5). Everything the strip, the list, the
 * mobile pager and the filters render is derived here from the `BoardDetailDto` the page already
 * fetches — there is no server projection in v1.
 *
 * What this deliberately does NOT compute is as important as what it does: **time in state** and
 * **per-edge flow counts** are not derivable. Nothing records when a card entered its current
 * state (`UpdatedAt` is bumped by any write, including concurrency-token churn) and nothing
 * records a move at all, so "sitting in Review for 11 days" cannot be distinguished from "moved
 * here an hour ago". Every age below is CARD AGE since `CreatedAt`, and says so in its wording.
 * Both unlock with CARD-0019's `CardRevision`.
 */

/** Priorities the board renders, most important first. */
export const PRIORITIES = [0, 1, 2, 3] as const

/** Above this many cards in one state, the list bands by priority instead of running flat. */
export const BAND_THRESHOLD = 20

/** Session statuses that mean work is live right now. Lockstep with `CardModal`'s active count. */
const LIVE_SESSION_STATUSES = new Set(['Starting', 'Running', 'Stopping'])

export interface BoardFilter {
  /** Free text over identifier (all forms), title, description and labels. */
  query: string
  /** `stateKey` of the selected state, or null for "all states". */
  state: string | null
  /** Priority numbers to keep; empty means all. */
  priorities: number[]
  /** Labels a card must carry ALL of; empty means all. */
  labels: string[]
}

export const EMPTY_FILTER: BoardFilter = { query: '', state: null, priorities: [], labels: [] }

export type StateSignal =
  | { kind: 'empty' }
  | { kind: 'running'; count: number }
  | { kind: 'closed'; at: string }
  | { kind: 'oldest'; identifier: string; ageDays: number; p0Count: number }

export interface StateShape {
  columnId: string
  stateKey: string
  name: string
  columnOrder: number
  cardStatus: CardStatus
  isActive: boolean
  isTerminal: boolean
  /** Cards left after the query/priority/label filters, P0 first then oldest first. */
  cards: CardDto[]
  filteredCount: number
  /** Cards in this state before any filter — the `m` of `n of m`. */
  totalCount: number
  /** Count per priority over the FILTERED cards, indexed by priority (0..3+). */
  priorityMix: number[]
  p0Count: number
  /** Earliest-created card still in this state. Card age, never time in state. */
  oldest: CardDto | null
  liveSessionCount: number
  signal: StateSignal
}

export interface BoardShape {
  states: StateShape[]
  totalCount: number
  filteredCount: number
  /** Every label on the board, sorted — the vocabulary the label filter offers. */
  labels: string[]
}

export function isFilterActive(filter: BoardFilter): boolean {
  return filter.query.trim() !== ''
    || filter.priorities.length > 0
    || filter.labels.length > 0
}

/** A card counts as live when it owns a session or has one that has not finished. */
export function hasLiveSession(card: CardDto): boolean {
  return card.sessions.some((session) => LIVE_SESSION_STATUSES.has(session.status))
    || (card.ownerSessionId !== null
      && !card.sessions.some((session) => session.id === card.ownerSessionId))
}

export function cardMatchesFilter(card: CardDto, filter: BoardFilter): boolean {
  if (filter.priorities.length > 0 && !filter.priorities.includes(card.priority)) return false
  if (filter.labels.length > 0 && !filter.labels.every((label) => card.labels.includes(label))) {
    return false
  }

  const query = filter.query.trim()
  if (!query) return true
  const needle = query.toLowerCase()
  return matchesIdentifierQuery(card.identifier, query)
    || card.title.toLowerCase().includes(needle)
    || card.description.toLowerCase().includes(needle)
    || card.labels.some((label) => label.toLowerCase().includes(needle))
}

/** Whole days since `iso`, floored. Negative clock skew reads as 0 rather than -1. */
export function ageInDays(iso: string, now: Date): number {
  const created = new Date(iso).getTime()
  if (Number.isNaN(created)) return 0
  return Math.max(0, Math.floor((now.getTime() - created) / 86_400_000))
}

/**
 * P0 first, then oldest first. The server returns cards priority DESCENDING
 * (`BoardService.ToDetailDto`), which puts P3 at the top of a column; the whole design reads P0
 * as most important, so the ordering is settled here rather than relied on from the payload.
 */
function orderCards(cards: CardDto[]): CardDto[] {
  return [...cards].sort((a, b) =>
    a.priority - b.priority || a.createdAt.localeCompare(b.createdAt))
}

function buildSignal(
  cards: CardDto[],
  column: Pick<BoardColumnDto, 'isTerminal'>,
  liveSessionCount: number,
  p0Count: number,
  now: Date,
): StateSignal {
  if (cards.length === 0) return { kind: 'empty' }
  if (liveSessionCount > 0) return { kind: 'running', count: liveSessionCount }

  if (column.isTerminal) {
    const closed = cards
      .map((card) => card.completedAt)
      .filter((value): value is string => !!value)
      .sort()
    if (closed.length > 0) return { kind: 'closed', at: closed[closed.length - 1] }
  }

  const oldest = cards.reduce((worst, card) =>
    card.createdAt < worst.createdAt ? card : worst)
  return {
    kind: 'oldest',
    identifier: displayIdentifier(oldest.identifier),
    ageDays: ageInDays(oldest.createdAt, now),
    p0Count,
  }
}

/** The signal line as rendered. One derived fact per state, chosen by rule (feature 011 §2.1). */
export function describeSignal(signal: StateSignal): string {
  switch (signal.kind) {
    case 'empty':
      return '—'
    case 'running':
      return `● ${signal.count} running`
    case 'closed':
      return `last closed ${signal.at.slice(0, 10)}`
    case 'oldest':
      return signal.p0Count > 0
        ? `${signal.p0Count} P0 · oldest ${signal.identifier} · ${signal.ageDays}d`
        : `oldest ${signal.identifier} · ${signal.ageDays}d`
  }
}

export function buildBoardShape(
  board: BoardDetailDto,
  filter: BoardFilter = EMPTY_FILTER,
  now: Date = new Date(),
): BoardShape {
  const states = [...board.columns]
    .sort((a, b) => a.columnOrder - b.columnOrder)
    .map<StateShape>((column) => {
      const cards = orderCards(column.cards.filter((card) => cardMatchesFilter(card, filter)))
      const priorityMix: number[] = []
      for (const card of cards) {
        const bucket = Math.max(0, card.priority)
        priorityMix[bucket] = (priorityMix[bucket] ?? 0) + 1
      }
      for (let index = 0; index < priorityMix.length; index += 1) {
        priorityMix[index] = priorityMix[index] ?? 0
      }
      const p0Count = priorityMix[0] ?? 0
      const liveSessionCount = cards.filter(hasLiveSession).length
      const oldest = cards.length === 0
        ? null
        : cards.reduce((worst, card) => (card.createdAt < worst.createdAt ? card : worst))

      return {
        columnId: column.id,
        stateKey: column.stateKey,
        name: column.name,
        columnOrder: column.columnOrder,
        cardStatus: column.cardStatus,
        isActive: column.isActive,
        isTerminal: column.isTerminal,
        cards,
        filteredCount: cards.length,
        totalCount: column.cards.length,
        priorityMix,
        p0Count,
        oldest,
        liveSessionCount,
        signal: buildSignal(cards, column, liveSessionCount, p0Count, now),
      }
    })

  const labels = new Set<string>()
  for (const column of board.columns) {
    for (const card of column.cards) {
      for (const label of card.labels) labels.add(label)
    }
  }

  return {
    states,
    totalCount: states.reduce((total, state) => total + state.totalCount, 0),
    filteredCount: states.reduce((total, state) => total + state.filteredCount, 0),
    labels: [...labels].sort((a, b) => a.localeCompare(b)),
  }
}

/** Empty is information, so every empty state says what it means rather than disappearing. */
export function emptyStateMessage(state: StateShape, filtered: boolean): string {
  if (filtered) return `No cards in ${state.name} match the current filter.`
  if (state.isActive) {
    return `Nothing is in ${state.name}. Cards land here when a session starts on them — `
      + 'moving a card here spawns an agent.'
  }
  if (state.isTerminal) return `Nothing has reached ${state.name} yet.`
  return `Nothing is in ${state.name}.`
}

export interface PriorityBand {
  priority: number
  cards: CardDto[]
}

/** The state's cards split into priority bands, P0 first. Empty bands are dropped. */
export function priorityBands(cards: CardDto[]): PriorityBand[] {
  const byPriority = new Map<number, CardDto[]>()
  for (const card of cards) {
    const bucket = byPriority.get(card.priority) ?? []
    bucket.push(card)
    byPriority.set(card.priority, bucket)
  }
  return [...byPriority.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([priority, banded]) => ({ priority, cards: banded }))
}

/**
 * Which state group opens by default when nothing is selected: the first non-empty non-terminal
 * state AFTER the first one (the state most likely to need eyes), falling back to the first
 * non-empty state at all — otherwise a board whose only cards sit in Backlog would open nothing.
 */
export function defaultExpandedState(states: StateShape[]): string | null {
  const populated = states.filter((state) => state.filteredCount > 0)
  if (populated.length === 0) return null
  const preferred = populated.find((state) =>
    !state.isTerminal && state.columnOrder > states[0].columnOrder)
  return (preferred ?? populated[0]).stateKey
}

/**
 * Legal move targets for a card.
 *
 * LOCKSTEP PAIR with `server/Domain/StateMachine/CardStateMachine.cs`: any live state reaches any
 * other directly, and terminal states (Done, Canceled) have no way out until CARD-0019's history
 * can record a reopen as a correction. Change one side and change this one.
 */
export function canMoveTo(from: CardStatus, to: CardStatus): boolean {
  if (from === to) return true
  return from !== 'Done' && from !== 'Canceled'
}

export function legalMoveTargets(card: CardDto, columns: BoardColumnDto[]): BoardColumnDto[] {
  return columns
    .filter((column) => column.id !== card.boardColumnId && canMoveTo(card.status, column.cardStatus))
    .sort((a, b) => a.columnOrder - b.columnOrder)
}

/** The state before and after this one along the presented spine (mobile connectors). */
export function spineNeighbours(states: StateShape[], stateKey: string): {
  index: number
  previous: StateShape | null
  current: StateShape | null
  next: StateShape | null
} {
  const index = states.findIndex((state) => state.stateKey === stateKey)
  if (index < 0) return { index: -1, previous: null, current: null, next: null }
  return {
    index,
    previous: index > 0 ? states[index - 1] : null,
    current: states[index],
    next: index < states.length - 1 ? states[index + 1] : null,
  }
}
