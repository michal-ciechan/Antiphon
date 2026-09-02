import type { BoardColumnDto, BoardDetailDto, CardDto, CardImportance, CardQuadrant, CardStatus } from '../../api/boards'
import { QUADRANT_ORDER } from './cardRanking'
import { displayIdentifier, matchesIdentifierQuery } from '../../shared/cardIdentifier'

/**
 * The pure core of the board state graph (feature 011 §5). Everything the strip, the list, the
 * mobile pager and the filters render is derived here from the `BoardDetailDto` the page already
 * fetches — there is no server projection in v1.
 *
 * What this deliberately does NOT compute is as important as what it does: **time in state** and
 * **per-edge flow counts** are not derivable HERE. Moves ARE recorded now — CARD-0019 landed
 * `CardRevision`, and a card's `Move` entries carry from/to and a timestamp — but they arrive
 * only from `GET /cards/{id}/revisions`, one card at a time. The board payload still carries no
 * entered-state-at, and `UpdatedAt` is bumped by any write including concurrency-token churn, so
 * "sitting in Review for 11 days" still cannot be distinguished from "moved here an hour ago" at
 * board scale. Every age below is CARD AGE since `CreatedAt`, and says so in its wording. What is
 * left is the server-side projection, deferred per feature 011 §5.
 *
 * Archived cards need no filtering here: they are simply absent from the payload unless the page
 * asks for them (`useBoard(id, { includeArchived })`).
 */

/** Importance chips, most important first. */
export const IMPORTANCES: readonly CardImportance[] = ['Critical', 'High', 'Normal', 'Low']

/** Above this many cards in one state, the list bands by priority instead of running flat. */
export const BAND_THRESHOLD = 20

/** Session statuses that mean work is live right now. Lockstep with `CardModal`'s active count. */
const LIVE_SESSION_STATUSES = new Set(['Starting', 'Running', 'Stopping'])

export interface BoardFilter {
  /** Free text over identifier (all forms), title, description and labels. */
  query: string
  /** `stateKey` of the selected state, or null for "all states". */
  state: string | null
  /** Importance names to keep; empty means all. */
  importances: CardImportance[]
  /** When true, keep only cards whose effective urgency is not Normal. */
  urgentOnly: boolean
  /** Labels a card must carry ALL of; empty means all. */
  labels: string[]
}

export const EMPTY_FILTER: BoardFilter = { query: '', state: null, importances: [], urgentOnly: false, labels: [] }

export type StateSignal =
  | { kind: 'empty' }
  | { kind: 'running'; count: number }
  | { kind: 'closed'; at: string }
  | { kind: 'oldest'; identifier: string; ageDays: number; criticalCount: number }

export interface StateShape {
  columnId: string
  stateKey: string
  name: string
  columnOrder: number
  cardStatus: CardStatus
  isActive: boolean
  isTerminal: boolean
  /** Cards left after the query/importance/urgent/label filters, lowest rank first. */
  cards: CardDto[]
  filteredCount: number
  /** Cards in this state before any filter — the `m` of `n of m`. */
  totalCount: number
  /** Count per importance over the FILTERED cards, Critical-first. */
  importanceMix: number[]
  criticalCount: number
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
    || filter.importances.length > 0
    || filter.urgentOnly
    || filter.labels.length > 0
}

/** A card counts as live when it owns a session or has one that has not finished. */
export function hasLiveSession(card: CardDto): boolean {
  return card.sessions.some((session) => LIVE_SESSION_STATUSES.has(session.status))
    || (card.ownerSessionId !== null
      && !card.sessions.some((session) => session.id === card.ownerSessionId))
}

export function cardMatchesFilter(card: CardDto, filter: BoardFilter): boolean {
  if (filter.importances.length > 0 && !filter.importances.includes(card.importance)) return false
  if (filter.urgentOnly && card.effectiveUrgency === 'Normal') return false
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

/** Lowest rank first, then earliest due date, then oldest created. */
function orderCards(cards: CardDto[]): CardDto[] {
  return [...cards].sort((a, b) =>
    a.rank - b.rank
    || (a.dueAt ?? '9999').localeCompare(b.dueAt ?? '9999')
    || a.createdAt.localeCompare(b.createdAt))
}

function buildSignal(
  cards: CardDto[],
  column: Pick<BoardColumnDto, 'isTerminal'>,
  liveSessionCount: number,
  criticalCount: number,
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
    criticalCount,
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
      return signal.criticalCount > 0
        ? `${signal.criticalCount} Critical · oldest ${signal.identifier} · ${signal.ageDays}d`
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
      const importanceMix = IMPORTANCES.map((importance) =>
        cards.filter((card) => card.importance === importance).length)
      const criticalCount = importanceMix[0] ?? 0
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
        importanceMix,
        criticalCount,
        oldest,
        liveSessionCount,
        signal: buildSignal(cards, column, liveSessionCount, criticalCount, now),
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

export interface QuadrantBand {
  quadrant: CardQuadrant
  cards: CardDto[]
}

const QUADRANT_LABELS: Record<CardQuadrant, string> = {
  DoFirst: 'Do first',
  Schedule: 'Schedule',
  Clear: 'Clear',
  Someday: 'Someday',
}

export function quadrantLabel(value: CardQuadrant): string {
  return QUADRANT_LABELS[value]
}

/** The state's cards split into Eisenhower bands, DoFirst first. Empty bands are dropped. */
export function quadrantBands(cards: CardDto[]): QuadrantBand[] {
  const byQuadrant = new Map<CardQuadrant, CardDto[]>()
  for (const card of cards) {
    const bucket = byQuadrant.get(card.quadrant) ?? []
    bucket.push(card)
    byQuadrant.set(card.quadrant, bucket)
  }
  return QUADRANT_ORDER
    .filter((cell) => (byQuadrant.get(cell)?.length ?? 0) > 0)
    .map((cell) => ({ quadrant: cell, cards: byQuadrant.get(cell)! }))
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
 * other directly, and terminal states (Done, Canceled) stay terminal for the MOVE verb. Reopen
 * is a distinct verb (`canReopenFrom` here, `CardStateMachine.CanReopenFrom` on the server) —
 * not a new `canMoveTo` edge. Change one side and change this one.
 */
export function canMoveTo(from: CardStatus, to: CardStatus): boolean {
  // A self-move is not a transition. The server builds every row via `Without(self)`, so
  // `CanTransition(x, x)` is false and a same-status move is refused. `legalMoveTargets` filters
  // the card's current COLUMN out, which hides this on a board whose columns each carry a
  // distinct status — but two columns may share one `cardStatus`, and then the column filter
  // passes and the server still says no. Answer it here, where the lockstep claim is made.
  if (from === to) return false
  return from !== 'Done' && from !== 'Canceled'
}

/**
 * Reopen is a dedicated verb, not a move-table edge. True only for Done/Canceled; every live
 * status is refused so a reopen cannot be used as a silent move.
 * LOCKSTEP PAIR with `server/Domain/StateMachine/CardStateMachine.cs` CanReopenFrom.
 */
export function canReopenFrom(status: CardStatus): boolean {
  return status === 'Done' || status === 'Canceled'
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
