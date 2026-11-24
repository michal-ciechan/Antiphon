import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import {
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Divider,
  Group,
  NumberInput,
  Paper,
  Progress,
  SegmentedControl,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Tabs,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import {
  TbAdjustments,
  TbAlertTriangle,
  TbArrowsDiff,
  TbCheck,
  TbCircleCheck,
  TbColumns2,
  TbFilter,
  TbFlask,
  TbPlayerPause,
  TbPlayerPlay,
  TbPlayerStop,
  TbPlus,
  TbRefresh,
  TbScale,
  TbThumbDown,
  TbThumbUp,
  TbX,
} from 'react-icons/tb'
import {
  BASE_STEPS,
  CASES,
  COMPARISON_EXPECTATIONS,
  COMPARISON_FINDINGS,
  EXPERIMENTS,
  ORIGIN_META,
  PROMPT_DIFF,
  RESULTS,
  STEP_DIFF,
  SUITE,
  VARIANTS,
  fmtSeconds,
  fmtUsd,
  summarise,
  variantSteps,
  type CellLookup,
  type ExpectationResult,
  type ExperimentRow,
  type FindingRow,
  type StepDiff,
} from './mockData'
import { ByoaButton, Fact, GraphLegend, GraphPreview, PageHeader, ResultBadge, ResultCell, SectionLabel } from './shared'

/**
 * PROPOSAL MOCK — not production code. The Experiments area of
 * `docs/features/013-experiments/plan.md`: A/B testing of workflow graphs.
 *   A — history of every experiment
 *   B — a new experiment: the suite, each variant's graph with the steps that will run, the budget
 *   C — customising a variant (a graph setup) in the editor
 *   D — the A/B view: cases against variants
 *   E — one cell compared with the original: a different model on the same graph
 *   F — one cell compared with the original: a different graph (an added step, a prompt change)
 *   G — an experiment paused at its budget
 */

const STATUS_META: Record<ExperimentRow['status'], { label: string; color: string }> = {
  done: { label: 'Done', color: 'success' },
  running: { label: 'Running', color: 'active' },
  paused: { label: 'Paused at budget', color: 'warning' },
  stopped: { label: 'Stopped', color: 'gray' },
}

function StepName({ id }: { id: string }) {
  return (
    <Text span ff="monospace" size="xs" c="gray.4">
      {id}
    </Text>
  )
}

// ───────────────────────────────────────── A · History ─────────────────────────────────────────

function History() {
  return (
    <Box>
      <PageHeader title="Experiments" subtitle="Replay recorded runs through different graph setups and compare the results. Each row is one run of a suite; nothing here posts to a PR.">
        <ByoaButton />
        <Button variant="default" leftSection={<TbFlask size={16} />}>
          Cases
        </Button>
        <Button variant="default">Suites</Button>
        <Button variant="default">Variants</Button>
        <Button leftSection={<TbPlus size={16} />}>New experiment</Button>
      </PageHeader>
      <Group gap="xs" mb="sm">
        <TextInput placeholder="Search title, note, suite or variant" w={320} leftSection={<TbFilter size={14} />} />
        <Select placeholder="Start step" data={['input', ...BASE_STEPS.map((s) => s.id)]} w={170} clearable />
        <Select placeholder="Suite" data={['demo-review']} w={160} clearable />
        <Select placeholder="Status" data={['Done', 'Running', 'Paused at budget', 'Stopped']} w={180} clearable />
      </Group>
      <Paper withBorder radius="md">
        <Table verticalSpacing="sm" highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Experiment</Table.Th>
              <Table.Th>Suite · starts at</Table.Th>
              <Table.Th>Variants</Table.Th>
              <Table.Th>Result</Table.Th>
              <Table.Th>Spend</Table.Th>
              <Table.Th>Run by</Table.Th>
              <Table.Th>Status</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {EXPERIMENTS.map((e) => (
              <Table.Tr key={e.id} style={{ cursor: 'pointer' }}>
                <Table.Td maw={340}>
                  <Text size="sm" fw={600}>
                    {e.title}
                  </Text>
                  <Text size="xs" c="dimmed" lineClamp={2}>
                    {e.note}
                  </Text>
                  <Text size="xs" c="dimmed" ff="monospace">
                    {e.id}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{e.suite}</Text>
                  <StepName id={e.start} />
                </Table.Td>
                <Table.Td>
                  <Stack gap={3}>
                    {e.variants.map((v, i) => (
                      <Badge key={v} size="sm" variant={i === 0 ? 'outline' : 'light'} color={i === 0 ? 'gray' : 'active'} radius="sm" tt="none">
                        {v}
                      </Badge>
                    ))}
                  </Stack>
                </Table.Td>
                <Table.Td maw={260}>
                  <Text size="sm">{e.result}</Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{fmtUsd(e.costUsd)}</Text>
                  <Text size="xs" c="dimmed">
                    {e.budgetUsd ? `of ${fmtUsd(e.budgetUsd)} budget` : 'no budget'}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{e.by}</Text>
                  {e.viaByoa && (
                    <Badge size="xs" variant="light" color="grape" radius="sm" tt="none">
                      via BYOA
                    </Badge>
                  )}
                  <Text size="xs" c="dimmed">
                    {e.when}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Badge color={STATUS_META[e.status].color} variant="light" radius="sm" tt="none">
                    {STATUS_META[e.status].label}
                  </Badge>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
    </Box>
  )
}

// ────────────────────────────────────── B · New experiment ──────────────────────────────────────

function NewExperiment() {
  const [tab, setTab] = useState<string | null>('var-opus')
  const [sideBySide, setSideBySide] = useState(false)
  const [budgetMode, setBudgetMode] = useState('amount')
  const shown = VARIANTS.filter((v) => (sideBySide ? true : v.id === tab))
  const runs = BASE_STEPS.length - BASE_STEPS.findIndex((s) => s.id === SUITE.start)
  const trials = CASES.length * (VARIANTS.length - 1)
  return (
    <Box>
      <PageHeader title="New experiment" subtitle="Pick a suite and the graph setups to compare. Each case runs once per variant, from the suite's start step to the end of the graph, in its own container.">
        <ByoaButton />
        <Button variant="default">Cancel</Button>
        <Button leftSection={<TbPlayerPlay size={16} />}>Start</Button>
      </PageHeader>
      <Box style={{ display: 'grid', gridTemplateColumns: '360px 1fr', gap: 16, alignItems: 'start' }}>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="md">
            <SectionLabel>What runs</SectionLabel>
            <Stack gap="xs">
              <Select label="Suite" data={['demo-review']} defaultValue="demo-review" description={`${CASES.length} cases · ${CASES.filter((c) => c.heldOut).length} held out`} />
              <Fact labelWidth={104} label="Starts at">
                <StepName id={SUITE.start} /> (set by the suite)
              </Fact>
              <Fact labelWidth={104} label="Runs">
                {runs} steps, to the end of the graph
              </Fact>
              <Fact labelWidth={104} label="Gate answers">
                <StepName id="human-approve" /> → approve
              </Fact>
              <Fact labelWidth={104} label="Judge">
                {SUITE.judge.who}, after <StepName id={SUITE.judge.gather ?? ''} />
              </Fact>
            </Stack>
          </Paper>
          <Paper withBorder radius="md" p="md">
            <SectionLabel>Instructions for this run</SectionLabel>
            <Stack gap="xs">
              <Textarea
                label="Run note"
                description="What this run is for. Shown in history; never sent to a model."
                autosize
                minRows={2}
                defaultValue="Does Opus catch the empty-title miss, and does a second verify pass cut the style noise?"
              />
              <Select label="Stop rule" data={['Run to the end of the graph', 'Stop after a named step', 'Stop at the first failed expectation']} defaultValue="Run to the end of the graph" />
              <Switch label="Extra samples per cell" description="Off: each case runs once per variant." />
            </Stack>
          </Paper>
          <Paper withBorder radius="md" p="md">
            <SectionLabel>Spend</SectionLabel>
            <Stack gap="xs">
              <SegmentedControl fullWidth value={budgetMode} onChange={setBudgetMode} data={[{ value: 'amount', label: 'Budget' }, { value: 'none', label: 'No budget' }]} />
              {budgetMode === 'amount' ? (
                <NumberInput label="Budget" prefix="$" defaultValue={25} description="The run pauses and asks once when the next trial would pass this." />
              ) : (
                <Text size="xs" c="dimmed">
                  The run never pauses for spend. Host limits still apply.
                </Text>
              )}
              <Divider />
              <Fact labelWidth={104} label="Trials">
                {trials} new · {CASES.length} reused from the record
              </Fact>
              <Fact labelWidth={104} label="Estimated cost">about $13, from the cases' original usage</Fact>
              <Text size="xs" c="dimmed">
                Starting asks for no confirmation.
              </Text>
            </Stack>
          </Paper>
        </Stack>
        <Paper withBorder radius="md" p="md">
          <Group justify="space-between" mb="sm">
            <SectionLabel>Variants: the graph setups to compare</SectionLabel>
            <Group gap="xs">
              <Switch size="sm" label="Side by side" checked={sideBySide} onChange={(e) => setSideBySide(e.currentTarget.checked)} />
              <Button size="xs" variant="default" leftSection={<TbPlus size={14} />}>
                Add variant
              </Button>
            </Group>
          </Group>
          {!sideBySide && (
            <Tabs value={tab} onChange={setTab} mb="sm">
              <Tabs.List>
                {VARIANTS.map((v) => (
                  <Tabs.Tab key={v.id} value={v.id} rightSection={v.baseline ? <Badge size="xs" variant="outline" color="gray">baseline</Badge> : v.overrides.length ? <Badge size="xs" color="warning" variant="light">{v.overrides.length}</Badge> : null}>
                    {v.name}
                  </Tabs.Tab>
                ))}
              </Tabs.List>
            </Tabs>
          )}
          <Stack gap="md">
            {shown.map((v) => (
              <Box key={v.id}>
                <Group justify="space-between" mb={6}>
                  <Box>
                    <Text fw={600} size="sm">
                      {v.name}{' '}
                      <Text span size="xs" c="dimmed" ff="monospace">
                        {v.hash}
                      </Text>
                    </Text>
                    <Text size="xs" c="dimmed">
                      {v.note}
                    </Text>
                  </Box>
                  <Button size="xs" variant="light" leftSection={<TbAdjustments size={14} />}>
                    Customise
                  </Button>
                </Group>
                <GraphPreview variant={v} start={SUITE.start} compact={sideBySide} />
                {v.overrides.length > 0 && (
                  <Stack gap={2} mt={8}>
                    {v.overrides.map((o) => (
                      <Text key={o.stepId} size="xs">
                        <StepName id={o.stepId} /> <Text span size="xs" c="dimmed">{o.summary}</Text>
                      </Text>
                    ))}
                  </Stack>
                )}
              </Box>
            ))}
          </Stack>
          <Divider my="sm" />
          <Group justify="space-between">
            <GraphLegend />
            <Badge color="success" variant="light" radius="sm" tt="none" leftSection={<TbCircleCheck size={12} />}>
              All 3 graphs valid
            </Badge>
          </Group>
        </Paper>
      </Box>
    </Box>
  )
}

// ───────────────────────────────────── C · Customise a variant ─────────────────────────────────────

function Customise() {
  const variant = VARIANTS[2]
  const [sel, setSel] = useState('verify-2')
  const step = variantSteps(variant).find((s) => s.def.id === sel) ?? variantSteps(variant)[0]
  return (
    <Box>
      <PageHeader
        title={
          <>
            Customise variant: {variant.name}
          </>
        }
        subtitle="Change a step's settings or the graph itself. Saving makes a new variant; the live workflow is never touched."
      >
        <ByoaButton />
        <Button variant="default">Validate</Button>
        <Button variant="default">Discard</Button>
        <Button>Save as new variant</Button>
      </PageHeader>
      <Alert color="active" variant="light" mb="sm" icon={<TbArrowsDiff size={16} />} title="2 differences from the baseline">
        {variant.overrides.map((o) => (
          <Text key={o.stepId} size="sm">
            <StepName id={o.stepId} /> {o.summary}
          </Text>
        ))}
      </Alert>
      <Box style={{ display: 'grid', gridTemplateColumns: '1fr 400px', gap: 16, alignItems: 'start' }}>
        <Paper withBorder radius="md" p="md">
          <Group justify="space-between" mb="sm">
            <SectionLabel>Graph (click a step)</SectionLabel>
            <Group gap="xs">
              <Button size="xs" variant="default" leftSection={<TbPlus size={14} />}>
                Add step
              </Button>
              <Button size="xs" variant="default">
                Describe a change…
              </Button>
            </Group>
          </Group>
          <GraphPreview variant={variant} start={SUITE.start} selected={sel} onSelect={setSel} />
          <Divider my="sm" />
          <Group justify="space-between">
            <GraphLegend />
            <Badge color="success" variant="light" radius="sm" tt="none" leftSection={<TbCircleCheck size={12} />}>
              Valid: no cycle without a gate, every input has a producer
            </Badge>
          </Group>
        </Paper>
        <Paper withBorder radius="md" p="md">
          <Group justify="space-between" mb="xs">
            <Box>
              <Text fw={700}>{step.def.label}</Text>
              <Text size="xs" c="dimmed" ff="monospace">
                {step.def.id} · {step.def.kind}
              </Text>
            </Box>
            {step.override && (
              <Badge color={step.override.change === 'added' ? 'success' : 'warning'} variant="light" radius="sm" tt="none">
                {step.override.change}
              </Badge>
            )}
          </Group>
          <Stack gap="xs">
            <Select label="Model" data={['Haiku 5.5', 'Sonnet 5.5', 'Opus 5.5', 'GPT-5.5', 'Grok Low']} defaultValue="Sonnet 5.5" description="Baseline: this step does not exist" />
            <Select label="Runs after" data={BASE_STEPS.map((s) => s.id)} defaultValue="verify" />
            <NumberInput label="Keep a finding at or above" defaultValue={0.6} step={0.05} min={0} max={1} decimalScale={2} description="Baseline verify: 0.50" />
            <Textarea
              label="Prompt: verify.instructions"
              description="A patch on the current text. The full rendered prompt is in the Prompt tab."
              autosize
              minRows={4}
              styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', fontSize: 12 } }}
              defaultValue={'+ Drop a finding when the diff already makes the change it asks for.\n+ Drop a finding that only states a style preference the repo does not follow.\n+ When you are not sure, keep the finding and lower its confidence.'}
            />
            <Textarea label="Instruction for this step" description="Plain words added to this step's prompt for runs of this variant." autosize minRows={2} placeholder="e.g. Pay particular attention to findings about naming." />
            <Group justify="space-between" mt={4}>
              <Button size="xs" variant="subtle" color="danger">
                Remove step
              </Button>
              <Button size="xs" variant="default">
                Reset to baseline
              </Button>
            </Group>
          </Stack>
        </Paper>
      </Box>
    </Box>
  )
}

// ───────────────────────────────────────── D · A/B view ─────────────────────────────────────────

function BudgetBar({ spent, budget, paused }: { spent: number; budget?: number; paused?: boolean }) {
  if (!budget)
    return (
      <Text size="sm">
        {fmtUsd(spent)} spent · no budget
      </Text>
    )
  return (
    <Box w={260}>
      <Group justify="space-between">
        <Text size="xs" c="dimmed">
          Spend
        </Text>
        <Text size="xs">
          {fmtUsd(spent)} of {fmtUsd(budget)}
        </Text>
      </Group>
      <Progress value={(spent / budget) * 100} color={paused ? 'warning' : 'active'} size="sm" />
    </Box>
  )
}

function SummaryCards({ cellFor }: { cellFor: CellLookup }) {
  return (
    <SimpleGrid cols={3} spacing="sm" mb="sm">
      {VARIANTS.map((v) => {
        const s = summarise(v.id, cellFor)
        return (
          <Paper key={v.id} withBorder radius="md" p="sm">
            <Group justify="space-between" mb={4}>
              <Text fw={700} size="sm">
                {v.name}
              </Text>
              {v.baseline ? (
                <Badge size="xs" variant="outline" color="gray">
                  baseline
                </Badge>
              ) : (
                <Text size="xs" c="dimmed" ff="monospace">
                  {v.hash}
                </Text>
              )}
            </Group>
            <Group gap="lg" align="flex-end">
              <Box>
                <Text fz={26} fw={700} lh={1}>
                  {s.passed}
                  <Text span size="sm" c="dimmed" fw={400}>
                    {' '}
                    of {CASES.length} pass
                  </Text>
                </Text>
              </Box>
              {s.flaky > 0 && (
                <Text size="sm" c="warning.4">
                  {s.flaky} flaky
                </Text>
              )}
              {s.queued > 0 && (
                <Text size="sm" c="dimmed">
                  {s.queued} not started
                </Text>
              )}
            </Group>
            {v.baseline ? (
              <Text size="xs" c="dimmed" mt={6}>
                Original runs, served from the record
              </Text>
            ) : (
              <Group gap="md" mt={6}>
                <Text size="xs" c={s.fixed ? 'success.4' : 'dimmed'}>
                  {s.fixed} fixed
                </Text>
                <Text size="xs" c={s.regressed ? 'danger.4' : 'dimmed'}>
                  {s.regressed} regressed
                </Text>
                <Text size="xs" c="dimmed">
                  {fmtUsd(s.costUsd)} · {fmtSeconds(s.seconds)}
                </Text>
              </Group>
            )}
          </Paper>
        )
      })}
    </SimpleGrid>
  )
}

function AbView({ paused = false }: { paused?: boolean }) {
  const exp = EXPERIMENTS[0]
  const [selected, setSelected] = useState<string | null>('case-03|var-opus')
  const [diffOnly, setDiffOnly] = useState(false)
  const cellFor: CellLookup = (caseId, variantId) =>
    paused && variantId === 'var-two-verify' && ['case-04', 'case-05', 'case-06'].includes(caseId) ? { result: 'queued', note: 'Not started: waiting on the budget' } : RESULTS[caseId][variantId]
  const budget = paused ? 12 : 25
  const spent = VARIANTS.reduce((sum, v) => sum + summarise(v.id, cellFor).costUsd, 0)
  const rows = CASES.filter((c) => !diffOnly || new Set(VARIANTS.map((v) => (cellFor(c.id, v.id).result === 'reused' ? 'pass' : cellFor(c.id, v.id).result))).size > 1)
  return (
    <Box>
      <PageHeader
        title={exp.title}
        subtitle={
          <>
            {exp.note} · suite <b>{exp.suite}</b>, from <StepName id={exp.start} /> to the end of the graph · {exp.by}, {exp.when}
          </>
        }
      >
        <BudgetBar spent={spent} budget={budget} paused={paused} />
        <ByoaButton />
        <Button variant="default" leftSection={<TbPlus size={14} />}>
          Add variant
        </Button>
        <Button variant="default" leftSection={<TbRefresh size={14} />}>
          Run again
        </Button>
      </PageHeader>
      {paused && (
        <Alert color="warning" variant="light" mb="sm" icon={<TbPlayerPause size={18} />} title="Paused: the budget is reached">
          <Text size="sm">
            {fmtUsd(spent)} of {fmtUsd(budget)} is spent, and the next trial (about 66¢) would pass it. 3 trials have not started. Trials already running were left to finish. Nothing new starts until
            you choose.
          </Text>
          <Group gap="xs" mt="xs">
            <NumberInput size="xs" w={120} prefix="$" defaultValue={25} aria-label="New budget" />
            <Button size="xs">Raise the budget</Button>
            <Button size="xs" variant="default">
              Remove the budget
            </Button>
            <Button size="xs" variant="default" color="danger" leftSection={<TbPlayerStop size={14} />}>
              Stop here
            </Button>
          </Group>
        </Alert>
      )}
      <SummaryCards cellFor={cellFor} />
      <Paper withBorder radius="md" p="sm">
        <Group justify="space-between" mb="xs">
          <SectionLabel>Cases against variants (click a cell to compare it with the original)</SectionLabel>
          <Switch size="sm" label="Differences only" checked={diffOnly} onChange={(e) => setDiffOnly(e.currentTarget.checked)} />
        </Group>
        <Table verticalSpacing={6} withRowBorders>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Case</Table.Th>
              {VARIANTS.map((v) => (
                <Table.Th key={v.id}>
                  {v.name}
                  {v.baseline && (
                    <Text span size="xs" c="dimmed" fw={400}>
                      {' '}
                      (baseline)
                    </Text>
                  )}
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((c) => (
              <Table.Tr key={c.id}>
                <Table.Td maw={330}>
                  <Group gap={6} wrap="nowrap">
                    <Text size="sm" fw={600} ff="monospace">
                      {c.name}
                    </Text>
                    {c.heldOut && (
                      <Badge size="xs" color="indigo" variant="light" radius="sm" tt="none">
                        held out
                      </Badge>
                    )}
                  </Group>
                  <Text size="xs" c="dimmed" lineClamp={1}>
                    {c.title}
                  </Text>
                  <Group gap={6} mt={2}>
                    <Badge size="xs" variant="dot" color={ORIGIN_META[c.origin].color} radius="sm" tt="none">
                      {ORIGIN_META[c.origin].label}
                    </Badge>
                    <Text size="xs" c="dimmed">
                      {c.pr}
                    </Text>
                  </Group>
                </Table.Td>
                {VARIANTS.map((v) => {
                  const key = `${c.id}|${v.id}`
                  const cell = cellFor(c.id, v.id)
                  return (
                    <Table.Td key={v.id}>
                      <ResultCell cell={cell} selected={selected === key} onClick={() => setSelected(key)} />
                    </Table.Td>
                  )
                })}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
        <Text size="xs" c="dimmed" mt="xs">
          Each cell ran once unless it shows a count. A cell that both passed and failed is marked flaky, not averaged. Held-out cases are never shown to an agent that drafts a change.
        </Text>
      </Paper>
    </Box>
  )
}

// ──────────────────────────────────────── E · Comparison ────────────────────────────────────────

const GROUP_META: Record<FindingRow['group'], { title: string; hint: string; color: string }> = {
  matched: { title: 'In both', hint: 'Same location and meaning as the original', color: 'gray' },
  missing: { title: 'Missing', hint: 'In the original, not in this trial', color: 'danger' },
  new: { title: 'New', hint: 'Not in the original. Mark each one so the case learns what to expect', color: 'active' },
}

interface Scenario {
  caseIndex: number
  variantName: string
  verdict: 'pass' | 'fail'
  verdictText: string
  trial: string
  original: string
  judgeBadge: string
  judgeText: string
  gathered: string
  steps: StepDiff[]
  stepsNote: string
  findings: FindingRow[]
  expectations: ExpectationResult[]
  settings: [step: string, setting: string, original: string, trial: string][]
  settingsNote: string
  promptDiff?: typeof PROMPT_DIFF
}

const OPUS_ON_CLEAN: Scenario = {
  caseIndex: 2,
  variantName: 'Opus reviewers',
  verdict: 'fail',
  verdictText: 'The trial kept both original findings and raised 2 more. The case expects the output to be unchanged, so it fails until the new findings are judged.',
  trial: '$1.44 · 2m 09s · 1 sample',
  original: '$0.58 · 1m 51s',
  judgeBadge: 'Prefers this trial',
  judgeText:
    "B finds a real defect A does not: POST /cards accepts an empty title, and the diff adds that endpoint. B's second new finding is a fair observation but minor. Neither output asks for a change the diff already makes.",
  gathered: 'build ok · 41 tests pass · lines touched by findings: 4 of 4 are in the diff',
  steps: STEP_DIFF,
  stepsNote: 'First difference at review: the reviewers ran on Opus 5.5 at the deep level. route took the same edge in both.',
  findings: COMPARISON_FINDINGS,
  expectations: COMPARISON_EXPECTATIONS,
  settings: [
    ['review', 'Model', 'Sonnet 5.5', 'Opus 5.5'],
    ['review', 'Level', 'standard', 'deep'],
  ],
  settingsNote: 'Same graph, prompts, checks and workspace (d93b82d at /work/antiphon-demo, verified).',
}

const TWO_VERIFY_ON_STYLE_NIT: Scenario = {
  caseIndex: 5,
  variantName: 'Two verify passes',
  verdict: 'pass',
  verdictText: 'The second verify pass dropped the style finding the author had dismissed, and kept the other three. That is what the case expects.',
  trial: '$0.74 · 2m 01s · 1 sample',
  original: '$0.61 · 1m 55s',
  judgeBadge: 'Prefers this trial',
  judgeText: 'A asks to turn the Card record into a class. The case says the repo uses records for value types by design. B leaves that out and keeps both real defects, at the same severities.',
  gathered: 'build ok · 41 tests pass · findings on unchanged lines: 0',
  steps: [
    { stepId: 'intake', state: 'recorded' },
    { stepId: 'delta', state: 'recorded' },
    { stepId: 'categorise', state: 'same' },
    { stepId: 'select', state: 'same' },
    { stepId: 'group', state: 'same' },
    { stepId: 'packs', state: 'same' },
    { stepId: 'review', state: 'same' },
    { stepId: 'collate', state: 'same' },
    { stepId: 'verify', state: 'same' },
    { stepId: 'verify-2', state: 'added' },
    { stepId: 'publish', state: 'differs' },
    { stepId: 'route', state: 'same' },
  ],
  stepsNote: 'The first verify kept the same 4 findings in both. verify-2 exists only in this variant and dropped one. route took the same edge in both.',
  findings: [
    { group: 'matched', severity: 'major', file: 'src/Demo.Core/Board.cs', line: 52, title: 'StaleCards compares the dates the wrong way round', check: 'SEED-0001' },
    { group: 'matched', severity: 'major', file: 'src/Demo.Web/CardEndpoints.cs', line: 44, title: '/done sets the status without Board.Move', check: 'DEMO-0001' },
    { group: 'matched', severity: 'minor', file: 'src/Demo.Web/CardEndpoints.cs', line: 47, title: 'Completed timestamp is set from local time', check: 'SEED-0006' },
    { group: 'missing', severity: 'nit', file: 'src/Demo.Core/Card.cs', line: 6, title: 'Card should be a class, not a record', check: 'SEED-0005', note: 'Expected to be absent' },
  ],
  expectations: [{ text: 'No finding asking to turn the Card record into a class', kind: 'finding-absent', at: 'verify', a: 'fail', b: 'pass', reason: 'The original posted it; the author dismissed it as a style preference. The trial did not post it.' }],
  settings: [
    ['verify', 'Prompt verify.instructions', 'built-in default', 'patched, +3 −1 lines'],
    ['verify-2', 'Step', 'not in the graph', 'added after verify'],
    ['verify-2', 'Model', '—', 'Sonnet 5.5'],
    ['verify-2', 'Keep a finding at or above', '—', '0.60'],
  ],
  settingsNote: 'Same reviewers, checks and workspace (b511da6 at /work/antiphon-demo, verified).',
  promptDiff: PROMPT_DIFF,
}

function Comparison({ sc }: { sc: Scenario }) {
  const c = CASES[sc.caseIndex]
  const sevColor = { nit: 'gray', minor: 'yellow', major: 'orange' } as const
  return (
    <Box>
      <PageHeader
        title={
          <>
            <Text span ff="monospace" fz={20}>
              {c.name}
            </Text>{' '}
            · {sc.variantName} against the original
          </>
        }
        subtitle={c.why}
      >
        <Select w={210} size="xs" data={['Original (as recorded)', ...VARIANTS.filter((v) => !v.baseline && v.name !== sc.variantName).map((v) => v.name)]} defaultValue="Original (as recorded)" aria-label="Compare with" leftSection={<TbColumns2 size={14} />} />
        <ByoaButton />
        <Button variant="default" size="xs" leftSection={<TbRefresh size={14} />}>
          Run again
        </Button>
        <Button variant="default" size="xs">
          Keep workspace
        </Button>
      </PageHeader>
      <Group gap="sm" mb="sm" align="stretch">
        <Paper withBorder radius="md" p="sm" style={{ flex: 1 }}>
          <Group justify="space-between">
            <SectionLabel>Verdict</SectionLabel>
            <ResultBadge result={sc.verdict} />
          </Group>
          <Text size="sm" mb={8}>
            {sc.verdictText}
          </Text>
          <Fact label="Trial" labelWidth={70}>
            {sc.trial}
          </Fact>
          <Fact label="Original" labelWidth={70}>
            {sc.original}
          </Fact>
        </Paper>
        <Paper withBorder radius="md" p="sm" style={{ flex: 1 }}>
          <Group justify="space-between">
            <SectionLabel>Judge: {SUITE.judge.who} (outputs shown as A and B, unlabelled)</SectionLabel>
            <Badge variant="light" color="active" radius="sm" tt="none" leftSection={<TbScale size={12} />}>
              {sc.judgeBadge}
            </Badge>
          </Group>
          <Text size="sm">"{sc.judgeText}"</Text>
          <Text size="xs" c="dimmed" mt={6}>
            Gathered by <StepName id="scenarios/gather.py" />: {sc.gathered}
          </Text>
        </Paper>
      </Group>
      <Paper withBorder radius="md" p="sm" mb="sm">
        <SectionLabel>Where the two runs differ, step by step</SectionLabel>
        <Group gap={4}>
          {sc.steps.map((s) => (
            <Badge
              key={s.stepId}
              size="md"
              radius="sm"
              tt="none"
              variant={s.state === 'differs' || s.state === 'added' ? 'light' : 'outline'}
              color={s.state === 'differs' ? 'warning' : s.state === 'added' ? 'success' : 'gray'}
              style={{ opacity: s.state === 'recorded' ? 0.5 : 1, fontFamily: 'var(--mantine-font-family-monospace)' }}
              leftSection={s.state === 'differs' ? <TbArrowsDiff size={11} /> : s.state === 'added' ? <TbPlus size={11} /> : s.state === 'same' ? <TbCheck size={11} /> : undefined}
            >
              {s.stepId}
            </Badge>
          ))}
        </Group>
        <Group gap="md" mt={6}>
          <Text size="xs" c="dimmed">
            {sc.stepsNote}
          </Text>
        </Group>
        <Text size="xs" c="dimmed" mt={2}>
          ✓ same output · ⇄ differs · + only in this variant · faded: not run, fed from the record
        </Text>
      </Paper>
      <Box style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, alignItems: 'start' }}>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="sm">
            <SectionLabel>Findings</SectionLabel>
            {(['matched', 'missing', 'new'] as const).map((g) => {
              const rows = sc.findings.filter((f) => f.group === g)
              return (
                <Box key={g} mb="sm">
                  <Group gap={6} mb={4}>
                    <Badge size="sm" color={GROUP_META[g].color} variant="light" radius="sm" tt="none">
                      {GROUP_META[g].title} · {rows.length}
                    </Badge>
                    <Text size="xs" c="dimmed">
                      {GROUP_META[g].hint}
                    </Text>
                  </Group>
                  {rows.length === 0 && (
                    <Text size="xs" c="dimmed" pl={4}>
                      None.
                    </Text>
                  )}
                  {rows.map((f) => (
                    <Group key={f.title} justify="space-between" wrap="nowrap" py={4} style={{ borderTop: '1px solid var(--mantine-color-dark-5)' }}>
                      <Box style={{ minWidth: 0 }}>
                        <Group gap={6} wrap="nowrap">
                          <Badge size="xs" color={sevColor[f.severity]} variant="filled" radius="sm" style={{ flexShrink: 0 }}>
                            {f.severity}
                          </Badge>
                          <Text size="sm" truncate>
                            {f.title}
                          </Text>
                        </Group>
                        <Text size="xs" c="dimmed" ff="monospace">
                          {f.file}:{f.line} · {f.check}
                        </Text>
                      </Box>
                      {f.note && (
                        <Badge size="sm" color="success" variant="light" radius="sm" tt="none" leftSection={<TbCheck size={11} />} style={{ flexShrink: 0 }}>
                          {f.note}
                        </Badge>
                      )}
                      {g === 'new' && (
                        <Group gap={4} wrap="nowrap">
                          <Button size="compact-xs" variant="light" color="success" leftSection={<TbThumbUp size={12} />}>
                            Good
                          </Button>
                          <Button size="compact-xs" variant="light" color="gray" leftSection={<TbThumbDown size={12} />}>
                            Noise
                          </Button>
                        </Group>
                      )}
                    </Group>
                  ))}
                </Box>
              )
            })}
          </Paper>
          <Paper withBorder radius="md" p="sm">
            <SectionLabel>Expectations</SectionLabel>
            <Table verticalSpacing={4}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Expectation</Table.Th>
                  <Table.Th w={90}>Original</Table.Th>
                  <Table.Th w={90}>Trial</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {sc.expectations.map((e) => (
                  <Table.Tr key={e.text}>
                    <Table.Td>
                      <Text size="sm">{e.text}</Text>
                      <Text size="xs" c="dimmed">
                        <Code fz={10}>{e.kind}</Code> at <StepName id={e.at} /> · {e.reason}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <ResultBadge result={e.a} />
                    </Table.Td>
                    <Table.Td>
                      <ResultBadge result={e.b} />
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Paper>
        </Stack>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="sm">
            <SectionLabel>Settings that differ</SectionLabel>
            <Table verticalSpacing={4}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Step</Table.Th>
                  <Table.Th>Setting</Table.Th>
                  <Table.Th>Original</Table.Th>
                  <Table.Th>Trial</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {sc.settings.map(([step, setting, a, b]) => (
                  <Table.Tr key={step + setting}>
                    <Table.Td>
                      <StepName id={step} />
                    </Table.Td>
                    <Table.Td>{setting}</Table.Td>
                    <Table.Td>
                      <Text size="sm" c={a === '—' ? 'dimmed' : undefined}>
                        {a}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="warning.4">
                        {b}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
            <Text size="xs" c="dimmed" mt={4}>
              {sc.settingsNote}
            </Text>
          </Paper>
          <Paper withBorder radius="md" p="sm">
            <SectionLabel>{sc.promptDiff ? 'Prompt difference: verify.instructions' : 'Prompts'}</SectionLabel>
            {sc.promptDiff ? (
              <Box ff="monospace" fz={12} style={{ borderRadius: 6, overflow: 'hidden', border: '1px solid var(--mantine-color-dark-5)' }}>
                {sc.promptDiff.map((l, i) => (
                  <Box key={i} px={8} py={2} bg={l.op === '+' ? 'rgba(35,133,81,0.22)' : l.op === '-' ? 'rgba(205,66,70,0.22)' : undefined} c={l.op === ' ' ? 'dimmed' : undefined}>
                    {l.op} {l.text}
                  </Box>
                ))}
              </Box>
            ) : (
              <Text size="sm">
                Identical in both runs: all 6 prompts have the same hash. Only the reviewers' model and level changed.
              </Text>
            )}
            <Group gap="xs" mt="xs">
              <Button size="xs" variant="default">
                Raw answers
              </Button>
              <Button size="xs" variant="default">
                Model calls
              </Button>
              <Button size="xs" variant="default">
                Captured PR review
              </Button>
            </Group>
          </Paper>
          <Alert color="gray" variant="light" icon={<TbAlertTriangle size={16} />} title="Judged once">
            <Text size="sm">One sample per side. If this result decides between two setups, run this cell again before trusting it.</Text>
            <Group gap="xs" mt={6}>
              <Button size="xs" variant="default" leftSection={<TbRefresh size={14} />}>
                Add 2 samples
              </Button>
              <Button size="xs" variant="default" leftSection={<TbX size={14} />}>
                Overrule the judge
              </Button>
            </Group>
          </Alert>
        </Stack>
      </Box>
    </Box>
  )
}

const meta: Meta = {
  title: 'Proposals/Experiments/Experiments',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Every experiment: what it asked, its suite and start step, the variants, the result line, spend and who ran it. */
export const HistoryList: Story = { name: 'A · History', render: () => <History /> }

/** (B) A new experiment: the suite sets the start step; each variant is shown as a graph with the steps that will run; a budget or none. */
export const New: Story = { name: 'B · New experiment', render: () => <NewExperiment /> }

/** (C) Customise a variant: pick a step, change its settings or prompt, or change the graph; saving makes a new variant. */
export const CustomiseVariant: Story = { name: 'C · Customise a variant', render: () => <Customise /> }

/** (D) The A/B view: a summary per variant, then cases down and variants across. */
export const AbGrid: Story = { name: 'D · A/B view', render: () => <AbView /> }

/** (E) One cell against the original, same graph and a different model: verdict, the judge, where the runs differ, and new findings waiting to be marked good or noise. */
export const CellComparison: Story = { name: 'E · Comparison: a different model', render: () => <Comparison sc={OPUS_ON_CLEAN} /> }

/** (F) A cell where the graphs differ: an added step, a prompt difference, and a finding that is missing because the case expects it to be. */
export const CellComparisonGraph: Story = { name: 'F · Comparison: a different graph', render: () => <Comparison sc={TWO_VERIFY_ON_STYLE_NIT} /> }

/** (G) The same experiment paused at its budget: raise it, remove it, or stop. */
export const PausedAtBudget: Story = { name: 'G · Paused at the budget', render: () => <AbView paused /> }
