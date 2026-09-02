import { Badge, Box, Drawer, Group, Paper, Stack, Text, UnstyledButton } from '@mantine/core'
import { useRef, useState } from 'react'
import { TbChevronDown, TbChevronUp, TbLayoutGrid } from 'react-icons/tb'
import type { BoardColumnDto } from '../../api/boards'
import { IMPORTANCES, describeSignal, emptyStateMessage, spineNeighbours, type StateShape } from './boardShapeModel'
import { importanceFill, stateColor } from './boardVisuals'
import { CardListSection } from './CardListSection'

interface StatePagerProps {
  states: StateShape[]
  stateKey: string
  boardId: string
  columns: BoardColumnDto[]
  now: Date
  filtered: boolean
  onSelectState: (stateKey: string) => void
  onOpenCard: (cardId: string) => void
  expandedBands: ReadonlySet<string>
  onToggleBand: (bandKey: string) => void
}

const SWIPE_THRESHOLD = 48

/**
 * The phone board: one state at a time, with the states that feed it entering at the top and the
 * ones it leads to leaving at the bottom. Navigating is moving along the flow, not scrolling a
 * wall sideways.
 *
 * The connectors are NAVIGATION along the usual spine, not the move rule — a card may go to any
 * live state, which is what the "all states" sheet and each card's move menu offer.
 */
export function StatePager({
  states,
  stateKey,
  boardId,
  columns,
  now,
  filtered,
  onSelectState,
  onOpenCard,
  expandedBands,
  onToggleBand,
}: StatePagerProps) {
  const [sheetOpen, setSheetOpen] = useState(false)
  const touchStart = useRef<number | null>(null)
  const { index, previous, current, next } = spineNeighbours(states, stateKey)

  if (!current) return null

  const step = (delta: number) => {
    const target = states[index + delta]
    if (target) onSelectState(target.stateKey)
  }

  return (
    <Box
      data-testid="state-pager"
      onTouchStart={(event) => { touchStart.current = event.changedTouches[0]?.clientX ?? null }}
      onTouchEnd={(event) => {
        const start = touchStart.current
        touchStart.current = null
        if (start === null) return
        const delta = (event.changedTouches[0]?.clientX ?? start) - start
        if (delta <= -SWIPE_THRESHOLD) step(1)
        else if (delta >= SWIPE_THRESHOLD) step(-1)
      }}
    >
      <Group gap="sm" justify="center" py="xs" data-testid="state-pager-dots">
        {states.map((state) => {
          const on = state.stateKey === current.stateKey
          return (
            <UnstyledButton
              key={state.columnId}
              onClick={() => onSelectState(state.stateKey)}
              aria-label={`Go to ${state.name}, ${state.filteredCount} cards`}
              aria-current={on}
            >
              <Box
                style={{
                  width: on ? 11 : 8,
                  height: on ? 11 : 8,
                  borderRadius: '50%',
                  border: `1.5px solid var(--mantine-color-${stateColor(state.cardStatus)}-5)`,
                  background: on ? `var(--mantine-color-${stateColor(state.cardStatus)}-5)` : 'transparent',
                }}
              />
            </UnstyledButton>
          )
        })}
      </Group>

      <Connector state={previous} direction="from" onSelect={onSelectState} />

      <Group gap={8} align="baseline" px="xs" pt="sm" wrap="nowrap">
        <Text fw={700} tt="uppercase" c={stateColor(current.cardStatus)}>{current.name}</Text>
        <Text fz={22} fw={700}>{current.filteredCount}</Text>
        {filtered && <Text size="xs" c="dimmed">of {current.totalCount}</Text>}
        {current.isActive && <Badge size="xs" variant="outline" color="gray">active</Badge>}
        {current.isTerminal && <Badge size="xs" variant="outline" color="gray">terminal</Badge>}
      </Group>
      <Text size="xs" c="dimmed" px="xs" pb="xs">{describeSignal(current.signal)}</Text>

      {current.filteredCount === 0
        ? (
          <Text
            size="sm"
            c="dimmed"
            ta="center"
            px="xl"
            py={40}
            data-testid={`state-empty-${current.stateKey}`}
          >
            {emptyStateMessage(current, filtered)}
          </Text>
        )
        : (
          <CardListSection
            state={current}
            boardId={boardId}
            columns={columns}
            now={now}
            filtered={filtered}
            collapsible={false}
            expanded
            onToggle={() => {}}
            onOpenCard={onOpenCard}
            expandedBands={expandedBands}
            onToggleBand={onToggleBand}
            layout="stacked"
          />
        )}

      <Connector state={next} direction="to" onSelect={onSelectState} />

      <Group justify="center" py="sm">
        <UnstyledButton onClick={() => setSheetOpen(true)} aria-label="All states">
          <Group gap={6}>
            <TbLayoutGrid size={14} />
            <Text size="sm" c="dimmed">all states</Text>
          </Group>
        </UnstyledButton>
      </Group>

      <Drawer
        opened={sheetOpen}
        onClose={() => setSheetOpen(false)}
        position="bottom"
        size="60%"
        title="All states"
      >
        <Stack gap="xs">
          <Text size="xs" c="dimmed">
            A card can move directly between any live states — the connectors show the usual path,
            not the rule.
          </Text>
          {states.map((state) => (
            <UnstyledButton
              key={state.columnId}
              onClick={() => {
                onSelectState(state.stateKey)
                setSheetOpen(false)
              }}
              aria-label={`${state.name}, ${state.filteredCount} cards`}
            >
              <Paper withBorder p="xs" radius="md">
                <Group justify="space-between" wrap="nowrap">
                  <Text size="sm" fw={600} c={stateColor(state.cardStatus)}>{state.name}</Text>
                  <Text size="sm" fw={700}>{state.filteredCount}</Text>
                </Group>
                <Group gap={2} h={5} mt={6} wrap="nowrap">
                  {state.importanceMix.map((count, index) => (
                    count > 0
                      ? <Box key={IMPORTANCES[index]} style={{ flex: count, height: 5, borderRadius: 2, ...importanceFill(IMPORTANCES[index]) }} />
                      : null
                  ))}
                </Group>
                <Text size="xs" c="dimmed" mt={4}>{describeSignal(state.signal)}</Text>
              </Paper>
            </UnstyledButton>
          ))}
        </Stack>
      </Drawer>
    </Box>
  )
}

function Connector({
  state,
  direction,
  onSelect,
}: {
  state: StateShape | null
  direction: 'from' | 'to'
  onSelect: (stateKey: string) => void
}) {
  if (!state) return null
  return (
    <UnstyledButton
      onClick={() => onSelect(state.stateKey)}
      w="100%"
      data-testid={`pager-connector-${direction}`}
      aria-label={`${direction === 'from' ? 'from' : 'to'} ${state.name}, ${state.filteredCount} cards`}
    >
      <Paper
        withBorder
        p={8}
        radius="md"
        my={8}
        style={{ borderStyle: 'dashed', backgroundColor: 'var(--mantine-color-dark-6)' }}
      >
        <Group gap={8} wrap="nowrap">
          {direction === 'from' ? <TbChevronUp size={14} /> : <TbChevronDown size={14} />}
          <Text size="xs" c="dimmed">{direction}</Text>
          <Text size="sm" fw={600} c={stateColor(state.cardStatus)}>{state.name}</Text>
          <Text size="sm" fw={700}>{state.filteredCount}</Text>
        </Group>
      </Paper>
    </UnstyledButton>
  )
}
