import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  TouchSensor,
  type DragEndEvent,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { Button, Group, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import { usePlaceCard } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'
import { CardRow } from './CardRow'

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

interface SortableCardListProps {
  cards: CardDto[]
  boardId: string
  columns: BoardColumnDto[]
  now: Date
  onOpen: (cardId: string) => void
  layout?: 'row' | 'stacked'
  enabled: boolean
}

export function SortableCardList({
  cards,
  boardId,
  columns,
  now,
  onOpen,
  layout = 'row',
  enabled,
}: SortableCardListProps) {
  const placeCard = usePlaceCard(boardId)
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )
  const ids = cards.map((card) => card.id)

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event
    if (!enabled || !over || active.id === over.id) return
    const oldIndex = ids.indexOf(String(active.id))
    const newIndex = ids.indexOf(String(over.id))
    if (oldIndex < 0 || newIndex < 0) return

    const placement = placementFromReorder(cards, oldIndex, newIndex)
    const moved = placement.previous

    placeCard.mutate(
      {
        cardId: placement.cardId,
        request: {
          concurrencyToken: placement.concurrencyToken,
          before: placement.before,
          after: placement.after,
          editedBy: 'operator',
        },
        orderedIds: placement.orderedIds,
      },
      {
        onSuccess: (result) => {
          const axesChanged = result.importance !== moved.importance || result.urgency !== moved.urgency
          if (!axesChanged) return
          const id = notifications.show({
            color: 'blue',
            message: (
              <Group justify="space-between" wrap="nowrap" gap="sm">
                <Text size="sm">
                  {result.identifier} placed {placement.nextBefore ? `above ${placement.nextBefore.identifier}` : placement.nextAfter ? `below ${placement.nextAfter.identifier}` : ''}
                  {' · '}importance {moved.importance} → {result.importance}
                </Text>
                <Button
                  size="compact-xs"
                  variant="light"
                  onClick={() => {
                    notifications.hide(id)
                    placeCard.mutate({
                      cardId: result.id,
                      request: {
                        concurrencyToken: result.concurrencyToken,
                        after: placement.previousNeighbour?.identifier,
                        before: placement.previousNeighbour ? undefined : placement.previousNext?.identifier,
                        importance: moved.importance,
                        urgency: moved.urgency,
                        editedBy: 'operator',
                        reason: 'Undo reorder',
                      },
                    })
                  }}
                >
                  Undo
                </Button>
              </Group>
            ),
            autoClose: 8000,
          })
        },
        onError: (error) => {
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Reorder failed') })
        },
      },
    )
  }

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
      <SortableContext items={ids} strategy={verticalListSortingStrategy} disabled={!enabled}>
        <Stack gap={0}>
          {cards.map((card) => (
            <CardRow
              key={card.id}
              card={card}
              boardId={boardId}
              columns={columns}
              now={now}
              onOpen={onOpen}
              layout={layout}
              reorderable={enabled}
            />
          ))}
        </Stack>
      </SortableContext>
    </DndContext>
  )
}
