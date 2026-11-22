import { Group, Paper, Text, Tooltip, UnstyledButton } from '@mantine/core'
import type { ReactNode } from 'react'
import { TbSquare, TbSquareCheckFilled } from 'react-icons/tb'

/** A KPI card. With `onToggle` it is a filter: corner checkbox, accent border and tint when on. */
export function KpiCard({
  label,
  value,
  sub,
  accent,
  selected = false,
  onToggle,
  hint,
}: {
  label: string
  value: ReactNode
  sub?: ReactNode
  accent: string
  selected?: boolean
  onToggle?: () => void
  hint?: string
}) {
  const interactive = !!onToggle
  const ring = selected ? `var(--mantine-color-${accent}-5)` : undefined
  const card = (
    <Paper
      withBorder
      p="xs"
      radius="md"
      h="100%"
      style={{
        borderTopWidth: 2,
        borderTopColor: `var(--mantine-color-${accent}-6)`,
        borderRightColor: ring,
        borderBottomColor: ring,
        borderLeftColor: ring,
        background: selected ? `var(--mantine-color-${accent}-light)` : undefined,
        boxShadow: selected ? `inset 0 0 0 1px var(--mantine-color-${accent}-5)` : undefined,
        cursor: interactive ? 'pointer' : 'default',
        transition: 'background 120ms, box-shadow 120ms',
      }}
    >
      <Group justify="space-between" gap={4} wrap="nowrap" align="flex-start">
        <Text size="xs" c={selected ? undefined : 'dimmed'} tt="uppercase" fw={600} style={{ letterSpacing: 0.5 }}>
          {label}
        </Text>
        {interactive &&
          (selected ? (
            <TbSquareCheckFilled size={16} color={`var(--mantine-color-${accent}-5)`} style={{ flexShrink: 0 }} />
          ) : (
            <TbSquare size={16} color="var(--mantine-color-dark-2)" style={{ flexShrink: 0 }} />
          ))}
      </Group>
      <Text size="xl" fw={700} lh={1.2} component="div">
        {value}
      </Text>
      {sub && (
        <Text size="xs" c="dimmed" mt={2} component="div">
          {sub}
        </Text>
      )}
    </Paper>
  )
  if (!interactive) return card
  return (
    <Tooltip label={`${selected ? 'Filtering: ' : 'Click to filter: '}${hint ?? label}`} withArrow openDelay={250}>
      <UnstyledButton onClick={onToggle} aria-pressed={selected} h="100%" style={{ display: 'block' }}>
        {card}
      </UnstyledButton>
    </Tooltip>
  )
}
