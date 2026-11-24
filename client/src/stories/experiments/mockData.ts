/**
 * PROPOSAL MOCK — not production code. Data for the Experiments (A/B testing) Storybook prototypes,
 * `docs/features/013-experiments/plan.md`. Nothing outside `src/stories/experiments/` and
 * `src/stories/byoa/` imports this file.
 */

export type StepKind = 'code' | 'decision' | 'completion' | 'agent' | 'fan-out' | 'join' | 'gate' | 'wait'
export type SectionKey = 'intake' | 'review' | 'decide'

export interface StepDef {
  id: string
  label: string
  kind: StepKind
  section: SectionKey
  /** What the step uses by default: a model, an agent level, or nothing for plain code. */
  uses?: string
}

export const SECTION_TITLE: Record<SectionKey, string> = { intake: 'Intake', review: 'Review', decide: 'Decide' }

export const BASE_STEPS: StepDef[] = [
  { id: 'intake', label: 'Intake', kind: 'code', section: 'intake' },
  { id: 'delta', label: 'Delta', kind: 'code', section: 'intake' },
  { id: 'categorise', label: 'Categorise', kind: 'decision', section: 'intake', uses: 'Haiku 5.5' },
  { id: 'select', label: 'Select checks', kind: 'decision', section: 'intake', uses: 'Haiku 5.5' },
  { id: 'group', label: 'Group', kind: 'code', section: 'intake' },
  { id: 'packs', label: 'Build packs', kind: 'code', section: 'intake' },
  { id: 'review', label: 'Reviewers ×3', kind: 'fan-out', section: 'review', uses: 'Sonnet 5.5 · standard' },
  { id: 'collate', label: 'Collate', kind: 'join', section: 'review' },
  { id: 'verify', label: 'Verify', kind: 'completion', section: 'review', uses: 'Haiku 5.5' },
  { id: 'publish', label: 'Publish', kind: 'code', section: 'review' },
  { id: 'route', label: 'Route', kind: 'code', section: 'decide' },
  { id: 'needs-human-approval', label: 'Needs human?', kind: 'code', section: 'decide' },
  { id: 'bot-approve', label: 'Bot approve', kind: 'code', section: 'decide' },
]

export type StepChange = 'changed' | 'added' | 'removed'

export interface StepOverride {
  stepId: string
  change: StepChange
  /** One line for the badge tooltip and the difference list. */
  summary: string
  uses?: string
  /** For an added step: where it goes and what it is. */
  after?: string
  def?: StepDef
}

export interface Variant {
  id: string
  name: string
  baseline?: boolean
  note: string
  hash: string
  overrides: StepOverride[]
}

export const VARIANTS: Variant[] = [
  { id: 'var-recorded', name: 'As recorded', baseline: true, note: 'The graph and settings each original run used.', hash: 'a41c09e', overrides: [] },
  {
    id: 'var-opus',
    name: 'Opus reviewers',
    note: 'Same graph. Reviewers run on Opus at the deep level.',
    hash: '7be2d14',
    overrides: [{ stepId: 'review', change: 'changed', summary: 'Model Sonnet 5.5 → Opus 5.5, level standard → deep', uses: 'Opus 5.5 · deep' }],
  },
  {
    id: 'var-two-verify',
    name: 'Two verify passes',
    note: 'A second verify step re-checks what the first one kept, with a stricter prompt.',
    hash: 'c0f93aa',
    overrides: [
      { stepId: 'verify', change: 'changed', summary: 'Prompt verify.instructions patched (+3 −1 lines)', uses: 'Haiku 5.5' },
      {
        stepId: 'verify-2',
        change: 'added',
        summary: 'New step after verify: second pass on Sonnet 5.5',
        after: 'verify',
        def: { id: 'verify-2', label: 'Verify (2nd)', kind: 'completion', section: 'review', uses: 'Sonnet 5.5' },
      },
    ],
  },
]

/** The steps of a variant's graph, with the override that applies to each. */
export function variantSteps(v: Variant): { def: StepDef; override?: StepOverride }[] {
  const out: { def: StepDef; override?: StepOverride }[] = []
  for (const def of BASE_STEPS) {
    const o = v.overrides.find((x) => x.stepId === def.id)
    out.push({ def: o?.uses ? { ...def, uses: o.uses } : def, override: o })
    for (const added of v.overrides.filter((x) => x.change === 'added' && x.after === def.id && x.def)) out.push({ def: added.def!, override: added })
  }
  return out
}

export type ExpectationKind = 'unchanged' | 'finding-present' | 'finding-absent' | 'field' | 'budget' | 'script' | 'prompt'

