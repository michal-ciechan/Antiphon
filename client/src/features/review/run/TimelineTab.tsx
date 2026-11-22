import { Badge, Box, Group, Paper, ScrollArea, Stack, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { useMemo, useState } from 'react'
import type { NodeExecutionDto, ReviewGateDto, ReviewRunDetailDto } from '../../../api/reviewEngine'
import { CostText } from '../components/CostText'
import { ModelBadge } from '../components/ReviewBadges'
import { SectionLabel } from '../components/SectionLabel'
import { NODE_STATUS_META, modelColor } from '../reviewMeta'
import { costSplit, fmtDuration, fmtTokens } from '../reviewFormat'
import { iterationsOf } from './workflow/overlay'

const LABEL_W = 150
const LANE_H = 18
const PARK_KINDS = new Set(['Wait', 'Gate'])

interface Track {
  iteration: number
  start: number
  bars: { exec: NodeExecutionDto; start: number; end: number; lane: number }[]
  parks: NodeExecutionDto[]
  /** steps that ran while the iteration was parked (e.g. record-drift) */
  during: NodeExecutionDto[]
  gates: ReviewGateDto[]
  cost: number
  split: { actual: number; estimate: number }
  computeMin: number
  route: string | null
}

function buildTracks(run: ReviewRunDetailDto, now: number): Track[] {
  const iterations = iterationsOf(run)
  return iterations.map((iteration, i) => {
    const execs = run.nodes.filter((n) => n.iteration === iteration)
    const timed = execs.filter((e) => e.startedAt)
    const start = Math.min(...timed.map((e) => Date.parse(e.startedAt!)), now)
    const next = iterations[i + 1]
    const nextStart = next === undefined ? Infinity : Math.min(...run.nodes.filter((n) => n.iteration === next && n.startedAt).map((n) => Date.parse(n.startedAt!)))
    const parks = execs.filter((e) => PARK_KINDS.has(e.kind))
    const parkStart = Math.min(...parks.filter((p) => p.startedAt).map((p) => Date.parse(p.startedAt!)), Infinity)
    const duringPark = (e: NodeExecutionDto) => !PARK_KINDS.has(e.kind) && Date.parse(e.startedAt!) > parkStart
    let lane = 0
    const bars = timed
      .filter((e) => !PARK_KINDS.has(e.kind) && e.kind !== 'FanOut' && !duringPark(e))
      .map((e) => {
        const s = (Date.parse(e.startedAt!) - start) / 60_000
        const end = ((e.completedAt ? Date.parse(e.completedAt) : now) - start) / 60_000
        return { exec: e, start: s, end: Math.max(end, s + 0.02), lane: e.kind === 'Agent' ? ++lane : 0 }
      })
    const gates = run.gates.filter((g) => g.decidedAt && Date.parse(g.decidedAt) >= start && Date.parse(g.decidedAt) < nextStart)
    return {
      iteration,
      start,
      bars,
      parks,
      during: timed.filter(duringPark),
      gates,
      cost: execs.reduce((s, e) => s + e.costUsd, 0),
      split: costSplit(execs),
      computeMin: Math.max(0, ...bars.map((b) => b.end)),
      route: execs.find((e) => e.nodeId === 'route')?.summary ?? null,
    }
  })
}

function parkText(p: NodeExecutionDto, now: number): string {
  const how = p.status === 'Waiting' ? `parked ${fmtDuration(p.startedAt, null, now)} so far` : `parked ${fmtDuration(p.startedAt, p.completedAt, now)}`
  return `${how} on ${p.nodeId}${p.summary ? ` → ${p.summary}` : ''}`
}

/** Iterations and rounds: compute bars on one minute scale (parks compressed into pills), gate decisions, cost. */
export function TimelineTab({ run, now }: { run: ReviewRunDetailDto; now: number }) {
  const tracks = useMemo(() => buildTracks(run, now), [run, now])
  const [selected, setSelected] = useState<string | null>(null)
  const maxMin = Math.max(1, ...tracks.map((t) => t.computeMin))
  const scale = Math.min(140, 820 / maxMin)
  const ticks = [1, 2, 5, 10, 15, 30, 60, 120].find((m) => m * scale >= 44) ?? 240
  const sel = tracks.flatMap((t) => t.bars).find((b) => b.exec.id === selected)?.exec

  if (tracks.length === 0)
    return (
      <Text size="sm" c="dimmed">
        Nothing has run yet.
      </Text>
    )

  return (
    <Stack gap="sm">
      <Paper withBorder radius="md" p="xs">
        <SectionLabel>Iterations ribbon</SectionLabel>
        <Group gap={0} wrap="nowrap" align="stretch">
          {tracks.map((t, i) => {
            const last = i === tracks.length - 1
            return (
              <Group key={t.iteration} gap={0} wrap="nowrap" style={{ flex: t.parks.length ? 3 : 2 }} align="stretch">
                <Paper
                  p={6}
                  radius="sm"
                  style={{ flex: 1, border: `1px solid ${last ? '#3b7bf5' : 'var(--mantine-color-dark-4)'}`, background: last ? 'rgba(59,123,245,0.10)' : undefined }}
                >
                  <Text size="xs" fw={700}>
                    it{t.iteration}{' '}
                    <Text span size="xs" c="dimmed" fw={400}>
                      {new Date(t.start).toLocaleString(undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' })}
                    </Text>
                  </Text>
                  <Text size="xs" c="dimmed">
                    {t.computeMin.toFixed(1)} min compute · {t.route ? `route: ${t.route}` : last && run.status === 'Running' ? 'running' : 'no route yet'}
                  </Text>
                  <CostText usd={t.cost} split={t.split} />
                </Paper>
                {t.parks.map((p) => (
                  <Box key={p.id} px={8} style={{ flex: 1, minWidth: 150 }}>
                    <Box mt={10} h={2} style={{ background: `repeating-linear-gradient(90deg, ${p.kind === 'Wait' ? '#ffa92e' : '#f06595'} 0 6px, transparent 6px 10px)` }} />
                    <Text size="10px" ta="center" c={p.kind === 'Wait' ? 'orange.4' : 'pink.4'} mt={2}>
                      {parkText(p, now)}
                    </Text>
                    {t.during.map((d) => (
                      <Text key={d.id} size="10px" ta="center" c="cyan.4">
                        ↳ {d.nodeId}: {d.summary}
                      </Text>
                    ))}
                    {t.gates.map((g) => (
                      <Text key={g.id} size="10px" ta="center" c="dimmed">
                        @{g.assigneeLogin ?? '?'} → {g.decision === 'AnotherRound' ? 'another round' : g.decision?.toLowerCase()}
                        {g.steerPrompt ? ` · steer: "${g.steerPrompt}"` : ''}
                      </Text>
                    ))}
                  </Box>
                ))}
              </Group>
            )
          })}
        </Group>
        <Text size="xs" c="dimmed" mt={6}>
          Round {run.round} now · bot-only rounds {run.botOnlyRounds}. The contract does not map iterations to rounds yet; a gate's "another round" and a push each start one.
        </Text>
      </Paper>

      <Paper withBorder radius="md" p="xs">
        <SectionLabel>Steps by iteration (minutes from the iteration start, one scale) · click a bar</SectionLabel>
        <ScrollArea type="auto">
          <Box pos="relative" h={16} miw={LABEL_W + maxMin * scale + 40}>
            {Array.from({ length: Math.floor(maxMin / ticks) + 1 }, (_, k) => k * ticks).map((m) => (
              <Text key={m} size="10px" c="dimmed" pos="absolute" left={LABEL_W + m * scale} top={0}>
                {m}m
              </Text>
            ))}
          </Box>
          {tracks.map((t) => {
            const lanes = Math.max(0, ...t.bars.map((b) => b.lane)) + 1
            return (
              <Box key={t.iteration} pos="relative" miw={LABEL_W + maxMin * scale + 40} h={lanes * LANE_H + 10} style={{ borderTop: '1px solid var(--mantine-color-dark-5)' }}>
                <Box pos="absolute" left={0} top={4} w={LABEL_W - 10}>
                  <Text size="xs" fw={700}>
                    it{t.iteration}
                  </Text>
                  <CostText usd={t.cost} split={t.split} />
                </Box>
                {t.bars.map((b) => {
                  const e = b.exec
                  const w = Math.max(6, (b.end - b.start) * scale)
                  const color = e.model ? `var(--mantine-color-${modelColor(e.model)}-7)` : 'var(--mantine-color-gray-7)'
                  const label = e.kind === 'Agent' ? (e.summary?.split(':')[0] ?? e.nodeId) : e.nodeId
                  return (
                    <Tooltip
                      key={e.id}
                      withArrow
                      multiline
                      w={300}
                      label={`${e.nodeId} · ${NODE_STATUS_META[e.status].label} · ${fmtDuration(e.startedAt, e.completedAt, now)}${e.model ? ` · ${e.model}` : ''}${e.summary ? `\n${e.summary}` : ''}`}
                    >
                      <UnstyledButton
                        onClick={() => setSelected(e.id)}
                        pos="absolute"
                        left={LABEL_W + b.start * scale}
                        top={4 + b.lane * LANE_H}
                        w={w}
                        h={LANE_H - 3}
                        aria-label={`${e.nodeId} iteration ${t.iteration}`}
                        style={{
                          background: color,
                          borderRadius: 3,
                          outline: selected === e.id ? '2px solid white' : undefined,
                          borderRight: e.status === 'Running' ? '3px dashed #3b7bf5' : undefined,
                          animation: e.status === 'Running' ? 'reviewPulse 1.6s infinite' : undefined,
                          overflow: 'hidden',
                          paddingLeft: 4,
                          opacity: e.status === 'Failed' ? 1 : 0.95,
                          boxShadow: e.status === 'Failed' ? 'inset 0 0 0 2px var(--mantine-color-danger-6)' : undefined,
                        }}
                      >
                        {w > 46 && (
                          <Text size="10px" c="white" truncate fw={600}>
                            {label}
                            {e.status === 'Running' ? ' …' : ''}
                          </Text>
                        )}
                      </UnstyledButton>
                    </Tooltip>
                  )
                })}
              </Box>
            )
          })}
        </ScrollArea>
        <Text size="10px" c="dimmed" mt={4}>
          Bars are coloured by model (grey: code, no model). Parks on wait and gate steps hold no seat and are shown in the ribbon, not to scale.
        </Text>
      </Paper>

      {sel && (
        <Paper withBorder radius="md" p="xs">
          <Group gap="md" wrap="wrap">
            <Badge variant="light">{sel.nodeId}</Badge>
            <Text size="xs">
              it{sel.iteration} · {NODE_STATUS_META[sel.status].label} · {fmtDuration(sel.startedAt, sel.completedAt, now)}
            </Text>
            {sel.model && <ModelBadge model={sel.model} />}
            {sel.inputTokens > 0 && (
              <Text size="xs" ff="monospace" c="dimmed">
                {fmtTokens(sel.inputTokens)} in · {fmtTokens(sel.outputTokens)} out
              </Text>
            )}
            <CostText usd={sel.costUsd} />
            <Text size="xs" c="dimmed">
              {sel.summary}
            </Text>
          </Group>
        </Paper>
      )}
    </Stack>
  )
}
