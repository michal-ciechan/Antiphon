import { Box, Group, Text, UnstyledButton } from '@mantine/core'
import { useWindowEvent } from '@mantine/hooks'
import { useState } from 'react'
import { TbArrowNarrowRight } from 'react-icons/tb'
import type { StateShape } from './boardShapeModel'
import { StateNode } from './StateNode'
import { stateColor } from './boardVisuals'

interface ShapeStripProps {
  states: StateShape[]
  /** stateKey of the selected node, or null for "all states". */
  selected: string | null
  filtered: boolean
  onSelect: (stateKey: string) => void
}

/**
 * Past this scroll depth the strip gives its vertical budget back to the list (§6), and it does
 * not give it back until you are well above that again. The gap is not cosmetic: collapsing the
 * strip makes the page ~140px shorter, which can clamp `scrollY` back BELOW a single threshold
 * and expand it again — one threshold oscillates, two do not.
 */
const COLLAPSE_AT = 180
const EXPAND_AT = 60

/**
 * The board's states across the top, connected by a single spine of muted arrows in column order.
 *
 * The edges are a PRESENTATION of the usual path, not the rule: since the state machine was
 * widened, any live state moves directly to any other, so drawing the true graph would be 20
 * directed edges over 4 nodes — unreadable, and unweightable besides, because nothing records a
 * move. Hence one spine plus a permanent caption saying so.
 */
export function ShapeStrip({ states, selected, filtered, onSelect }: ShapeStripProps) {
  const [compact, setCompact] = useState(false)
  useWindowEvent('scroll', () => {
    const depth = window.scrollY
    setCompact((current) => (current ? depth >= EXPAND_AT : depth > COLLAPSE_AT))
  })

  return (
    <Box
      data-testid="shape-strip"
      mb="sm"
      style={{
        position: 'sticky',
        top: 'var(--app-shell-header-offset, 56px)',
        zIndex: 3,
        backgroundColor: 'var(--mantine-color-body)',
        borderBottom: compact ? '1px solid var(--mantine-color-dark-4)' : undefined,
      }}
    >
      {compact
        ? (
          <Group gap={6} py={6} wrap="nowrap" data-testid="shape-strip-compact">
            {states.map((state, index) => (
              <Group key={state.columnId} gap={6} wrap="nowrap">
                {index > 0 && <TbArrowNarrowRight size={14} color="var(--mantine-color-dimmed)" />}
                <UnstyledButton
                  onClick={() => onSelect(state.stateKey)}
                  aria-pressed={selected === state.stateKey}
                  aria-label={`${state.name}, ${state.filteredCount} cards`}
                >
                  <Group gap={4} wrap="nowrap">
                    <Text size="xs" c={stateColor(state.cardStatus)} fw={600} tt="uppercase">
                      {state.name}
                    </Text>
                    <Text size="sm" fw={700}>{state.filteredCount}</Text>
                  </Group>
                </UnstyledButton>
              </Group>
            ))}
          </Group>
        )
        : (
          <>
            <Group gap="xs" wrap="nowrap" align="stretch" py="sm" style={{ overflowX: 'auto' }}>
              {states.map((state, index) => (
                <Group key={state.columnId} gap="xs" wrap="nowrap" align="center">
                  {index > 0 && (
                    <TbArrowNarrowRight
                      size={22}
                      color="var(--mantine-color-dark-2)"
                      aria-hidden
                      style={{ flex: '0 0 auto' }}
                    />
                  )}
                  <StateNode
                    state={state}
                    selected={selected === state.stateKey}
                    filtered={filtered}
                    onSelect={onSelect}
                  />
                </Group>
              ))}
            </Group>
            <Text size="xs" c="dimmed" fs="italic" ta="center" pb={6}>
              edges show the usual path — any card can move directly between any live states
            </Text>
          </>
        )}
    </Box>
  )
}