export interface Expectation {
  id: string
  kind: ExpectationKind
  at: string
  text: string
}

export type CaseOrigin = 'graph' | 'missed' | 'false-positive' | 'file'

export interface EvalCase {
  id: string
  name: string
  title: string
  why: string
  origin: CaseOrigin
  pr: string
  heldOut?: boolean
  tags: string[]
  expectations: Expectation[]
}

export const ORIGIN_META: Record<CaseOrigin, { label: string; color: string }> = {
  graph: { label: 'Saved from a run', color: 'gray' },
  missed: { label: 'Bot missed this', color: 'grape' },
  'false-positive': { label: 'Should not have been raised', color: 'orange' },
  file: { label: 'Scenario file', color: 'cyan' },
}

export const CASES: EvalCase[] = [
  {
    id: 'case-01',
    name: 'done-bypasses-move',
    title: '/done sets the status without Board.Move',
    why: 'PR #2 added a /done endpoint that sets the status directly, so timestamps and the allowed-transition rule are skipped. A reviewer must catch this.',
    origin: 'file',
    pr: 'antiphon-demo#2',
    tags: ['correctness', 'reviewer'],
    expectations: [
      { id: 'e1', kind: 'finding-present', at: 'verify', text: 'A finding in src/Demo.Web/CardEndpoints.cs about /done changing status without Board.Move, at least major' },
      { id: 'e2', kind: 'field', at: 'categorise', text: 'categories contains correctness' },
      { id: 'e3', kind: 'budget', at: 'end', text: 'cost ≤ $2.00' },
    ],
  },
  {
    id: 'case-02',
    name: 'stale-cards-swapped-operands',
    title: 'StaleCards compares the dates the wrong way round',
    why: 'Board.StaleCards subtracts now from the card date, so no card is ever stale. Found by the bot on the original run; kept so a cheaper model does not lose it.',
    origin: 'graph',
    pr: 'antiphon-demo#2',
    tags: ['correctness', 'reviewer', 'regression'],
    expectations: [
      { id: 'e1', kind: 'finding-present', at: 'verify', text: 'A finding in src/Demo.Core/Board.cs about swapped operands in StaleCards' },
      { id: 'e2', kind: 'prompt', at: 'end', text: 'Pass only if no finding asks for a change that the diff already makes' },
    ],
  },
  {
    id: 'case-03',
    name: 'card-priority-clean',
    title: 'A clean feature should stay quiet',
    why: 'PR #1 adds card priority and has no defects. The review should raise nothing above minor.',
    origin: 'graph',
    pr: 'antiphon-demo#1',
    tags: ['clean', 'noise'],
    expectations: [
      { id: 'e1', kind: 'unchanged', at: 'verify', text: 'Findings equivalent to the original (1 minor, 1 nit)' },
      { id: 'e2', kind: 'field', at: 'route', text: 'route == findings' },
    ],
  },
  {
    id: 'case-04',
    name: 'docs-only-approves',
    title: 'A docs-only change is approved by the bot',
    why: 'PR #3 changes only docs. It must reach bot-approve with no findings and no human gate.',
    origin: 'graph',
    pr: 'antiphon-demo#3',
    tags: ['clean', 'routing'],
    expectations: [
      { id: 'e1', kind: 'field', at: 'route', text: 'route == clean' },
      { id: 'e2', kind: 'field', at: 'needs-human-approval', text: 'answer == no' },
      { id: 'e3', kind: 'script', at: 'end', text: 'scenarios/checks/no-code-findings.py exits 0' },
    ],
  },
  {
    id: 'case-05',
    name: 'missed-empty-title',
    title: 'An empty card title is accepted',
    why: 'On PR #1 a human reviewer commented that POST /cards accepts an empty title. No bot finding matched the comment.',
    origin: 'missed',
    pr: 'antiphon-demo#1',
    heldOut: true,
    tags: ['missed', 'validation'],
    expectations: [{ id: 'e1', kind: 'finding-present', at: 'verify', text: 'A finding in src/Demo.Web/CardEndpoints.cs about a missing check for an empty title' }],
  },
  {
    id: 'case-06',
    name: 'no-style-nit-on-records',
    title: 'Do not ask for a class where a record is used',
    why: 'The author dismissed this finding as a style preference: the repo uses records for value types by design.',
    origin: 'false-positive',
    pr: 'antiphon-demo#2',
    heldOut: true,
    tags: ['false-positive', 'style'],
    expectations: [{ id: 'e1', kind: 'finding-absent', at: 'verify', text: 'No finding asking to turn the Card record into a class' }],
  },
]

