/**
 * PROPOSAL MOCK — not production code. Small presentational pieces shared by the review-engine
 * Storybook prototypes. Nothing outside `src/stories/review-engine/` imports this file.
 */
import { Avatar, Badge, Box, Group, Paper, Stack, Text, Tooltip } from '@mantine/core'
import type { ReactNode } from 'react'
import { TbCheck, TbDatabaseX, TbRobot, TbShieldCheck, TbShieldLock, TbUser, TbUsers, TbX } from 'react-icons/tb'
import {
  BLAST_DIMS,
  FLAG_META,
  MODELS,
  PEOPLE,
  SEAT_QUOTA,
  SEVERITY_META,
  STAGE_META,
  TEAMS,
  TRUST,
  TRUST_THRESHOLDS,
  blastNorm,
  blastText,
  effectiveTrust,
  personName,
  thresholdFor,
  type Blast,
  type Cost,
  type Flag,
  type ModelKey,
  type Owner,
  type Severity,
  type Stage,
} from './mockData'

/**
 * The one cost formatter for every review-engine story. Under $1: rounded cents ("5¢", "<1¢" for
 * a non-zero amount under half a cent). $1 or more: rounded whole dollars ("$4"). No decimals.
 */
// eslint-disable-next-line react-refresh/only-export-components -- story-only helper, kept beside CostText on purpose
export function fmtCost(usd: number): string {
  const sign = usd < 0 ? '−' : ''
  const v = Math.abs(usd)
  if (v === 0) return '0¢'
  const cents = Math.round(v * 100)
  if (cents === 0) return `${sign}<1¢`
  if (cents < 100) return `${sign}${cents}¢`
  return `${sign}$${Math.round(v).toLocaleString('en-US')}`
}

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

/** Tooltip body for any cost: actual API spend, the seat estimate, and each seat model's quota. */
export function CostTip({ cost, models }: { cost: Cost; models?: ModelKey[] }) {
  const seats = (models ?? (Object.keys(SEAT_QUOTA) as ModelKey[])).filter((m) => MODELS[m].billing === 'subscription' && SEAT_QUOTA[m])
  return (
    <Stack gap={2}>
      <Text size="xs">
        <b>{fmtCost(cost.api)}</b> actual · metered API (Jev via LiteLLM)
      </Text>
      <Text size="xs">
        <b>≈{fmtCost(cost.seatEq)}</b> API-equivalent estimate · subscription seats (Luna, Haiku, Grok, Sonnet/Opus), not billed
      </Text>
      {cost.seatEq > 0 && seats.length > 0 && (
        <Box mt={2}>
          <Text size="10px" c="dimmed" tt="uppercase" fw={700}>
            Seat quota used
          </Text>
          {seats.map((m) => (
            <Text key={m} size="xs">
              <Text span size="xs" ff="monospace" fw={600}>
                {MODELS[m].short}
              </Text>{' '}
              {SEAT_QUOTA[m]!.pctUsed}% of {SEAT_QUOTA[m]!.window} · {SEAT_QUOTA[m]!.resets}
            </Text>
          ))}
        </Box>
      )}
      <Text size="10px" c="dimmed">
        Estimate = the same tokens at API list price.
      </Text>
    </Stack>
  )
}

/** Dollars first. "≈" marks a total that includes a seat (API-equivalent) estimate; the tooltip splits it. */
export function CostText({ cost, size = 'xs', models, dimmed = false }: { cost: Cost; size?: 'xs' | 'sm' | 'md' | 'lg' | 'xl'; models?: ModelKey[]; dimmed?: boolean }) {
  const total = cost.api + cost.seatEq
  const est = cost.seatEq > 0
  return (
    <Tooltip withArrow multiline w={340} label={<CostTip cost={cost} models={models} />}>
      <Text
        size={size}
        ff="monospace"
        span
        c={dimmed ? 'dimmed' : undefined}
        style={{ whiteSpace: 'nowrap', textDecoration: est ? 'underline dotted' : undefined, textUnderlineOffset: 3, cursor: 'help' }}
      >
        {total === 0 ? '—' : `${est ? '≈' : ''}${fmtCost(total)}`}
      </Text>
    </Tooltip>
  )
}

