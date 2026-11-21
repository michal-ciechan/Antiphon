import type { Meta, StoryObj } from '@storybook/react'
import { useMemo, useState } from 'react'
import {
  Badge,
  Box,
  Chip,
  Group,
  Paper,
  Progress,
  ScrollArea,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import {
  TbCircleCheckFilled,
  TbCircleXFilled,
  TbClockHour4,
  TbSearch,
  TbArrowRight,
  TbUserQuestion,
  TbSquare,
  TbSquareCheckFilled,
  TbFilterOff,
  TbX,
} from 'react-icons/tb'
import {
  DAILY,
  MODELS,
  PROVIDER_USAGE_TODAY,
  QUEUE,
  REPOS,
  RUNS,
  SEVERITY_META,
  STAGES,
  STAGE_META,
  fmtTokens,
  fmtUsd,
  personName,
  runRef,
  type ModelKey,
  type Repo,
  type ReviewRun,
  type Stage,
} from './mockData'
import {
  BlastMini,
  CostText,
  ModelStack,
  PersonChip,
  RoundPips,
  SectionLabel,
  SlaBar,
  Spark,
  StageChip,
} from './shared'

/**
 * PROPOSAL MOCK — not production code. Review runs dashboard for
 * `docs/features/012-review-engine/design.md` §17 (Review runs page): PRs by stage, number in
 * review, queue depth, models/providers in use, tokens and cost per run, cost per accepted
 * finding. Three distinct layouts: a dense table, a stage board, and a KPI overview grouped by
 * whose move it is.
 */

function StatusIcon({ state }: { state: ReviewRun['statusReview']['state'] }) {
  if (state === 'success') return <TbCircleCheckFilled size={14} color="var(--mantine-color-success-5)" />
  if (state === 'failure') return <TbCircleXFilled size={14} color="var(--mantine-color-danger-5)" />
  return <TbClockHour4 size={14} color="var(--mantine-color-warning-5)" />
}

function AuthorCell({ run }: { run: ReviewRun }) {
  const ra = run.resolvedAuthor
  if (!ra) return <PersonChip login={run.author} size="xs" />
  if (ra === 'unknown')
    return (
      <Group gap={4} wrap="nowrap">
        <PersonChip login={run.author} size="xs" />
        <Tooltip label="Service account; no signal resolved a human author → team queue tagged 'author unknown'">
          <Badge size="xs" color="warning" variant="light" leftSection={<TbUserQuestion size={10} />}>
            author unknown
          </Badge>
        </Tooltip>
      </Group>
    )
  return (
    <Tooltip label={`Resolved: ${ra.via} (p=${ra.confidence.toFixed(2)})`}>
      <Group gap={4} wrap="nowrap">
        <Text size="xs" c="dimmed" truncate>
          {run.author}
        </Text>
        <TbArrowRight size={10} />
        <Text size="xs" truncate>
          {personName(ra.login)}
        </Text>
      </Group>
    </Tooltip>
  )
}

function FindingsCell({ run }: { run: ReviewRun }) {
  const f = run.findings
  if (f.posted === 0 && f.open === 0)
    return (
      <Text size="xs" c="dimmed">
        —
      </Text>
    )
  return (
    <Tooltip
      label={`${f.open} open · ${f.posted} posted · ${f.accepted} accepted · ${f.dismissed} dismissed · ${f.belowBar} below bar (stored, not posted)`}
    >
      <Group gap={4} wrap="nowrap">
        <Text size="sm" fw={700} c={run.topSeverity && f.open > 0 ? `${SEVERITY_META[run.topSeverity].color}.5` : undefined}>
          {f.open}
        </Text>
        <Text size="xs" c="dimmed">
          open / {f.posted}
        </Text>
        {f.accepted > 0 && (
          <Text size="xs" c="success.5">
            ✓{f.accepted}
          </Text>
        )}
      </Group>
    </Tooltip>
  )
}

function FanOut({ run }: { run: ReviewRun }) {
  if (!run.fanOut) return null
  const { done, total } = run.fanOut
  return (
    <Group gap={2} wrap="nowrap" mt={3}>
      {Array.from({ length: total }, (_, i) => (
        <Box
          key={i}
          w={14}
          h={4}
          style={{ borderRadius: 2 }}
          bg={i < done ? 'success.6' : i === done ? 'active.5' : 'dark.4'}
        />
      ))}
    </Group>
  )
}

// ---------------------------------------------------------------- (A) dense table

function DenseTable() {
  const [repo, setRepo] = useState<'all' | Repo>('all')
  const [stages, setStages] = useState<string[]>([])
  const [models, setModels] = useState<ModelKey[]>([])
  const [deepRounds, setDeepRounds] = useState(false)
  const [q, setQ] = useState('')
  const [groupBy, setGroupBy] = useState<'none' | 'stage' | 'repo'>('stage')

  const rows = useMemo(
    () =>
      RUNS.filter((r) => repo === 'all' || r.repo === repo)
        .filter((r) => stages.length === 0 || stages.includes(r.stage))
        .filter((r) => models.length === 0 || r.models.some((m) => models.includes(m)))
        .filter((r) => !deepRounds || r.round >= 3)
        .filter((r) => !q || `${runRef(r)} ${r.title} ${r.author}`.toLowerCase().includes(q.toLowerCase())),
    [repo, stages, models, deepRounds, q],
  )
  const filters: StripFilters = { stages, onStages: setStages, models, onModels: setModels, deepRounds, onDeepRounds: setDeepRounds }

  const groups: { key: string; label: React.ReactNode; runs: ReviewRun[] }[] =
    groupBy === 'none'
      ? [{ key: 'all', label: null, runs: rows }]
      : groupBy === 'stage'
        ? STAGES.map((s) => ({ key: s, label: <StageChip stage={s} size="xs" />, runs: rows.filter((r) => r.stage === s) })).filter((g) => g.runs.length > 0)
        : REPOS.map((rp) => ({ key: rp, label: <Text size="xs" fw={700} ff="monospace">lumen/{rp}</Text>, runs: rows.filter((r) => r.repo === rp) })).filter((g) => g.runs.length > 0)

  const totalApi = rows.reduce((a, r) => a + r.cost.api, 0)
  const totalSeat = rows.reduce((a, r) => a + r.cost.seatEq, 0)
  const inReview = rows.filter((r) => ['intake', 'categorising', 'selecting', 'reviewing'].includes(r.stage)).length

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Text fw={700} size="lg">
            Review runs
          </Text>
          <Text size="xs" c="dimmed">
            {rows.length} runs · {inReview} in review · {rows.filter((r) => r.stage === 'human-gate').length} at a human gate · live (SignalR)
          </Text>
        </Box>
        <Group gap="xs">
          <TextInput size="xs" placeholder="Search PR, title, author" leftSection={<TbSearch size={12} />} value={q} onChange={(e) => setQ(e.currentTarget.value)} w={220} />
          <SegmentedControl size="xs" value={repo} onChange={(v) => setRepo(v as 'all' | Repo)} data={[{ value: 'all', label: 'All repos' }, ...REPOS.map((r) => ({ value: r, label: r }))]} />
          <SegmentedControl size="xs" value={groupBy} onChange={(v) => setGroupBy(v as typeof groupBy)} data={[{ value: 'none', label: 'Flat' }, { value: 'stage', label: 'By stage' }, { value: 'repo', label: 'By repo' }]} />
        </Group>
      </Group>

      <KpiStrip f={filters} />

      <Chip.Group multiple value={stages} onChange={setStages}>
        <Group gap={4}>
          {STAGES.map((s) => (
            <Chip key={s} value={s} size="xs" color={STAGE_META[s].color} variant="light">
              {STAGE_META[s].label} {RUNS.filter((r) => r.stage === s).length}
            </Chip>
          ))}
        </Group>
      </Chip.Group>

      <ActiveFilters f={filters} shown={rows.length} search={q} repo={repo} />

      <Paper withBorder radius="md">
        <Table.ScrollContainer minWidth={1480} type="native">
          <Table verticalSpacing={6} horizontalSpacing="sm" fz="sm" highlightOnHover stickyHeader>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Pull request</Table.Th>
                <Table.Th w={200}>Stage</Table.Th>
                <Table.Th w={110}>Round</Table.Th>
                <Table.Th w={60}>
                  <Tooltip label="Blast radius: reach · contract · data · rollback · reversal · exposure">
                    <span>Blast</span>
                  </Tooltip>
                </Table.Th>
                <Table.Th w={110}>Findings</Table.Th>
                <Table.Th w={170}>Models</Table.Th>
                <Table.Th w={90} ta="right">
                  Tokens
                </Table.Th>
                <Table.Th w={130} ta="right">
                  Cost
                </Table.Th>
                <Table.Th w={260}>antiphon/review</Table.Th>
                <Table.Th w={80} ta="right">
                  Updated
                </Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {groups.map((g) => (
                <GroupRows key={g.key} label={g.label} runs={g.runs} />
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <Group justify="space-between" px="sm" py={6} style={{ borderTop: '1px solid var(--mantine-color-dark-4)' }}>
          <Text size="xs" c="dimmed">
            Cost: metered API (Jev via LiteLLM) + list-price equivalent of subscription seats (not billed).
          </Text>
          <Text size="xs" ff="monospace">
            Σ {fmtUsd(totalApi)} API + ≈{fmtUsd(totalSeat)} seats · {fmtTokens(rows.reduce((a, r) => a + r.tokensIn, 0))} in
          </Text>
        </Group>
      </Paper>
    </Stack>
  )
}

function GroupRows({ label, runs }: { label: React.ReactNode; runs: ReviewRun[] }) {
  return (
    <>
      {label && (
        <Table.Tr bg="dark.6">
          <Table.Td colSpan={10} py={4}>
            <Group gap={8}>
              {label}
              <Text size="xs" c="dimmed">
                {runs.length}
              </Text>
            </Group>
          </Table.Td>
        </Table.Tr>
      )}
      {runs.map((r) => (
        <Table.Tr key={r.id} style={{ cursor: 'pointer', opacity: r.stage === 'merged' ? 0.6 : 1 }}>
          <Table.Td maw={380}>
            <Group gap={6} wrap="nowrap">
              <Text size="xs" ff="monospace" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                {runRef(r)}
              </Text>
              <Text size="sm" truncate fw={500}>
                {r.title}
              </Text>
            </Group>
            <Group gap={8} wrap="nowrap" mt={2}>
              <AuthorCell run={r} />
              <Text size="xs" c="dimmed" ff="monospace">
                +{r.changed.add} −{r.changed.del} · {r.changed.files} files
              </Text>
            </Group>
          </Table.Td>
          <Table.Td>
            <StageChip stage={r.stage} size="xs" />
            <Text size="xs" c="dimmed" truncate mt={2}>
              {r.stageDetail}
              {r.assignee && ` · ${personName(r.assignee)}`}
            </Text>
            <FanOut run={r} />
          </Table.Td>
          <Table.Td>
            <RoundPips round={r.round} botOnly={r.botOnlyRounds} compact />
            <Text size="xs" c="dimmed" mt={2} style={{ whiteSpace: 'nowrap' }}>
              R{r.round} · it{r.iteration} · bot {r.botOnlyRounds}/3
            </Text>
          </Table.Td>
          <Table.Td>
            <BlastMini blast={r.blast} />
          </Table.Td>
          <Table.Td>
            <FindingsCell run={r} />
          </Table.Td>
          <Table.Td>
            <ModelStack models={r.models} max={5} />
          </Table.Td>
          <Table.Td ta="right">
            <Text size="xs" ff="monospace">
              {fmtTokens(r.tokensIn)}
            </Text>
            <Text size="xs" ff="monospace" c="dimmed">
              {fmtTokens(r.tokensOut)} out
            </Text>
          </Table.Td>
          <Table.Td ta="right">
            <CostText cost={r.cost} />
          </Table.Td>
          <Table.Td>
            <Group gap={6} wrap="nowrap">
              <StatusIcon state={r.statusReview.state} />
              <Text size="xs" truncate>
                {r.statusReview.text}
              </Text>
            </Group>
            {r.statusApproval === 'success' && (
              <Text size="xs" c="success.5">
                antiphon/approval ✓ @ {r.headSha}
              </Text>
            )}
          </Table.Td>
          <Table.Td ta="right">
            <Text size="xs" c="dimmed">
              {r.updated}
            </Text>
          </Table.Td>
        </Table.Tr>
      ))}
    </>
  )
}

// ---------------------------------------------------------------- (B) stage board

function BoardCard({ run }: { run: ReviewRun }) {
  const gateItem = QUEUE.find((q) => q.subject.startsWith(runRef(run)))
  const done = run.stage === 'merged' || run.stage === 'approved'
  return (
    <Paper withBorder radius="sm" p={8} style={{ opacity: run.stage === 'merged' ? 0.55 : 1, cursor: 'pointer' }}>
      <Group justify="space-between" gap={4} wrap="nowrap">
        <Text size="xs" ff="monospace" c="dimmed">
          {runRef(run)}
        </Text>
        <RoundPips round={run.round} botOnly={run.botOnlyRounds} compact />
      </Group>
      <Text size="sm" fw={500} lineClamp={2} mt={2} lh={1.3}>
        {run.title}
      </Text>
      <Box mt={4}>
        <AuthorCell run={run} />
      </Box>
      <Text size="xs" c="dimmed" mt={4}>
        {run.stageDetail}
      </Text>
      <FanOut run={run} />
      {run.stage === 'human-gate' && (
        <Stack gap={2} mt={6}>
          <PersonChip login={run.assignee} size="xs" />
          {gateItem && <SlaBar ageWh={gateItem.ageWh} slaWh={gateItem.slaWh} width={70} />}
        </Stack>
      )}
      {!done && run.findings.open > 0 && (
        <Group gap={4} mt={6}>
          {run.topSeverity && (
            <Badge size="xs" color={SEVERITY_META[run.topSeverity].color} variant="light">
              {run.findings.open} open · top {run.topSeverity}
            </Badge>
          )}
        </Group>
      )}
      <Group justify="space-between" mt={6} wrap="nowrap" gap={4}>
        <ModelStack models={run.models} max={3} />
        <BlastMini blast={run.blast} height={12} />
      </Group>
      <Group justify="space-between" mt={4}>
        <CostText cost={run.cost} />
        <Text size="xs" c="dimmed">
          {run.updated}
        </Text>
      </Group>
    </Paper>
  )
}

function StageBoard() {
  const [swim, setSwim] = useState(false)
  const lanes: (Repo | null)[] = swim ? REPOS : [null]
  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Box>
          <Text fw={700} size="lg">
            Review runs · board
          </Text>
          <Text size="xs" c="dimmed">
            One column per stage. Column header: count · Σ cost. Reviewer pips: green done, blue running, grey queued.
          </Text>
        </Box>
        <Switch size="xs" label="Swimlanes by repo" checked={swim} onChange={(e) => setSwim(e.currentTarget.checked)} />
      </Group>
      <ScrollArea type="auto" offsetScrollbars>
        <Box style={{ display: 'grid', gridTemplateColumns: `${swim ? '110px ' : ''}repeat(${STAGES.length}, 220px)`, gap: 8 }}>
          {swim && <Box />}
          {STAGES.map((s) => {
            const runs = RUNS.filter((r) => r.stage === s)
            const cost = runs.reduce((a, r) => a + r.cost.api + r.cost.seatEq, 0)
            return (
              <Paper key={s} p={6} radius="sm" bg="dark.6" style={{ borderTop: `3px solid var(--mantine-color-${STAGE_META[s].color}-6)` }}>
                <Group justify="space-between" wrap="nowrap">
                  <Text size="sm" fw={700}>
                    {STAGE_META[s].label}
                  </Text>
                  <Badge size="sm" variant="filled" color={STAGE_META[s].color}>
                    {runs.length}
                  </Badge>
                </Group>
                <Text size="xs" c="dimmed">
                  ≈{fmtUsd(cost)}
                  {s === 'reviewing' && ' · 7 seats busy'}
                  {s === 'human-gate' && ' · oldest 5.6 wh'}
                  {s === 'intake' && ' · 2 queued ahead'}
                </Text>
              </Paper>
            )
          })}
          {lanes.map((lane) => (
            <LaneRow key={lane ?? 'all'} lane={lane} swim={swim} />
          ))}
        </Box>
      </ScrollArea>
    </Stack>
  )
}

function LaneRow({ lane, swim }: { lane: Repo | null; swim: boolean }) {
  return (
    <>
      {swim && (
        <Box pt={6}>
          <Text size="xs" fw={700} ff="monospace">
            {lane}
          </Text>
          <Text size="xs" c="dimmed">
            {RUNS.filter((r) => r.repo === lane).length} runs
          </Text>
        </Box>
      )}
      {STAGES.map((s) => (
        <Stack key={s} gap={6} p={4} bg={swim ? 'dark.7' : undefined} style={{ borderRadius: 4, minHeight: swim ? 60 : 400 }}>
          {RUNS.filter((r) => r.stage === s && (lane === null || r.repo === lane)).map((r) => (
            <BoardCard key={r.id} run={r} />
          ))}
        </Stack>
      ))}
    </>
  )
}

// ---------------------------------------------------------------- (C) KPI strip + whose-move list

const MOVE_GROUPS: { key: string; title: string; hint: string; stages: Stage[]; color: string }[] = [
  { key: 'humans', title: 'Waiting on humans', hint: 'Gates parked with no seat held', stages: ['human-gate'], color: 'pink' },
  { key: 'antiphon', title: 'Antiphon is working', hint: 'Intake → reviewers', stages: ['intake', 'categorising', 'selecting', 'reviewing'], color: 'active' },
  { key: 'authors', title: 'Waiting on authors', hint: 'Findings posted, parked on await-fixes', stages: ['waiting-author'], color: 'warning' },
  { key: 'done', title: 'Approved / merged', hint: 'Merge left to the author', stages: ['approved', 'merged'], color: 'success' },
]

const IN_REVIEW: Stage[] = ['intake', 'categorising', 'selecting', 'reviewing']
const AT_GATE: Stage[] = ['human-gate']

interface StripFilters {
  stages: string[]
  onStages: (s: string[]) => void
  models: ModelKey[]
  onModels: (m: ModelKey[]) => void
  deepRounds: boolean
  onDeepRounds: (v: boolean) => void
}

function hasAll(f: StripFilters, ss: Stage[]): boolean {
  return ss.every((s) => f.stages.includes(s))
}

function toggleStages(f: StripFilters, ss: Stage[]) {
  f.onStages(hasAll(f, ss) ? f.stages.filter((s) => !ss.includes(s as Stage)) : [...new Set([...f.stages, ...ss])])
}

/** A KPI card. With onToggle it is a filter: corner checkbox, accent border and tint when on. */
function ToggleKpi({
  label,
  value,
  sub,
  accent,
  selected = false,
  onToggle,
  hint,
}: {
  label: string
  value: React.ReactNode
  sub?: React.ReactNode
  accent: string
  selected?: boolean
  onToggle?: () => void
  hint?: string
}) {
  const interactive = !!onToggle
  const card = (
    <Paper
      withBorder
      p="xs"
      radius="md"
      h="100%"
      style={{
        borderTopWidth: 2,
        borderTopColor: `var(--mantine-color-${accent}-6)`,
        borderRightColor: selected ? `var(--mantine-color-${accent}-5)` : undefined,
        borderBottomColor: selected ? `var(--mantine-color-${accent}-5)` : undefined,
        borderLeftColor: selected ? `var(--mantine-color-${accent}-5)` : undefined,
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
      <Text size="xl" fw={700} lh={1.2}>
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

function ProviderSplit({ models, onModels }: { models?: ModelKey[]; onModels?: (m: ModelKey[]) => void }) {
  const interactive = !!onModels
  const sel = models ?? []
  const anySel = interactive && sel.length > 0
  const dim = (m: ModelKey) => (anySel && !sel.includes(m) ? 0.3 : 1)
  const total = PROVIDER_USAGE_TODAY.reduce((a, p) => a + p.calls, 0)
  const jev = PROVIDER_USAGE_TODAY.find((p) => p.model === 'jev')!
  const seats = PROVIDER_USAGE_TODAY.filter((p) => p.model !== 'jev')
  const seatCalls = seats.reduce((a, p) => a + p.calls, 0)
  return (
    <Paper
      withBorder
      p="xs"
      radius="md"
      h="100%"
      style={{
        borderTopWidth: 2,
        borderTopColor: 'var(--mantine-color-grape-6)',
        borderRightColor: anySel ? 'var(--mantine-color-grape-5)' : undefined,
        borderBottomColor: anySel ? 'var(--mantine-color-grape-5)' : undefined,
        borderLeftColor: anySel ? 'var(--mantine-color-grape-5)' : undefined,
        boxShadow: anySel ? 'inset 0 0 0 1px var(--mantine-color-grape-5)' : undefined,
      }}
    >
      <Group justify="space-between" wrap="nowrap">
        <Text size="xs" c="dimmed" tt="uppercase" fw={600} style={{ letterSpacing: 0.5, whiteSpace: 'nowrap' }}>
          Models & providers today
        </Text>
        <Text size="xs" c={anySel ? 'grape.3' : 'dimmed'} truncate>
          {anySel ? `filtering: runs using ${sel.map((m) => MODELS[m].short).join(' or ')}` : `${total.toLocaleString()} calls`}
        </Text>
      </Group>
      <Progress.Root size={14} mt={6}>
        <Tooltip label={`Jev via LiteLLM (metered): ${jev.calls} calls · ${fmtUsd(jev.cost.api)}`}>
          <Progress.Section value={(jev.calls / total) * 100} color="cyan" style={{ opacity: dim('jev') }}>
            <Progress.Label>Jev {Math.round((jev.calls / total) * 100)}%</Progress.Label>
          </Progress.Section>
        </Tooltip>
        {seats.map((p) => (
          <Tooltip key={p.model} label={`${MODELS[p.model].name} seat: ${p.calls} requests · quota ${p.quotaPct}%`}>
            <Progress.Section value={Math.max(1.5, (p.calls / total) * 100)} color={MODELS[p.model].color} style={{ opacity: dim(p.model) }} />
          </Tooltip>
        ))}
      </Progress.Root>
      <Group gap="md" mt={6} wrap="nowrap">
        <Box>
          <Text size="xs" c="cyan.4" fw={600}>
            API (Jev)
          </Text>
          <Text size="xs" ff="monospace">
            {jev.calls.toLocaleString()} calls · {fmtTokens(jev.tokensIn)} · {fmtUsd(jev.cost.api)}
          </Text>
        </Box>
        <Box>
          <Text size="xs" c="grape.4" fw={600}>
            Subscription seats
          </Text>
          <Text size="xs" ff="monospace">
            {seatCalls} requests · quota{' '}
            {seats
              .filter((p) => (p.quotaPct ?? 0) >= 10)
              .map((p) => `${MODELS[p.model].short} ${p.quotaPct}%`)
              .join(' · ')}
          </Text>
        </Box>
        <Box>
          <Text size="xs" c="warning.4" fw={600}>
            Escalations
          </Text>
          <Text size="xs" ff="monospace">
            Jev→luna 61 · →haiku 9 · →grok 3
          </Text>
        </Box>
      </Group>
      {interactive && (
        <Chip.Group multiple value={sel} onChange={(v) => onModels!(v as ModelKey[])}>
          <Group gap={4} mt={6}>
            {PROVIDER_USAGE_TODAY.map((p) => (
              <Chip key={p.model} value={p.model} size="xs" color={MODELS[p.model].color} variant="light">
                {MODELS[p.model].short} {RUNS.filter((r) => r.models.includes(p.model)).length}
              </Chip>
            ))}
          </Group>
        </Chip.Group>
      )}
    </Paper>
  )
}

/** The KPI strip. Pass `f` to make the cards filters (dense table); omit it for a read-only strip (overview). */
function KpiStrip({ f }: { f?: StripFilters }) {
  const inReview = RUNS.filter((r) => IN_REVIEW.includes(r.stage)).length
  const atGate = RUNS.filter((r) => r.stage === 'human-gate').length
  const queueOther = QUEUE.filter((x) => x.state !== 'done' && ['review-gate', 'approval-gate', 'escalated-dispute'].includes(x.kind)).length - atGate
  const deep = RUNS.filter((r) => r.round >= 3).length
  const today = DAILY[DAILY.length - 1]
  const week = DAILY.slice(-7)
  const accepted7 = week.reduce((a, d) => a + d.accepted, 0)
  const cost7 = week.reduce((a, d) => a + d.costApi + d.costSeatEq, 0)
  const api7 = week.reduce((a, d) => a + d.costApi, 0)
  return (
    <Box style={{ display: 'grid', gridTemplateColumns: 'repeat(5, minmax(130px, 1fr)) minmax(480px, 2.6fr)', gap: 8 }}>
      <ToggleKpi
        label="In review"
        value={inReview}
        accent="active"
        sub={<Spark values={DAILY.map((d) => d.gatesOpened + 2)} width={100} />}
        selected={!!f && hasAll(f, IN_REVIEW)}
        onToggle={f ? () => toggleStages(f, IN_REVIEW) : undefined}
        hint="intake, categorising, selecting, reviewing"
      />
      <ToggleKpi
        label="Waiting on humans"
        value={atGate}
        accent="pink"
        sub={
          <>
            runs at a gate · +{queueOther} disputes &amp; pre-assigned in queues · oldest 5.6 wh ·{' '}
            <Text span c="danger.4" size="xs">
              2 over SLA
            </Text>
          </>
        }
        selected={!!f && hasAll(f, AT_GATE)}
        onToggle={f ? () => toggleStages(f, AT_GATE) : undefined}
        hint="runs parked on a human gate"
      />
      <ToggleKpi
        label="Avg rounds"
        value="1.8"
        accent="gray"
        sub={`to approval, 7d · p90 3 · ${deep} runs at round 3+`}
        selected={!!f && f.deepRounds}
        onToggle={f ? () => f.onDeepRounds(!f.deepRounds) : undefined}
        hint="runs at round 3 or later"
      />
      <ToggleKpi
        label="Cost today"
        value={
          <Text span fw={700} size="xl" ff="monospace">
            {fmtUsd(today.costApi)}
          </Text>
        }
        accent="cyan"
        sub={<>API · +≈{fmtUsd(today.costSeatEq)} seats</>}
      />
      <ToggleKpi
        label="Per accepted finding"
        value={
          <Text span fw={700} size="xl" ff="monospace">
            ≈{fmtUsd(cost7 / accepted7)}
          </Text>
        }
        accent="success"
        sub={`7d: ${accepted7} accepted · API only ${fmtUsd(api7 / accepted7)}`}
      />
      <ProviderSplit models={f?.models} onModels={f?.onModels} />
    </Box>
  )
}

/** "Showing N of 14" plus one removable badge per active filter. */
function ActiveFilters({ f, shown, search, repo }: { f: StripFilters; shown: number; search: string; repo: 'all' | Repo }) {
  const groups: { label: string; color: string; remove: () => void }[] = []
  let rest = [...f.stages] as Stage[]
  if (hasAll(f, IN_REVIEW)) {
    groups.push({ label: 'In review', color: 'active', remove: () => toggleStages(f, IN_REVIEW) })
    rest = rest.filter((s) => !IN_REVIEW.includes(s))
  }
  if (hasAll(f, AT_GATE)) {
    groups.push({ label: 'Waiting on humans', color: 'pink', remove: () => toggleStages(f, AT_GATE) })
    rest = rest.filter((s) => !AT_GATE.includes(s))
  }
  for (const s of rest) groups.push({ label: STAGE_META[s].label, color: STAGE_META[s].color, remove: () => f.onStages(f.stages.filter((x) => x !== s)) })
  for (const m of f.models) groups.push({ label: `uses ${MODELS[m].short}`, color: MODELS[m].color, remove: () => f.onModels(f.models.filter((x) => x !== m)) })
  if (f.deepRounds) groups.push({ label: 'round 3+', color: 'gray', remove: () => f.onDeepRounds(false) })
  const any = groups.length > 0
  return (
    <Group gap={6} mih={22}>
      <Text size="xs" c={any || search || repo !== 'all' ? undefined : 'dimmed'} fw={any ? 600 : 400}>
        Showing {shown} of {RUNS.length} runs
      </Text>
      {any && (
        <Text size="xs" c="dimmed">
          · filtered by
        </Text>
      )}
      {groups.map((g) => (
        <Badge
          key={g.label}
          size="sm"
          color={g.color}
          variant="light"
          rightSection={
            <UnstyledButton onClick={g.remove} aria-label={`Remove ${g.label} filter`} style={{ display: 'flex' }}>
              <TbX size={11} />
            </UnstyledButton>
          }
        >
          {g.label}
        </Badge>
      ))}
      {any && (
        <UnstyledButton
          onClick={() => {
            f.onStages([])
            f.onModels([])
            f.onDeepRounds(false)
          }}
        >
          <Group gap={4}>
            <TbFilterOff size={13} color="var(--mantine-color-gray-5)" />
            <Text size="xs" c="gray.5" td="underline">
              Clear all
            </Text>
          </Group>
        </UnstyledButton>
      )}
    </Group>
  )
}

function StageFunnel() {
  const total = RUNS.length
  return (
    <Box>
      <Progress.Root size={20}>
        {STAGES.map((s) => {
          const n = RUNS.filter((r) => r.stage === s).length
          return n === 0 ? null : (
            <Tooltip key={s} label={`${STAGE_META[s].label}: ${n}`}>
              <Progress.Section value={(n / total) * 100} color={STAGE_META[s].color}>
                <Progress.Label>
                  {STAGE_META[s].label} {n}
                </Progress.Label>
              </Progress.Section>
            </Tooltip>
          )
        })}
      </Progress.Root>
    </Box>
  )
}

function KpiOverview() {
  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Text fw={700} size="lg">
          Review engine · today
        </Text>
        <Text size="xs" c="dimmed">
          Sat 21 Nov · working-hour clocks paused (weekend)
        </Text>
      </Group>
      <KpiStrip />
      <StageFunnel />
      <SimpleGrid cols={2} spacing="sm">
        {MOVE_GROUPS.map((g) => {
          const runs = RUNS.filter((r) => g.stages.includes(r.stage))
          return (
            <Paper key={g.key} withBorder radius="md" p="xs">
              <SectionLabel
                right={
                  <Badge size="sm" color={g.color} variant="light">
                    {runs.length}
                  </Badge>
                }
              >
                {g.title} <Text span size="xs" c="dimmed" tt="none" fw={400}>· {g.hint}</Text>
              </SectionLabel>
              <Stack gap={0}>
                {runs.map((r) => (
                  <Group key={r.id} gap={8} wrap="nowrap" py={5} style={{ borderTop: '1px solid var(--mantine-color-dark-5)' }}>
                    <StatusIcon state={r.statusReview.state} />
                    <Box style={{ flex: 1, minWidth: 0 }}>
                      <Group gap={6} wrap="nowrap">
                        <Text size="xs" ff="monospace" c="dimmed">
                          {runRef(r)}
                        </Text>
                        <Text size="sm" truncate>
                          {r.title}
                        </Text>
                      </Group>
                      <Text size="xs" c="dimmed" truncate>
                        {g.key === 'humans'
                          ? `${r.stageDetail} · ${r.assignee ? personName(r.assignee) : 'unassigned'} · ${(r.humanRequired ?? []).slice(0, 2).join(', ')}`
                          : g.key === 'authors'
                            ? `${personName(typeof r.resolvedAuthor === 'object' ? r.resolvedAuthor.login : r.author)} · ${r.stageDetail}`
                            : r.statusReview.text}
                      </Text>
                    </Box>
                    {g.key === 'antiphon' && r.fanOut ? <FanOut run={r} /> : <RoundPips round={r.round} botOnly={r.botOnlyRounds} compact />}
                    <Box w={110} ta="right">
                      <CostText cost={r.cost} />
                    </Box>
                  </Group>
                ))}
              </Stack>
            </Paper>
          )
        })}
      </SimpleGrid>
    </Stack>
  )
}

// ---------------------------------------------------------------- switchable page (table default)

type RunsView = 'table' | 'board' | 'overview'

/** One page, one data set; the view switch only changes the layout. Table is the default. */
function ReviewRunsPage({ initial = 'table' }: { initial?: RunsView }) {
  const [view, setView] = useState<RunsView>(initial)
  return (
    <Stack gap="xs">
      <Group justify="flex-end">
        <SegmentedControl
          size="xs"
          value={view}
          onChange={(v) => setView(v as RunsView)}
          data={[
            { value: 'table', label: 'Table' },
            { value: 'board', label: 'Board' },
            { value: 'overview', label: 'Overview' },
          ]}
        />
      </Group>
      {view === 'table' && <DenseTable />}
      {view === 'board' && <StageBoard />}
      {view === 'overview' && <KpiOverview />}
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Review Runs',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** The page as proposed: dense table by default, with Board and Overview one click away (same data). */
export const Page: Story = { name: 'Page · Table default, switchable', render: () => <ReviewRunsPage /> }

/** (A) Dense, filterable table. Grouped by stage by default; flat or by repo via the toggle. */
export const DenseTableView: Story = { name: 'A · Dense table', render: () => <DenseTable /> }

/** (B) One column per stage, optional swimlanes per repo. Cards show reviewer fan-out pips and gate SLA. */
export const StageBoardView: Story = { name: 'B · Stage board', render: () => <StageBoard /> }

/** (C) KPI strip (incl. Jev vs subscription split) + runs grouped by whose move it is. */
export const KpiOverviewView: Story = { name: 'C · KPI strip + whose move', render: () => <KpiOverview /> }