export type CellResult = 'pass' | 'fail' | 'flaky' | 'running' | 'queued' | 'reused'

export interface Cell {
  result: CellResult
  /** Passes out of samples, when the cell was run more than once. */
  samples?: [number, number]
  costUsd?: number
  seconds?: number
  /** The first step whose output differs from the baseline's. */
  divergesAt?: string
  note?: string
}

export const RESULT_META: Record<CellResult, { label: string; color: string }> = {
  pass: { label: 'Pass', color: 'success' },
  fail: { label: 'Fail', color: 'danger' },
  flaky: { label: 'Flaky', color: 'warning' },
  running: { label: 'Running', color: 'active' },
  queued: { label: 'Queued', color: 'gray' },
  reused: { label: 'Pass (reused)', color: 'success' },
}

/** results[caseId][variantId] */
export const RESULTS: Record<string, Record<string, Cell>> = {
  'case-01': {
    'var-recorded': { result: 'reused', costUsd: 0, seconds: 0, note: 'Original run, not re-run' },
    'var-opus': { result: 'pass', costUsd: 1.62, seconds: 141, divergesAt: 'review' },
    'var-two-verify': { result: 'pass', costUsd: 0.71, seconds: 118, divergesAt: 'verify' },
  },
  'case-02': {
    'var-recorded': { result: 'reused', costUsd: 0, seconds: 0, note: 'Original run, not re-run' },
    'var-opus': { result: 'pass', costUsd: 1.58, seconds: 137, divergesAt: 'review' },
    'var-two-verify': { result: 'flaky', samples: [2, 3], costUsd: 2.04, seconds: 352, divergesAt: 'verify-2', note: 'The second pass dropped the finding in 1 of 3 samples' },
  },
  'case-03': {
    'var-recorded': { result: 'reused', costUsd: 0, seconds: 0, note: 'Original run, not re-run' },
    'var-opus': { result: 'fail', costUsd: 1.44, seconds: 129, divergesAt: 'review', note: '2 new findings, not yet judged' },
    'var-two-verify': { result: 'pass', costUsd: 0.63, seconds: 104, divergesAt: 'verify' },
  },
  'case-04': {
    'var-recorded': { result: 'reused', costUsd: 0, seconds: 0, note: 'Original run, not re-run' },
    'var-opus': { result: 'pass', costUsd: 0.38, seconds: 41 },
    'var-two-verify': { result: 'pass', costUsd: 0.09, seconds: 22 },
  },
  'case-05': {
    'var-recorded': { result: 'fail', costUsd: 0, seconds: 0, note: 'The original run missed it' },
    'var-opus': { result: 'pass', costUsd: 1.51, seconds: 133, divergesAt: 'review' },
    'var-two-verify': { result: 'fail', costUsd: 0.66, seconds: 109, divergesAt: 'verify' },
  },
  'case-06': {
    'var-recorded': { result: 'fail', costUsd: 0, seconds: 0, note: 'The original run raised it' },
    'var-opus': { result: 'fail', costUsd: 1.6, seconds: 139, divergesAt: 'review' },
    'var-two-verify': { result: 'pass', costUsd: 0.74, seconds: 121, divergesAt: 'verify-2' },
  },
}

export interface Suite {
  id: string
  name: string
  description: string
  start: string
  judge: { kind: 'model' | 'agent'; who: string; gather?: string; prompt: string }
  gates: Record<string, string>
  budgetUsd?: number
}

export const SUITE: Suite = {
  id: 'suite-demo-review',
  name: 'demo-review',
  description: 'The three demo PRs plus what people caught and dismissed on them.',
  start: 'categorise',
  judge: {
    kind: 'model',
    who: 'Opus 5.5',
    gather: 'scenarios/gather.py',
    prompt:
      'Compare the two outputs for the same change. Prefer the one that finds the real defects described in the case, with fewer findings a maintainer would ignore. Wording does not matter.',
  },
  gates: { 'human-approve': 'approve' },
  budgetUsd: 25,
}

export interface ExperimentRow {
  id: string
  title: string
  note: string
  suite: string
  start: string
  variants: string[]
  status: 'done' | 'running' | 'paused' | 'stopped'
  result: string
  costUsd: number
  budgetUsd?: number
  by: string
  viaByoa?: boolean
  when: string
}

