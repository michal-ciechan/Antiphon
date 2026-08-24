import { Badge, Group, Paper, Stack, Text, Tooltip } from '@mantine/core'
import { TbTerminal2 } from 'react-icons/tb'
import type { CardDto } from '../../api/boards'
import { trackerAbbreviation } from '../../api/boards'
import { displayIdentifier } from '../../shared/cardIdentifier'

interface BoardCardProps {
  card: CardDto
  onOpen: (cardId: string) => void
}

/**
 * The stacked-tile rendering, now used only by the all-boards view (`/boards`), which stays
 * status-bucketed in v1. The single-board surface renders `CardRow` under the state graph.
 *
 * The accessible name keeps the CANONICAL identifier — it is what a person cites and what E2E
 * looks a card up by — while the visible chip renders the short form.
 */
export function BoardCard({ card, onOpen }: BoardCardProps) {
  return (
    <Paper
      withBorder
      p="sm"
      radius={6}
      role="article"
      aria-label={`${card.identifier} ${card.title}`}
      onClick={() => onOpen(card.id)}
      style={{
        cursor: 'pointer',
        borderLeft: card.ownerSessionId
          ? '3px solid var(--mantine-color-success-5)'
          : '3px solid var(--mantine-color-dark-4)',
      }}
    >
      <Stack gap={6}>
        <Stack gap={2} style={{ minWidth: 0 }}>
          <Text size="xs" c="dimmed">
            {displayIdentifier(card.identifier)}
            {card.externalIssue && (
              <Text component="span" size="xs" c="dimmed" opacity={0.7} data-testid="card-external-key">
                {` ${trackerAbbreviation(card.externalIssue.trackerKind)} ${card.externalIssue.key}`.replace('  ', ' ')}
              </Text>
            )}
          </Text>
          <Text fw={600} size="sm" lineClamp={2}>{card.title}</Text>
        </Stack>

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
