/**
 * PROPOSAL MOCK — not production code. Small presentational pieces shared by the review-engine
 * Storybook prototypes. Nothing outside `src/stories/review-engine/` imports this file.
 */
import { Avatar, Badge, Box, Group, Paper, Stack, Text, Tooltip } from '@mantine/core'
import type { ReactNode } from 'react'
import { TbRobot, TbShieldLock, TbDatabaseX } from 'react-icons/tb'
import {
  BLAST_DIMS,
  FLAG_META,
  MODELS,
  PEOPLE,
  SEVERITY_META,
  STAGE_META,
  blastNorm,
  blastText,
  fmtUsd,
  type Blast,
  type Cost,
  type Flag,
  type ModelKey,
  type Severity,
  type Stage,
} from './mockData'

export function StageChip({ stage, detail, size = 'sm' }: { stage: Stage; detail?: string; size?: 'xs' | 'sm' | 'md' }) {
  const m = STAGE_META[stage]
  return (
    <Tooltip label={m.hint} withArrow openDelay={300}>
      <Group gap={6} wrap="nowrap">
        <Badge color={m.color} variant="light" size={size} radius="sm">
          {m.label}
        </Badge>
        {detail && (
          <Text size="xs" c="dimmed" truncate>
            {detail}
          </Text>
        )}
      </Group>
    </Tooltip>
  )
}

export function SeverityBadge({ severity, size = 'sm', variant = 'filled' }: { severity: Severity; size?: 'xs' | 'sm' | 'md'; variant?: 'filled' | 'light' | 'outline' }) {
  const m = SEVERITY_META[severity]
  return (
    <Badge color={m.color} variant={variant} size={size} radius="sm" miw={size === 'xs' ? 52 : 64} style={{ overflow: 'visible' }}>
      {m.label}
    </Badge>
  )
}

export function FlagBadges({ flags, size = 'xs' }: { flags: Flag[]; size?: 'xs' | 'sm' }) {
  if (flags.length === 0) return null
  return (
    <Group gap={4} wrap="nowrap">
      {flags.map((f) => (
        <Badge
          key={f}
          size={size}
          color={FLAG_META[f].color}
          variant="outline"
          radius="sm"
          leftSection={f === 'security' ? <TbShieldLock size={10} /> : <TbDatabaseX size={10} />}
        >
          {FLAG_META[f].label}
        </Badge>
      ))}
    </Group>
  )
}

export function ModelBadge({ model, withProvider = false, short = false, size = 'xs' }: { model: ModelKey; withProvider?: boolean; short?: boolean; size?: 'xs' | 'sm' }) {
  const m = MODELS[model]
  return (
    <Tooltip
      withArrow
      openDelay={200}
      label={
        <Stack gap={0}>
          <Text size="xs" fw={600}>
            {m.name}
          </Text>
          <Text size="xs">
            {m.provider} · {m.transport}
          </Text>
          <Text size="xs" c="dimmed">
            {m.billing === 'api' ? 'Metered API' : 'Subscription seat'} · {m.role}
          </Text>
        </Stack>
      }
    >
      <Badge
        size={size}
        color={m.color}
        variant={m.billing === 'api' ? 'filled' : 'light'}
        radius="sm"
        tt="none"
        style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}
      >
        {short ? m.short : m.name}
        {withProvider && <span style={{ opacity: 0.7 }}> · {m.transport === 'HTTP' ? 'LiteLLM' : 'seat'}</span>}
      </Badge>
    </Tooltip>
  )
}

export function ModelStack({ models, max = 4, short = true }: { models: ModelKey[]; max?: number; short?: boolean }) {
  if (models.length === 0)
    return (
      <Text size="xs" c="dimmed">
        —
      </Text>
    )
  const shown = models.slice(0, max)
  return (
    <Group gap={3} wrap="wrap">
      {shown.map((m) => (
        <ModelBadge key={m} model={m} short={short} />
      ))}
      {models.length > max && (
        <Text size="xs" c="dimmed">
          +{models.length - max}
        </Text>
      )}
    </Group>
  )
}

function blastColor(n: number): string {
  if (n >= 0.75) return 'var(--mantine-color-danger-6)'
  if (n >= 0.5) return 'var(--mantine-color-warning-6)'
  if (n >= 0.25) return 'var(--mantine-color-yellow-6)'
  return 'var(--mantine-color-success-7)'
}

