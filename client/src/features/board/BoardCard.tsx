import { ActionIcon, Badge, Group, Paper, Stack, Text, Tooltip } from '@mantine/core'
import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { TbGripVertical, TbTerminal2 } from 'react-icons/tb'
import type { CardDto } from '../../api/boards'

interface BoardCardProps {
  card: CardDto
  onOpen: (cardId: string) => void
}

export function BoardCard({ card, onOpen }: BoardCardProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: card.id,
  })
  const style = {
    transform: CSS.Translate.toString(transform),
    opacity: isDragging ? 0.55 : 1,
  }

  return (
    <Paper
      ref={setNodeRef}
      withBorder
      p="sm"
      radius={6}
      shadow={isDragging ? 'md' : undefined}
      role="article"
      aria-label={`${card.identifier} ${card.title}`}
      onClick={() => onOpen(card.id)}
      style={{
        ...style,
        cursor: 'pointer',
        borderLeft: card.ownerSessionId
          ? '3px solid var(--mantine-color-success-5)'
          : '3px solid var(--mantine-color-dark-4)',
      }}
    >
      <Stack gap={6}>
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Stack gap={2} style={{ minWidth: 0 }}>
            <Text size="xs" c="dimmed">{card.identifier}</Text>
            <Text fw={600} size="sm" lineClamp={2}>{card.title}</Text>
          </Stack>
          <Tooltip label="Drag card" withArrow>
            <ActionIcon
              variant="subtle"
              size="sm"
              aria-label={`Drag ${card.identifier}`}
              {...listeners}
              {...attributes}
              onClick={(event) => event.stopPropagation()}
            >
              <TbGripVertical size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>

        {card.description && (
          <Text size="xs" c="dimmed" lineClamp={2}>{card.description}</Text>
        )}

        <Group justify="space-between" gap={6}>
          <Group gap={4}>
            <Badge size="xs" color="gray" variant="outline">P{card.priority}</Badge>
            {card.labels.slice(0, 2).map((label) => (
              <Badge key={label} size="xs" color="active" variant="light">{label}</Badge>
            ))}
          </Group>
          {card.ownerSessionId && (
            <Tooltip label="Session active" withArrow>
              <TbTerminal2 size={16} color="var(--mantine-color-success-5)" />
            </Tooltip>
          )}
        </Group>
      </Stack>
    </Paper>
  )
}