/** "Trust 82 · payments-api", with what that score allows on this repo in the tooltip. */
export function TrustBadge({ login, repo, size = 'xs', showScope = true }: { login: string; repo: string; size?: 'xs' | 'sm'; showScope?: boolean }) {
  const t = TRUST.find((x) => x.login === login && x.scopeKind === 'repo' && x.scope === repo)
  if (!t) return null
  const v = effectiveTrust(t)
  const teamKey = (Object.values(TEAMS).find((x) => x.repo === repo) ?? TEAMS.payments).key
  const clear = thresholdFor('clear-blocker', teamKey).value
  const gate = thresholdFor('decide-gate', teamKey).value
  const color = v >= clear ? 'success' : v >= gate ? 'yellow' : 'orange'
  return (
    <Tooltip
      withArrow
      multiline
      w={380}
      label={
        <Stack gap={3}>
          <Text size="xs" fw={700}>
            {personName(login)} · trust {v} on {repo}
          </Text>
          <Text size="xs" c="dimmed">
            computed {t.computed} ({t.basis})
            {t.adj &&
              ` · ${t.adj.kind === 'pinned' ? `pinned at ${t.adj.value}` : `offset ${t.adj.value > 0 ? '+' : ''}${t.adj.value}`} by ${personName(t.adj.by)} on ${t.adj.at}${t.adj.expires ? `, expires ${t.adj.expires}` : ''}: ${t.adj.reason}`}
          </Text>
          <Text size="10px" c="dimmed" tt="uppercase" fw={700} mt={2}>
            On {repo} this allows
          </Text>
          {TRUST_THRESHOLDS.map((th) => {
            const need = thresholdFor(th.key, teamKey)
            const ok = v >= need.value
            return (
              <Group key={th.key} gap={4} wrap="nowrap" align="flex-start">
                <Box c={ok ? 'success.4' : 'orange.4'} style={{ display: 'flex', marginTop: 2 }}>
                  {ok ? <TbCheck size={12} /> : <TbX size={12} />}
                </Box>
                <Text size="xs">
                  {th.label}{' '}
                  <Text span size="xs" c="dimmed">
                    (≥ {need.value}
                    {need.source === 'team' ? ', team override' : ''})
                  </Text>
                  {!ok && (
                    <Text span size="xs" c="orange.3">
                      {' '}
                      → {th.below}
                    </Text>
                  )}
                </Text>
              </Group>
            )
          })}
        </Stack>
      }
    >
      <Badge size={size} variant="light" color={color} radius="sm" tt="none" leftSection={<TbShieldCheck size={10} />} style={{ cursor: 'help', flexShrink: 0 }}>
        Trust {v}
        {showScope && <span style={{ opacity: 0.75 }}> · {repo}</span>}
      </Badge>
    </Tooltip>
  )
}

/** "Owners: payments-core, platform-sre": CODEOWNERS can name several teams and users. */
export function OwnersLine({ owners, label = 'Owners', size = 'xs' }: { owners: Owner[]; label?: string; size?: 'xs' | 'sm' }) {
  return (
    <Group gap={4} wrap="wrap">
      <Text size={size} c="dimmed">
        {label}:
      </Text>
      {owners.map((o) => (
        <Tooltip key={o.name} label={o.kind === 'team' ? `team @lumen/${o.name} · ${o.members ?? '?'} member${o.members === 1 ? '' : 's'}` : `user @${o.name} (${personName(o.name)})`}>
          <Badge
            size={size}
            variant="outline"
            color={o.kind === 'team' ? 'indigo' : 'gray'}
            radius="sm"
            tt="none"
            leftSection={o.kind === 'team' ? <TbUsers size={10} /> : <TbUser size={10} />}
          >
            {o.kind === 'user' ? `@${o.name}` : o.name}
          </Badge>
        </Tooltip>
      ))}
    </Group>
  )
}

export function PersonChip({ login, size = 'sm', note, trustRepo }: { login?: string; size?: 'xs' | 'sm'; note?: ReactNode; trustRepo?: string }) {
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
      {trustRepo && <TrustBadge login={login} repo={trustRepo} showScope={false} />}
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