/** Six tiny bars: Reach, Contract, Data, Rollback, Reversal, Exposure. */
export function BlastMini({ blast, height = 18 }: { blast?: Blast; height?: number }) {
  if (!blast)
    return (
      <Text size="xs" c="dimmed">
        pending
      </Text>
    )
  return (
    <Tooltip
      withArrow
      label={
        <Stack gap={2}>
          {BLAST_DIMS.map((d) => (
            <Group key={d.key} gap={6} justify="space-between" wrap="nowrap">
              <Text size="xs">{d.label}</Text>
              <Text size="xs" fw={600}>
                {blastText(blast, d.key)}
              </Text>
            </Group>
          ))}
        </Stack>
      }
    >
      <Group gap={2} wrap="nowrap" align="flex-end" h={height}>
        {BLAST_DIMS.map((d) => {
          const n = blastNorm(blast, d.key)
          return (
            <Box
              key={d.key}
              w={5}
              h={Math.max(2, Math.round(n * height))}
              style={{ background: blastColor(n), borderRadius: 1 }}
            />
          )
        })}
      </Group>
    </Tooltip>
  )
}

/** Labelled bars, one row per dimension, with the code-only evidence on hover. */
export function BlastPanel({ blast, trips, compact = false }: { blast: Blast; trips?: string[]; compact?: boolean }) {
  return (
    <Stack gap={compact ? 4 : 6}>
      {BLAST_DIMS.map((d) => {
        const n = blastNorm(blast, d.key)
        return (
          <Tooltip key={d.key} label={d.evidence} withArrow openDelay={300} position="left">
            <Box>
              <Group justify="space-between" gap={4} wrap="nowrap">
                <Text size="xs" c="dimmed">
                  {d.label}
                </Text>
                <Text size="xs" fw={600}>
                  {blastText(blast, d.key)}
                </Text>
              </Group>
              <Box h={4} bg="dark.5" style={{ borderRadius: 2 }}>
                <Box h={4} w={`${Math.max(3, n * 100)}%`} style={{ background: blastColor(n), borderRadius: 2 }} />
              </Box>
            </Box>
          </Tooltip>
        )
      })}
      {trips && trips.length > 0 && (
        <Text size="xs" c="pink.4">
          Human required: {trips.join(' · ')}
        </Text>
      )}
    </Stack>
  )
}

export function CostText({ cost, size = 'xs' }: { cost: Cost; size?: 'xs' | 'sm' }) {
  const total = cost.api + cost.seatEq
  return (
    <Tooltip
      withArrow
      label={
        <Stack gap={0}>
          <Text size="xs">API (Jev, metered): {fmtUsd(cost.api)}</Text>
          <Text size="xs">Seats (list-price equivalent, not billed): ≈{fmtUsd(cost.seatEq)}</Text>
        </Stack>
      }
    >
      <Text size={size} ff="monospace" span style={{ whiteSpace: 'nowrap' }}>
        {total === 0 ? '—' : fmtUsd(cost.api)}
        {cost.seatEq > 0 && (
          <Text span c="dimmed" size={size} ff="monospace">
            {' '}
            +≈{fmtUsd(cost.seatEq)}
          </Text>
        )}

      </Text>
    </Tooltip>
  )
}

export function PersonChip({ login, size = 'sm', note }: { login?: string; size?: 'xs' | 'sm'; note?: ReactNode }) {
  if (!login)
    return (
      <Text size="xs" c="dimmed" fs="italic">
        Unassigned
      </Text>
    )
  const p = PEOPLE[login]
  const name = p?.name ?? login
  const initials = p?.service ? '' : name.split(' ').map((s) => s[0]).join('').slice(0, 2)
  return (
    <Group gap={6} wrap="nowrap">
      <Avatar size={size === 'xs' ? 18 : 22} radius="xl" color={p?.service ? 'gray' : 'active'}>
        {p?.service ? <TbRobot size={12} /> : <Text size="9px" fw={700}>{initials}</Text>}
      </Avatar>
      <Text size={size === 'xs' ? 'xs' : 'sm'} truncate>
        {name}
      </Text>
      {note}
    </Group>
  )
}

