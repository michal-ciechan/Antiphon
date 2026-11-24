/**
 * PROPOSAL MOCK — not production code. Presentational pieces shared by the Experiments and BYOA
 * Storybook prototypes (`docs/features/013-experiments/plan.md`, `docs/features/014-byoa/plan.md`).
 */
import { ActionIcon, Badge, Box, Group, Paper, Stack, Text, Tooltip } from '@mantine/core'
import type { ReactNode } from 'react'
import {
  TbArrowRight,
  TbCheck,
  TbClock,
  TbLoader2,
  TbPlus,
  TbRecycle,
  TbTilde,
  TbTrash,
  TbWaveSine,
  TbX,
} from 'react-icons/tb'
import { RESULT_META, SECTION_TITLE, fmtSeconds, fmtUsd, variantSteps, type Cell, type CellResult, type SectionKey, type Variant } from './mockData'

export function SectionLabel({ children }: { children: ReactNode }) {
  return (
    <Text size="xs" fw={700} c="dimmed" tt="uppercase" mb={6} style={{ letterSpacing: 0.4 }}>
      {children}
    </Text>
  )
}

/** A page header: title, one line of explanation, and actions on the right. */
export function PageHeader({ title, subtitle, children }: { title: ReactNode; subtitle?: ReactNode; children?: ReactNode }) {
  return (
    <Group justify="space-between" align="flex-start" wrap="nowrap" mb="md">
      <Box>
        <Text fz={22} fw={700}>
          {title}
        </Text>
        {subtitle && (
          <Text size="sm" c="dimmed" maw={820}>
            {subtitle}
          </Text>
        )}
      </Box>
      <Group gap="xs" wrap="nowrap">
        {children}
      </Group>
    </Group>
  )
}

/**
 * The BYOA (bring your own agent) icon: a robot with a question mark. It means one thing
 * everywhere: "click to get a prompt for your AI agent".
 */
export function ByoaIcon({ size = 18 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" role="img" aria-label="BYOA" style={{ verticalAlign: 'middle', flexShrink: 0 }}>
      {/* robot head */}
      <path d="M9.5 8V5.2" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" />
      <circle cx="9.5" cy="4" r="1.3" fill="currentColor" />
      <rect x="2.2" y="8" width="14.6" height="12" rx="3" stroke="currentColor" strokeWidth="1.7" />
      <circle cx="7" cy="13.2" r="1.35" fill="currentColor" />
      <circle cx="12" cy="13.2" r="1.35" fill="currentColor" />
      <path d="M7 16.9h5" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" />
      {/* the question it answers */}
      <circle cx="17.6" cy="6.4" r="6" fill="var(--mantine-color-active-5)" stroke="#1a1b1e" strokeWidth="1.2" />
      <path d="M15.7 4.9a1.95 1.95 0 1 1 2.9 1.7c-.6.35-1 .8-1 1.5" stroke="#fff" strokeWidth="1.5" strokeLinecap="round" fill="none" />
      <circle cx="17.6" cy="10" r="0.95" fill="#fff" />
    </svg>
  )
}

/** The BYOA button as it appears in a page header or on a panel. */
export function ByoaButton({ onClick, label = 'BYOA: get a prompt for your AI agent' }: { onClick?: () => void; label?: string }) {
  return (
    <Tooltip label={label} withArrow>
      <ActionIcon variant="default" size="lg" aria-label={label} onClick={onClick}>
        <ByoaIcon size={22} />
      </ActionIcon>
    </Tooltip>
  )
}

const RESULT_ICON: Record<CellResult, ReactNode> = {
  pass: <TbCheck size={12} />,
  fail: <TbX size={12} />,
  flaky: <TbWaveSine size={12} />,
  running: <TbLoader2 size={12} />,
  queued: <TbClock size={12} />,
  reused: <TbRecycle size={12} />,
}

/** Result as icon + text + colour, never colour alone. */
export function ResultBadge({ result, samples, size = 'sm' }: { result: CellResult; samples?: [number, number]; size?: 'xs' | 'sm' | 'md' }) {
  const m = RESULT_META[result]
  return (
    <Badge color={m.color} variant="light" size={size} radius="sm" tt="none" leftSection={RESULT_ICON[result]}>
      {result === 'reused' ? 'Pass' : m.label}
      {samples && ` ${samples[0]}/${samples[1]}`}
    </Badge>
  )
}

/** One cell of the A/B grid. */
export function ResultCell({ cell, selected, onClick }: { cell: Cell; selected?: boolean; onClick?: () => void }) {
  const m = RESULT_META[cell.result]
  return (
    <Paper
      p={8}
      radius="sm"
      withBorder
      onClick={onClick}
      style={{
        cursor: onClick ? 'pointer' : undefined,
        borderLeft: `3px solid var(--mantine-color-${m.color}-6)`,
        outline: selected ? '2px solid var(--mantine-color-active-5)' : undefined,
        minWidth: 190,
      }}
    >
      <Group justify="space-between" wrap="nowrap" gap={6}>
        <ResultBadge result={cell.result} samples={cell.samples} />
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
          {cell.result === 'queued' ? '' : !cell.costUsd && !cell.seconds ? 'recorded' : `${fmtUsd(cell.costUsd ?? 0)} · ${fmtSeconds(cell.seconds ?? 0)}`}
        </Text>
      </Group>
      {cell.note && (
        <Text size="xs" c="dimmed" mt={4} lineClamp={2}>
          {cell.note}
        </Text>
      )}
      {cell.divergesAt && (
        <Text size="xs" c="dimmed" mt={2}>
          differs from{' '}
          <Text span ff="monospace" size="xs" c="gray.4">
            {cell.divergesAt}
          </Text>
        </Text>
      )}
    </Paper>
  )
}

