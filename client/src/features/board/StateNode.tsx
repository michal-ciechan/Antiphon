import { Badge, Box, Group, Paper, Stack, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { describeSignal, type StateShape } from './boardShapeModel'
import { priorityFill, stateAccent, stateColor } from './boardVisuals'

interface StateNodeProps {
  state: StateShape
  selected: boolean
  /** True when a search/label/priority filter is narrowing the board, so the node shows n of m. */
  filtered: boolean
  onSelect: (stateKey: string) => void
}

/**
 * One state of the board, as a node in the shape strip.
 *
 * The count is a NUMERAL, not an area or a bar height: at board scale a number is read faster and
 * more precisely, and area would imply a precision of comparison nobody needs. The priority bar
 * under it is the shape within the shape — Backlog's problem is not 31, it is that 8 of the 31
 * are P0.
 */
export function StateNode({ state, selected, filtered, onSelect }: StateNodeProps) {
  const empty = state.filteredCount === 0
  const accent = stateAccent(state.cardStatus)
  const mixLabel = state.priorityMix
    .map((count, priority) => (count > 0 ? `${count} P${priority}` : null))
    .filter(Boolean)
    .join(', ')

  return (
    <UnstyledButton
      onClick={() => onSelect(state.stateKey)}
      aria-pressed={selected}
      aria-label={filtered
        ? `${state.name}, ${state.filteredCount} of ${state.totalCount} cards`
        : `${state.name}, ${state.filteredCount} cards`}
      data-testid={`state-node-${state.stateKey}`}
      style={{ flex: '0 0 auto' }}
    >
      <Paper
        withBorder
        p="sm"
        radius="md"
        style={{
          width: 176,
          borderColor: selected ? accent : undefined,
          borderWidth: selected ? 2 : 1,
          borderStyle: empty ? 'dashed' : 'solid',
          backgroundColor: selected ? 'var(--mantine-color-dark-6)' : undefined,
        }}
      >
        <Stack gap={4}>
          <Group gap={6} wrap="nowrap" justify="space-between">
            <Text
              size="xs"
              fw={700}
              tt="uppercase"
              c={empty ? 'dimmed' : stateColor(state.cardStatus)}
              style={{ letterSpacing: '0.05em' }}
              truncate
            >
              {state.name}
            </Text>
            {state.isActive && <Badge size="xs" variant="outline" color="gray">active</Badge>}
            {state.isTerminal && <Badge size="xs" variant="outline" color="gray">terminal</Badge>}
          </Group>

          <Group gap={6} align="baseline" wrap="nowrap">
            <Text fz={30} fw={700} lh={1.1} c={empty ? 'dimmed' : undefined}>
              {state.filteredCount}
            </Text>
            {filtered && (
              <Text size="xs" c="dimmed">of {state.totalCount}</Text>
            )}
          </Group>

          <Tooltip label={mixLabel || 'no cards'} withArrow disabled={!mixLabel}>
            <Group gap={2} h={6} wrap="nowrap" aria-label={`priority mix: ${mixLabel || 'none'}`}>
              {empty
                ? <Box style={{ flex: 1, borderRadius: 2, background: 'var(--mantine-color-dark-4)' }} />
                : state.priorityMix.map((count, priority) => (
                  count > 0
                    ? (
                      <Box
                        key={priority}
                        style={{ flex: count, borderRadius: 2, height: 6, ...priorityFill(priority) }}
                      />
                    )
                    : null
                ))}
            </Group>
          </Tooltip>

          <Text size="xs" c="dimmed" truncate>{describeSignal(state.signal)}</Text>
        </Stack>
      </Paper>
    </UnstyledButton>
  )
}