/** R1 R2 [R3] … plus the bot-only counter toward the human gate. */
export function RoundPips({ round, botOnly, limit = 3, compact = false }: { round: number; botOnly: number; limit?: number; compact?: boolean }) {
  return (
    <Tooltip
      withArrow
      label={`Round ${round}. Bot-only rounds since last human involvement: ${botOnly}/${limit}${botOnly >= limit ? ' → human gate opens' : ''}`}
    >
      <Group gap={3} wrap="nowrap">
        {Array.from({ length: Math.max(round, compact ? 0 : 4) }, (_, i) => i + 1).map((r) => (
          <Box
            key={r}
            w={compact ? 6 : 16}
            h={compact ? 6 : 14}
            style={{
              borderRadius: compact ? 3 : 3,
              border: '1px solid var(--mantine-color-dark-3)',
              background: r < round ? 'var(--mantine-color-dark-3)' : r === round ? 'var(--mantine-color-active-6)' : 'transparent',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {!compact && (
              <Text size="9px" fw={700} c={r <= round ? 'white' : 'dimmed'}>
                {r}
              </Text>
            )}
          </Box>
        ))}
        {!compact && (
          <Text size="xs" c={botOnly >= limit ? 'pink.4' : 'dimmed'} ml={4}>
            bot-only {botOnly}/{limit}
          </Text>
        )}
      </Group>
    </Tooltip>
  )
}

/** Working-hours age against the team SLA. */
export function SlaBar({ ageWh, slaWh, width = 90 }: { ageWh: number; slaWh: number; width?: number }) {
  const pct = ageWh / slaWh
  const color = pct >= 1 ? 'danger' : pct >= 0.75 ? 'warning' : 'success'
  return (
    <Tooltip withArrow label={`${ageWh.toFixed(1)} working hours of a ${slaWh} wh SLA${pct >= 1 ? ' (breached)' : ''}. Weekends and out-of-hours don't count.`}>
      <Group gap={6} wrap="nowrap">
        <Box w={width} h={6} bg="dark.5" style={{ borderRadius: 3, overflow: 'hidden' }}>
          <Box h={6} w={`${Math.min(100, pct * 100)}%`} bg={`${color}.6`} />
        </Box>
        <Text size="xs" c={pct >= 1 ? 'danger.4' : pct >= 0.75 ? 'warning.4' : 'dimmed'} ff="monospace" style={{ whiteSpace: 'nowrap' }}>
          {ageWh.toFixed(1)}/{slaWh} wh
        </Text>
      </Group>
    </Tooltip>
  )
}

export function SectionLabel({ children, right }: { children: ReactNode; right?: ReactNode }) {
  return (
    <Group justify="space-between" mb={4} wrap="nowrap">
      <Text size="xs" fw={700} c="dimmed" tt="uppercase" style={{ letterSpacing: 0.8 }}>
        {children}
      </Text>
      {right}
    </Group>
  )
}

export function Kpi({ label, value, sub, accent }: { label: string; value: ReactNode; sub?: ReactNode; accent?: string }) {
  return (
    <Paper withBorder p="xs" radius="md" style={accent ? { borderTop: `2px solid var(--mantine-color-${accent}-6)` } : undefined}>
      <Text size="xs" c="dimmed" tt="uppercase" fw={600} style={{ letterSpacing: 0.5 }}>
        {label}
      </Text>
      <Text size="xl" fw={700} lh={1.2}>
        {value}
      </Text>
      {sub && (
        <Text size="xs" c="dimmed" mt={2}>
          {sub}
        </Text>
      )}
    </Paper>
  )
}

/** Tiny inline bar series, no axes. */
export function Spark({ values, width = 90, height = 22, color = 'var(--mantine-color-active-5)' }: { values: number[]; width?: number; height?: number; color?: string }) {
  const max = Math.max(...values, 1)
  const bw = width / values.length
  return (
    <svg width={width} height={height} style={{ display: 'block' }}>
      {values.map((v, i) => {
        const h = Math.max(1, (v / max) * height)
        return <rect key={i} x={i * bw + 0.5} y={height - h} width={Math.max(1, bw - 1.5)} height={h} rx={1} fill={color} opacity={i === values.length - 1 ? 1 : 0.6} />
      })}
    </svg>
  )
}