const KIND_COLOR: Record<string, string> = {
  code: 'gray',
  decision: 'cyan',
  completion: 'teal',
  agent: 'violet',
  'fan-out': 'violet',
  join: 'gray',
  gate: 'yellow',
  wait: 'yellow',
}

/**
 * A variant's graph, drawn as sections of steps in graph order. Steps before `start` are fed from
 * the record (dimmed); steps from `start` on will run (highlighted). A step that differs from the
 * baseline carries a badge.
 */
export function GraphPreview({
  variant,
  start,
  selected,
  onSelect,
  compact = false,
}: {
  variant: Variant
  start: string
  selected?: string
  onSelect?: (stepId: string) => void
  compact?: boolean
}) {
  const steps = variantSteps(variant)
  const startIndex = start === 'input' ? 0 : Math.max(0, steps.findIndex((s) => s.def.id === start))
  const sections: SectionKey[] = ['intake', 'review', 'decide']
  return (
    <Stack gap={6}>
      {sections.map((sec) => (
        <Paper key={sec} withBorder radius="md" px={10} py={compact ? 5 : 7} bg="dark.7">
          <Group gap={10} wrap="nowrap" align="center">
            <Text size="xs" fw={700} c="dimmed" w={52} style={{ flexShrink: 0 }}>
              {SECTION_TITLE[sec]}
            </Text>
            <Group gap={4} wrap="wrap" style={{ flex: 1 }}>
              {steps
                .map((s, i) => ({ ...s, i }))
                .filter((s) => s.def.section === sec)
                .map((s, k) => {
                  const runs = s.i >= startIndex
                  const change = s.override?.change
                  const border = change === 'added' ? 'success' : change === 'changed' ? 'warning' : runs ? 'active' : 'dark'
                  return (
                    <Group key={s.def.id} gap={4} wrap="nowrap">
                      {k > 0 && <TbArrowRight size={12} color="#5c5f66" />}
                      <Tooltip label={s.override ? s.override.summary : runs ? 'Will run' : 'Not run: fed from the recorded output'} withArrow openDelay={200}>
                        <Paper
                          radius="sm"
                          px={8}
                          py={4}
                          onClick={() => onSelect?.(s.def.id)}
                          style={{
                            cursor: onSelect ? 'pointer' : undefined,
                            opacity: runs ? 1 : 0.45,
                            border: `1px ${runs ? 'solid' : 'dashed'} var(--mantine-color-${border}-${border === 'dark' ? 3 : 6})`,
                            background: runs ? 'var(--mantine-color-dark-6)' : 'transparent',
                            outline: selected === s.def.id ? '2px solid var(--mantine-color-active-4)' : undefined,
                            outlineOffset: 1,
                          }}
                        >
                          <Group gap={4} wrap="nowrap">
                            <Box w={6} h={6} bg={`${KIND_COLOR[s.def.kind]}.5`} style={{ borderRadius: 3, flexShrink: 0 }} />
                            <Text size="xs" fw={600} style={{ whiteSpace: 'nowrap' }}>
                              {s.def.label}
                            </Text>
                            {change === 'changed' && (
                              <Badge size="xs" color="warning" variant="light" radius="sm" tt="none" leftSection={<TbTilde size={9} />}>
                                changed
                              </Badge>
                            )}
                            {change === 'added' && (
                              <Badge size="xs" color="success" variant="light" radius="sm" tt="none" leftSection={<TbPlus size={9} />}>
                                added
                              </Badge>
                            )}
                            {change === 'removed' && (
                              <Badge size="xs" color="danger" variant="light" radius="sm" tt="none" leftSection={<TbTrash size={9} />}>
                                removed
                              </Badge>
                            )}
                          </Group>
                          {!compact && s.def.uses && (
                            <Text size="10px" c={change === 'changed' ? 'warning.4' : 'dimmed'} ff="monospace" style={{ whiteSpace: 'nowrap' }}>
                              {s.def.uses}
                            </Text>
                          )}
                        </Paper>
                      </Tooltip>
                    </Group>
                  )
                })}
            </Group>
          </Group>
        </Paper>
      ))}
    </Stack>
  )
}

export function GraphLegend() {
  const item = (style: React.CSSProperties, label: string) => (
    <Group gap={5} wrap="nowrap">
      <Box w={18} h={12} style={{ borderRadius: 3, ...style }} />
      <Text size="xs" c="dimmed">
        {label}
      </Text>
    </Group>
  )
  return (
    <Group gap="md">
      {item({ border: '1px solid var(--mantine-color-active-6)', background: 'var(--mantine-color-dark-6)' }, 'will run')}
      {item({ border: '1px dashed var(--mantine-color-dark-3)', opacity: 0.6 }, 'fed from the record')}
      {item({ border: '1px solid var(--mantine-color-warning-6)' }, 'differs from the baseline')}
      {item({ border: '1px solid var(--mantine-color-success-6)' }, 'added step')}
    </Group>
  )
}

/** A label and its value on one line, for record tables. */
export function Fact({ label, children, labelWidth = 150 }: { label: string; children: ReactNode; labelWidth?: number }) {
  return (
    <Group gap="sm" wrap="nowrap" align="flex-start">
      <Text size="xs" c="dimmed" w={labelWidth} style={{ flexShrink: 0 }}>
        {label}
      </Text>
      <Text size="sm" component="div" style={{ minWidth: 0 }}>
        {children}
      </Text>
    </Group>
  )
}
