import { Badge, Group, Paper, ScrollArea, Stack, Text } from '@mantine/core'
import type { BoardColumnDto } from '../../api/boards'
import { BoardCard } from './BoardCard'

interface BoardColumnProps {
  column: BoardColumnDto
  onOpenCard: (cardId: string) => void
}

/**
 * The stacked-column rendering, kept ONLY for the all-boards view (`/boards`), whose six
 * synthetic status buckets are out of scope for feature 011. It is no longer a drop target:
 * drag-and-drop is gone from the board entirely (§2.3), and this view's drag was already inert.
 */
export function BoardColumn({ column, onOpenCard }: BoardColumnProps) {
  return (
    <Paper
      p="sm"
      radius={6}
      withBorder
      bg="dark.7"
      style={{
        minWidth: 280,
        height: 'calc(100vh - 180px)',
        display: 'flex',
        flexDirection: 'column',
      }}
      data-testid={`board-column-${column.stateKey}`}
    >
      <Group justify="space-between" mb="sm" wrap="nowrap">
        <Group gap={6}>
          <Text fw={700} size="sm">{column.name}</Text>
          {column.isActive && <Badge size="xs" color="green" variant="light">Active</Badge>}
          {column.isTerminal && <Badge size="xs" color="gray" variant="light">Terminal</Badge>}
        </Group>
        <Badge size="sm" variant="outline">{column.cards.length}</Badge>
      </Group>

      <ScrollArea style={{ flex: 1 }} offsetScrollbars>
        <Stack gap="sm">
          {column.cards.map((card) => (
            <BoardCard key={card.id} card={card} onOpen={onOpenCard} />
          ))}
        </Stack>
      </ScrollArea>
    </Paper>
  )
}