export const EXPERIMENTS: ExperimentRow[] = [
  {
    id: 'exp-0042',
    title: 'Opus reviewers and a second verify pass',
    note: 'Does Opus catch the empty-title miss, and does a second verify pass cut the style noise?',
    suite: 'demo-review',
    start: 'categorise',
    variants: ['As recorded', 'Opus reviewers', 'Two verify passes'],
    status: 'done',
    result: 'Opus 4/6 · Two verify 4/6, 1 flaky · baseline 4/6',
    costUsd: 13.0,
    budgetUsd: 25,
    by: 'michal-ciechan',
    when: '24 Nov, 16:10',
  },
  {
    id: 'exp-0041',
    title: 'Haiku for check selection',
    note: 'Can the selector move to the cheaper chain without dropping SEED-0001?',
    suite: 'demo-review',
    start: 'select',
    variants: ['As recorded', 'Haiku selector'],
    status: 'done',
    result: 'Haiku selector 6/6 · no regressions · cost −38%',
    costUsd: 3.2,
    budgetUsd: 25,
    by: 'michal-ciechan',
    viaByoa: true,
    when: '24 Nov, 11:42',
  },
  {
    id: 'exp-0040',
    title: 'Smaller context packs',
    note: 'Half the token budget for related files.',
    suite: 'demo-review',
    start: 'packs',
    variants: ['As recorded', 'Packs 8k'],
    status: 'paused',
    result: '7 of 12 trials done · budget reached',
    costUsd: 10.0,
    budgetUsd: 10,
    by: 'hmartin',
    when: '23 Nov, 17:05',
  },
  {
    id: 'exp-0039',
    title: 'Learn candidate: "check input validation on new endpoints"',
    note: 'Gate for the proposal from the empty-title miss. Held-out cases included.',
    suite: 'demo-review',
    start: 'select',
    variants: ['Current', 'Candidate check VAL-0001'],
    status: 'done',
    result: 'Candidate 6/6 · target fixed · no regressions · general',
    costUsd: 4.1,
    by: 'antiphon[bot]',
    when: '23 Nov, 09:18',
  },
  {
    id: 'exp-0038',
    title: 'Whole workflow on Codex reviewers',
    note: 'Start at the input; compare agent kinds.',
    suite: 'demo-review',
    start: 'input',
    variants: ['As recorded', 'Codex reviewers'],
    status: 'stopped',
    result: 'Stopped by asmith after 4 trials',
    costUsd: 5.6,
    budgetUsd: 40,
    by: 'asmith',
    when: '22 Nov, 14:30',
  },
]

export function fmtUsd(usd: number): string {
  if (usd === 0) return '$0'
  if (usd < 1) return `${Math.round(usd * 100)}¢`
  return `$${usd.toFixed(2)}`
}

