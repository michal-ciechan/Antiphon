import type { CardDto } from '../../api/boards'
import { orderCards, quadrantLabel } from '../board/boardShapeModel'
import { QUADRANT_ORDER, type CardQuadrant } from '../board/cardRanking'

/** Visible rows per quadrant before the box offers Show all. */
export const BACKLOG_BOX_CAP = 12

/** What each cell means — the empty box is otherwise just a name. */
export const QUADRANT_HINTS: Record<CardQuadrant, string> = {
  DoFirst: 'important and urgent',
  Schedule: 'important, not yet urgent',
  Clear: 'urgent, not important',
  Someday: 'neither, yet',
}

export interface BacklogBox {
  quadrant: CardQuadrant
  label: string
  hint: string
  cards: CardDto[]
}

/**
 * Fleet-wide Backlog, one cell per quadrant. Empty cells stay: dropping them is the board
 * column's scroll aid, and here an empty cell is the message.
 */
export function groupBacklog(cards: CardDto[]): BacklogBox[] {
  const byQuadrant = new Map<CardQuadrant, CardDto[]>()
  for (const card of cards) {
    const bucket = byQuadrant.get(card.quadrant) ?? []
    bucket.push(card)
    byQuadrant.set(card.quadrant, bucket)
  }

  return QUADRANT_ORDER.map((cell) => ({
    quadrant: cell,
    label: quadrantLabel(cell),
    hint: QUADRANT_HINTS[cell],
    cards: orderCards(byQuadrant.get(cell) ?? []),
  }))
}

/** Distinct boards that currently have at least one Backlog card in this list. */
export function boardsPresent(cards: CardDto[]): number {
  return new Set(cards.map((card) => card.boardId)).size
}
