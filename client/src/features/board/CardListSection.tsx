import { Badge, Box, Group, Stack, Text, UnstyledButton } from '@mantine/core'
import { TbChevronDown, TbChevronRight } from 'react-icons/tb'
import type { BoardColumnDto } from '../../api/boards'
import { BAND_THRESHOLD, emptyStateMessage, priorityBands, type StateShape } from './boardShapeModel'
import { stateColor } from './boardVisuals'
import { CardRow } from './CardRow'

interface CardListSectionProps {
  state: StateShape
  boardId: string
  columns: BoardColumnDto[]
  now: Date
  filtered: boolean
  /** False when this state is the selected one and owns the whole list. */
  collapsible: boolean
  expanded: boolean
  onToggle: (stateKey: string) => void
  onOpenCard: (cardId: string) => void
  expandedBands: ReadonlySet<string>
  onToggleBand: (bandKey: string) => void
  layout?: 'row' | 'stacked'
}

export function CardListSection({
  state,
  boardId,
  columns,
  now,
  filtered,
  collapsible,
  expanded,
  onToggle,
  onOpenCard,
  expandedBands,
  onToggleBand,
  layout = 'row',
}: CardListSectionProps) {
  const banded = state.cards.length > BAND_THRESHOLD
  const bands = banded ? priorityBands(state.cards) : []

  const rows = state.cards.map((card) => (
    <CardRow
      key={card.id}
      card={card}
      boardId={boardId}
      columns={columns}
      now={now}
      onOpen={onOpenCard}
      layout={layout}
    />
  ))

  const body = state.cards.length === 0
    ? (
      <Text size="sm" c="dimmed" px="xs" py="sm" data-testid={`state-empty-${state.stateKey}`}>
        {emptyStateMessage(state, filtered)}
      </Text>
    )
    : banded
      ? (
        <Stack gap={2}>
          {bands.map((band, index) => {
            const bandKey = `${state.stateKey}:${band.priority}`
            // The first two bands are open; the rest collapse to a summary row, because 31 cards
            // is otherwise a page of scrolling before the important ones are even counted.
            const open = index < 2 || expandedBands.has(bandKey)
            return (
              <Box key={band.priority}>
                <UnstyledButton
                  onClick={() => onToggleBand(bandKey)}
                  aria-expanded={open}
                  aria-label={`P${band.priority} band, ${band.cards.length} cards`}
                  w="100%"
                >
                  <Group gap={8} align="center" mt={index === 0 ? 0 : 8} mb={2} wrap="nowrap">
                    {open ? <TbChevronDown size={13} /> : <TbChevronRight size={13} />}
                    <Text size="xs" fw={700} c="dimmed" style={{ letterSpacing: '0.05em' }}>
                      P{band.priority} — {band.cards.length}
                    </Text>
                    <Box style={{ flex: 1, borderTop: '1px solid var(--mantine-color-dark-5)' }} />
                  </Group>
                </UnstyledButton>
                {open && (
                  <Stack gap={0}>
                    {band.cards.map((card) => (
                      <CardRow
                        key={card.id}
                        card={card}
                        boardId={boardId}
                        columns={columns}
                        now={now}
                        onOpen={onOpenCard}
                        layout={layout}
                      />
                    ))}
                  </Stack>
                )}
              </Box>
            )
          })}
        </Stack>
      )
      : <Stack gap={0}>{rows}</Stack>

  return (
    <Box data-testid={`board-column-${state.stateKey}`} mb={collapsible ? 4 : 0}>
      {collapsible
        ? (
          <UnstyledButton
            onClick={() => onToggle(state.stateKey)}
            aria-expanded={expanded}
            aria-label={`${state.name} group, ${state.filteredCount} cards`}
            w="100%"
          >
            <Group gap={8} align="center" mt="xs" mb={2} wrap="nowrap">
              {expanded ? <TbChevronDown size={14} /> : <TbChevronRight size={14} />}
              <Text size="xs" fw={700} tt="uppercase" c={stateColor(state.cardStatus)}>
                {state.name}
              </Text>
              <Badge size="xs" variant="outline" color="gray">
                {filtered ? `${state.filteredCount} of ${state.totalCount}` : state.filteredCount}
              </Badge>
              <Box style={{ flex: 1, borderTop: '1px solid var(--mantine-color-dark-5)' }} />
            </Group>
          </UnstyledButton>
        )
        : null}
      {(!collapsible || expanded) && body}
    </Box>
  )
}