export function fmtSeconds(s: number): string {
  if (s === 0) return '—'
  if (s < 60) return `${s}s`
  return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, '0')}s`
}

export interface VariantSummary {
  passed: number
  failed: number
  flaky: number
  queued: number
  regressed: number
  fixed: number
  costUsd: number
  seconds: number
}

export type CellLookup = (caseId: string, variantId: string) => Cell

export function summarise(variantId: string, cellFor: CellLookup = (c, v) => RESULTS[c][v], baselineId = 'var-recorded'): VariantSummary {
  const s: VariantSummary = { passed: 0, failed: 0, flaky: 0, queued: 0, regressed: 0, fixed: 0, costUsd: 0, seconds: 0 }
  for (const c of CASES) {
    const cell = cellFor(c.id, variantId)
    const base = cellFor(c.id, baselineId)
    const ok = cell.result === 'pass' || cell.result === 'reused'
    const baseOk = base.result === 'pass' || base.result === 'reused'
    if (cell.result === 'queued' || cell.result === 'running') {
      s.queued++
      continue
    }
    if (ok) s.passed++
    else if (cell.result === 'flaky') s.flaky++
    else s.failed++
    if (variantId !== baselineId) {
      if (baseOk && cell.result === 'fail') s.regressed++
      if (!baseOk && ok) s.fixed++
    }
    s.costUsd += cell.costUsd ?? 0
    s.seconds += cell.seconds ?? 0
  }
  return s
}

// ───────────────────────── one comparison: case-03 under "Opus reviewers" ─────────────────────────

export interface FindingRow {
  group: 'matched' | 'missing' | 'new'
  severity: 'nit' | 'minor' | 'major'
  file: string
  line: number
  title: string
  check: string
  judgement?: 'good' | 'noise'
  note?: string
}

export const COMPARISON_FINDINGS: FindingRow[] = [
  { group: 'matched', severity: 'minor', file: 'src/Demo.Core/Card.cs', line: 18, title: 'Priority has no upper bound', check: 'SEED-0006' },
  { group: 'matched', severity: 'nit', file: 'client/src/cards.ts', line: 44, title: 'Sort comparator can return NaN for a missing priority', check: 'SEED-0004' },
  { group: 'new', severity: 'major', file: 'src/Demo.Web/CardEndpoints.cs', line: 31, title: 'POST /cards accepts an empty title', check: 'SEED-0003' },
  { group: 'new', severity: 'minor', file: 'src/Demo.Core/BoardSummary.cs', line: 12, title: 'Summary counts ignore the new priority field', check: 'SEED-0001' },
]

export interface ExpectationResult {
  text: string
  kind: ExpectationKind
  at: string
  a: 'pass' | 'fail'
  b: 'pass' | 'fail'
  reason: string
}

export const COMPARISON_EXPECTATIONS: ExpectationResult[] = [
  { text: 'Findings equivalent to the original (1 minor, 1 nit)', kind: 'unchanged', at: 'verify', a: 'pass', b: 'fail', reason: 'Both original findings are present, and 2 findings are new. New findings are not judged yet.' },
  { text: 'route == findings', kind: 'field', at: 'route', a: 'pass', b: 'pass', reason: 'Both took the findings edge to await-fixes.' },
]

export type StepDiff = { stepId: string; state: 'same' | 'differs' | 'recorded' | 'added' }

export const STEP_DIFF: StepDiff[] = [
  { stepId: 'intake', state: 'recorded' },
  { stepId: 'delta', state: 'recorded' },
  { stepId: 'categorise', state: 'same' },
  { stepId: 'select', state: 'same' },
  { stepId: 'group', state: 'same' },
  { stepId: 'packs', state: 'same' },
  { stepId: 'review', state: 'differs' },
  { stepId: 'collate', state: 'differs' },
  { stepId: 'verify', state: 'differs' },
  { stepId: 'publish', state: 'differs' },
  { stepId: 'route', state: 'same' },
]

export const PROMPT_DIFF: { op: ' ' | '+' | '-'; text: string }[] = [
  { op: ' ', text: 'You are verifying review findings against the diff.' },
  { op: ' ', text: 'Keep a finding only when the diff shows the problem it describes.' },
  { op: '-', text: 'Drop a finding when you are not sure.' },
  { op: '+', text: 'Drop a finding when the diff already makes the change it asks for.' },
  { op: '+', text: 'Drop a finding that only states a style preference the repo does not follow.' },
  { op: '+', text: 'When you are not sure, keep the finding and lower its confidence.' },
  { op: ' ', text: 'Answer with one line per finding: keep or drop, and a probability.' },
]

export const WORKSPACE_RECORD: { item: string; value: string }[] = [
  { item: 'Repository', value: 'github.com/michal-ciechan/antiphon-demo' },
  { item: 'Parent', value: 'e737fb3 on master' },
  { item: 'Worktree', value: 'd93b82d, detached, at /work/antiphon-demo' },
  { item: 'Base and merge-base', value: 'e737fb3 · e737fb3' },
  { item: 'Uncommitted state', value: 'none' },
  { item: 'How it was cut', value: 'fresh worktree from refs/pull/1/head, full history' },
  { item: 'Container image', value: 'antiphon-runner@sha256:9f1c…b27a' },
  { item: 'Agent CLI', value: 'claude 2.4.1' },
]

export const TRIAL_HISTORY: { experiment: string; variant: string; result: CellResult; when: string; cost: number }[] = [
  { experiment: 'exp-0042', variant: 'Opus reviewers', result: 'pass', when: '24 Nov, 16:10', cost: 1.51 },
  { experiment: 'exp-0042', variant: 'Two verify passes', result: 'fail', when: '24 Nov, 16:10', cost: 0.66 },
  { experiment: 'exp-0039', variant: 'Candidate check VAL-0001', result: 'pass', when: '23 Nov, 09:18', cost: 0.52 },
  { experiment: 'exp-0039', variant: 'Current', result: 'fail', when: '23 Nov, 09:18', cost: 0.49 },
]

export const HUMAN_COMMENTS: { id: string; who: string; file: string; line: number; text: string; matched?: string; caseName?: string }[] = [
  { id: 'hc-1', who: 'hmartin', file: 'src/Demo.Web/CardEndpoints.cs', line: 31, text: 'This accepts an empty title. Should be a 400.', caseName: 'missed-empty-title' },
  { id: 'hc-2', who: 'hmartin', file: 'src/Demo.Core/Card.cs', line: 18, text: 'Priority needs an upper bound, or the sort breaks.', matched: 'Priority has no upper bound' },
  { id: 'hc-3', who: 'asmith', file: 'docs/cards.md', line: 9, text: 'Mention that priority 0 is the default.' },
]
