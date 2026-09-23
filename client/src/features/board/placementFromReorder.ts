import { arrayMove } from '@dnd-kit/sortable'
import type { CardDto } from '../../api/boards'

export function placementFromReorder(cards: CardDto[], oldIndex: number, newIndex: number) {
  const next = arrayMove(cards, oldIndex, newIndex)
  const moved = next[newIndex]
  return {
    cardId: moved.id,
    concurrencyToken: moved.concurrencyToken,
    before: next[newIndex + 1]?.identifier,
    after: next[newIndex - 1]?.identifier,
    orderedIds: next.map((card) => card.id),
    previous: cards[oldIndex],
    previousNeighbour: oldIndex > 0 ? cards[oldIndex - 1] : undefined,
    previousNext: oldIndex + 1 < cards.length ? cards[oldIndex + 1] : undefined,
    nextBefore: next[newIndex + 1],
    nextAfter: next[newIndex - 1],
  }
}
