import type {
  BlastRadiusDto,
  DismissScope,
  FindingAction,
  FindingEventDto,
  FindingStatus,
  GateDecision,
  GateKind,
  NodeExecutionDto,
  NodeStatus,
  QueueItemStatus,
  ReasonCategory,
  ReviewFindingDto,
  ReviewGateDto,
  ReviewRunDetailDto,
  ReviewRunStatus,
  ReviewStage,
  ReviewSubjectDto,
  Severity,
  SyncState,
  Transport,
  WaitingOn,
} from '../../../api/reviewEngine'
import { NODE_KIND_BY_ID } from './workflowSeed'

/**
 * Seed data for the in-memory review API. Fictional org ("lumen"), people, PRs and incidents,
 * adapted from the review-engine proposal mocks. Times are minutes before `now`, so the data looks
 * live whenever the fake is created. Derived fields (open counts, models, cost) are left at zero
 * here and recomputed by the fake from the findings and node executions.
 */

// ---------------------------------------------------------------- ids

export function seedGuid(kind: number, n: number): string {
  return `5eed${kind.toString(16).padStart(4, '0')}-0000-4000-8000-${n.toString(16).padStart(12, '0')}`
}

export const ID_KIND = { run: 1, subject: 2, finding: 3, event: 4, gate: 5, node: 6 } as const

// ---------------------------------------------------------------- people, teams, models

export interface FakePerson {
  login: string
  name: string
  teams: string[]
  service?: boolean
}

export const BOT_LOGIN = 'antiphon[bot]'

export const FAKE_PEOPLE: FakePerson[] = [
  { login: 'asmith', name: 'Alice Smith', teams: ['payments-core'] },
  { login: 'hmartin', name: 'Hugo Martin', teams: ['payments-core', 'platform-sre'] },
  { login: 'cwei', name: 'Chen Wei', teams: ['payments-core'] },
  { login: 'ksato', name: 'Kenji Sato', teams: ['payments-core'] },
  { login: 'lnovak', name: 'Lena Novak', teams: ['payments-core'] },
  { login: 'bokafor', name: 'Ben Okafor', teams: ['platform-runtime'] },
  { login: 'elindqvist', name: 'Erik Lindqvist', teams: ['platform-runtime', 'platform-sre'] },
  { login: 'grao', name: 'Gita Rao', teams: ['platform-runtime'] },
  { login: 'jweber', name: 'Jonas Weber', teams: ['platform-runtime'] },
  { login: 'druiz', name: 'Dana Ruiz', teams: ['web-experience'] },
  { login: 'fhaddad', name: 'Farah Haddad', teams: ['web-experience'] },
  { login: 'icosta', name: 'Ines Costa', teams: ['web-experience'] },
  { login: 'renovate[bot]', name: 'renovate[bot]', teams: [], service: true },
  { login: 'antiphon-delegate', name: 'antiphon-delegate', teams: [], service: true },
  { login: 'release-train', name: 'release-train', teams: [], service: true },
  { login: BOT_LOGIN, name: 'Antiphon', teams: [], service: true },
]

/** Team SLA in working minutes (the fake measures plain minutes). */
export const FAKE_TEAM_SLA_MINUTES: Record<string, number> = {
  'payments-core': 240,
  'platform-runtime': 480,
  'web-experience': 480,
  'platform-sre': 480,
}

export function personName(login: string): string | null {
  return FAKE_PEOPLE.find((p) => p.login === login)?.name ?? null
}

export type ModelKey = 'jev' | 'luna' | 'haiku' | 'sonnet' | 'opus' | 'grok'

export const FAKE_MODELS: Record<ModelKey, { model: string; provider: string; transport: Transport; inPerM: number; outPerM: number }> = {
  jev: { model: 'jev-1.14.0', provider: 'litellm', transport: 'Http', inPerM: 0.04, outPerM: 0.04 },
  luna: { model: 'gpt-5.6-luna', provider: 'codex', transport: 'Seat', inPerM: 1.25, outPerM: 10 },
  haiku: { model: 'haiku', provider: 'claude', transport: 'Seat', inPerM: 1, outPerM: 5 },
  sonnet: { model: 'sonnet', provider: 'claude', transport: 'Seat', inPerM: 3, outPerM: 15 },
  opus: { model: 'opus', provider: 'claude', transport: 'Seat', inPerM: 15, outPerM: 75 },
  grok: { model: 'grok-4.7-low', provider: 'grok', transport: 'Seat', inPerM: 0.2, outPerM: 0.5 },
}

// ---------------------------------------------------------------- small helpers

/** Deterministic 0..1 from a string, so the seed is stable across reloads. */
function hash01(s: string): number {
  let h = 2166136261
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return ((h >>> 0) % 10_000) / 10_000
}

function between(seed: string, min: number, max: number): number {
  return Math.round(min + (max - min) * hash01(seed))
}

/** p 0..1 → the contract's 1..5 scale. */
const p5 = (p: number) => Math.max(1, Math.min(5, Math.round(1 + p * 4)))

function blast(reach: number, contractP: number, dataP: number, rollback: number, reversal: number, exposure: number): BlastRadiusDto {
  return { reach, externalContract: p5(contractP), data: p5(dataP), rollbackAtRelease: rollback, reversalAfterMonths: reversal, exposure }
}

// ---------------------------------------------------------------- node execution synthesis

export interface ReviewerPlan {
  group: string
  model: ModelKey
  status?: NodeStatus
}

/** What one iteration did. `current` iterations end Running/Waiting; past ones end Succeeded. */
export type IterationOutcome =
  | { kind: 'queued' }
  | { kind: 'running'; until: 'intake' | 'categorise' | 'select' | 'review' }
  | { kind: 'findings' }
  | { kind: 'round-limit' }
  | { kind: 'human-approve'; decidedBy?: string; decision?: GateDecision }
  | { kind: 'bot-approve'; merged?: boolean }
  | { kind: 'human-approved'; by: string; merged?: boolean }
  | { kind: 'failed'; at: string; summary: string }
  | { kind: 'closed' }

export interface IterationSpec {
  iteration: number
  startMinAgo: number
  headSha: string
  reviewers: ReviewerPlan[]
  verifyModel: ModelKey
  outcome: IterationOutcome
  current: boolean
  /** per-node summary overrides */
  summaries?: Record<string, string>
}

const PRE_REVIEW = ['intake', 'delta', 'categorise', 'select', 'group', 'packs'] as const
const POST_REVIEW = ['collate', 'verify', 'publish', 'route'] as const

/** Minutes each step takes, roughly as the proposal timeline shows. */
const DURATION_MIN: Record<string, number> = {
  intake: 0.3,
  delta: 0.1,
  categorise: 0.2,
  select: 0.3,
  group: 0.05,
  packs: 0.4,
  collate: 0.05,
  verify: 0.6,
  publish: 0.1,
  route: 0.02,
  'needs-human-approval': 0.02,
  'bot-approve': 0.02,
  'base-moved': 0.05,
}

export interface ExecFactory {
  nextId: () => string
  now: number
}

function iso(now: number, minAgo: number): string {
  return new Date(now - minAgo * 60_000).toISOString()
}

function costOf(model: ModelKey, inputTokens: number, outputTokens: number): number {
  const m = FAKE_MODELS[model]
  return Math.round(((inputTokens * m.inPerM + outputTokens * m.outPerM) / 1_000_000) * 10_000) / 10_000
}

export function makeExec(
  f: ExecFactory,
  nodeId: string,
  iteration: number,
  status: NodeStatus,
  startMinAgo: number | null,
  durationMin: number | null,
  extra: { model?: ModelKey; inputTokens?: number; outputTokens?: number; summary?: string | null } = {},
): NodeExecutionDto {
  const m = extra.model ? FAKE_MODELS[extra.model] : null
  const inputTokens = extra.inputTokens ?? 0
  const outputTokens = extra.outputTokens ?? 0
  const done = status === 'Succeeded' || status === 'Failed' || status === 'Skipped'
  return {
    id: f.nextId(),
    nodeId,
    iteration,
    kind: NODE_KIND_BY_ID[nodeId] ?? 'Code',
    status,
    provider: m?.provider ?? null,
    model: m?.model ?? null,
    transport: m?.transport ?? null,
    inputTokens,
    outputTokens,
    costUsd: extra.model ? costOf(extra.model, inputTokens, outputTokens) : 0,
    startedAt: startMinAgo === null ? null : iso(f.now, startMinAgo),
    completedAt: done && startMinAgo !== null && durationMin !== null ? iso(f.now, Math.max(0, startMinAgo - durationMin)) : null,
    summary: extra.summary ?? null,
  }
}

/** The model a step uses, its token footprint, and a plausible summary. */
function stepDetail(nodeId: string, spec: IterationSpec, seed: string): { model?: ModelKey; inputTokens?: number; outputTokens?: number; summary: string } {
  const s = spec.summaries?.[nodeId]
  const first = spec.iteration === 1
  switch (nodeId) {
    case 'intake':
      return { summary: s ?? `head ${spec.headSha} · mirror fetched · config read at base` }
    case 'delta':
      return { summary: s ?? (first ? 'full (first iteration)' : `${between(seed + 'd', 1, 5)} files changed since the last reviewed head`) }
    case 'categorise':
      return { model: 'jev', inputTokens: between(seed + 'c', 16_000, 21_000), outputTokens: 300, summary: s ?? 'kind, risk areas and blast radius answered' }
    case 'select': {
      const n = between(seed + 's', 6, 14)
      return { model: 'jev', inputTokens: between(seed + 'si', 22_000, 42_000), outputTokens: 1_200, summary: s ?? `${between(seed + 'sc', 58, 171)} candidates → ${n} checks` }
    }
    case 'group':
      return { summary: s ?? `${spec.reviewers.length} groups: ${spec.reviewers.map((r) => r.group).join(', ')}` }
    case 'packs':
      return { summary: s ?? `shared prefix ${between(seed + 'p', 38, 55)}.${between(seed + 'pp', 0, 9)}k tokens` }
    case 'collate':
      return { summary: s ?? `${between(seed + 'co', 3, 9)} → ${between(seed + 'cp', 2, 5)} after dedupe` }
    case 'verify':
      return { model: spec.verifyModel, inputTokens: between(seed + 'v', 40_000, 72_000), outputTokens: between(seed + 'vo', 2_000, 4_100), summary: s ?? `${between(seed + 'vd', 0, 1)} disproved and dropped` }
    case 'publish':
      return { summary: s ?? 'review posted · summary comment edited · statuses set' }
    case 'route':
      return { summary: s ?? '' }
    default:
      return { summary: s ?? '' }
  }
}

/** Builds every execution of one iteration along the default workflow's main path. */
export function synthIteration(f: ExecFactory, spec: IterationSpec, seed: string): NodeExecutionDto[] {
  const out: NodeExecutionDto[] = []
  let t = spec.startMinAgo
  const it = spec.iteration
  const o = spec.outcome
  const live = spec.current

  const step = (nodeId: string, status: NodeStatus = 'Succeeded', summary?: string) => {
    const d = stepDetail(nodeId, spec, `${seed}:${it}:${nodeId}`)
    const dur = DURATION_MIN[nodeId] ?? 0.05
    out.push(makeExec(f, nodeId, it, status, t, status === 'Running' || status === 'Waiting' ? null : dur, { ...d, summary: summary ?? d.summary }))
    t -= dur
  }

  if (o.kind === 'queued') {
    out.push(makeExec(f, 'intake', it, 'Pending', null, null, { summary: 'queued, 2 ahead' }))
    return out
  }

  const runningUntil = o.kind === 'running' ? o.until : null
  for (const nodeId of PRE_REVIEW) {
    if (runningUntil && nodeId === runningUntil) {
      step(nodeId, 'Running', stepDetail(nodeId, spec, seed).summary || 'running')
      return out
    }
    if (o.kind === 'failed' && o.at === nodeId) {
      step(nodeId, 'Failed', o.summary)
      return out
    }
    step(nodeId)
  }

  // fan-out: one reviewer execution per group, all starting together
  const reviewStart = t
  let longest = 0
  let doneCount = 0
  for (const r of spec.reviewers) {
    const status: NodeStatus = r.status ?? 'Succeeded'
    const dur = between(`${seed}:${it}:${r.group}:dur`, 3, 9)
    longest = Math.max(longest, dur)
    if (status === 'Succeeded') doneCount++
    const findingsN = between(`${seed}:${it}:${r.group}:n`, 0, 3)
    out.push(
      makeExec(f, 'reviewer', it, status, reviewStart, status === 'Running' ? null : dur, {
        model: r.model,
        inputTokens: between(`${seed}:${it}:${r.group}:in`, 54_000, 62_000),
        outputTokens: status === 'Running' ? between(`${seed}:${it}:${r.group}:o`, 900, 2_400) : between(`${seed}:${it}:${r.group}:out`, 2_000, 3_200),
        summary:
          status === 'Running'
            ? `${r.group}: running · opened 2 files beyond the pack`
            : status === 'Failed'
              ? `${r.group}: seat lost (runner restarted) after 2 repair attempts`
              : `${r.group}: ${findingsN} finding${findingsN === 1 ? '' : 's'} · every assigned check accounted for`,
      }),
    )
  }
  const reviewStatus: NodeStatus = spec.reviewers.some((r) => r.status === 'Failed')
    ? 'Failed'
    : spec.reviewers.some((r) => r.status === 'Running')
      ? 'Running'
      : 'Succeeded'
  out.push(
    makeExec(f, 'review', it, reviewStatus, reviewStart, reviewStatus === 'Running' ? null : longest, {
      summary: `${doneCount}/${spec.reviewers.length} reviewers done · policy all`,
    }),
  )
  t = reviewStart - longest
  if (reviewStatus !== 'Succeeded') return out

  const routeLabel = o.kind === 'findings' || o.kind === 'closed' ? 'findings' : o.kind === 'round-limit' ? 'bot-round-limit' : 'clean'
  for (const nodeId of POST_REVIEW) {
    if (o.kind === 'failed' && o.at === nodeId) {
      step(nodeId, 'Failed', o.summary)
      return out
    }
    step(nodeId, 'Succeeded', nodeId === 'route' ? routeLabel : undefined)
  }

  const park = (nodeId: string, waitingSummary: string, doneSummary: string, waitMin: number) => {
    if (live) {
      out.push(makeExec(f, nodeId, it, 'Waiting', t, null, { summary: waitingSummary }))
    } else {
      out.push(makeExec(f, nodeId, it, 'Succeeded', t, waitMin, { summary: doneSummary }))
    }
    t -= waitMin
  }

  switch (o.kind) {
    case 'findings':
      park('await-fixes', 'parked until a push or recheck · no seat held', 'push after fixes', between(seed + 'aw', 120, 1_100))
      break
    case 'round-limit':
      park('human-review', 'queue item open for the owning team', 'decided', 60)
      break
    case 'human-approve':
      step('needs-human-approval', 'Succeeded', 'yes (sticky for the rest of the PR)')
      park('human-approve', 'queue item open for the owning team', `${o.decidedBy ?? 'a reviewer'} → ${o.decision === 'AnotherRound' ? 'another-round' : (o.decision ?? 'approve').toLowerCase()}`, between(seed + 'ha', 60, 300))
      break
    case 'bot-approve':
      step('needs-human-approval', 'Succeeded', 'no')
      step('bot-approve', 'Succeeded', `antiphon/approval = success @ ${spec.headSha}`)
      step('base-moved', 'Succeeded', 'unchanged')
      if (o.merged) out.push(makeExec(f, 'await-merge', it, 'Succeeded', t, 30, { summary: 'merged by the author' }))
      else out.push(makeExec(f, 'await-merge', it, 'Waiting', t, null, { summary: 'holding the approval until merge or close' }))
      break
    case 'human-approved':
      step('needs-human-approval', 'Succeeded', 'yes (sticky for the rest of the PR)')
      out.push(makeExec(f, 'human-approve', it, 'Succeeded', t, 45, { summary: `${o.by} → approve` }))
      t -= 45
      step('base-moved', 'Succeeded', 'unchanged')
      if (o.merged) out.push(makeExec(f, 'await-merge', it, 'Succeeded', t, 30, { summary: 'merged by the author' }))
      else out.push(makeExec(f, 'await-merge', it, 'Waiting', t, null, { summary: 'holding the approval until merge or close' }))
      break
    case 'closed':
      out.push(makeExec(f, 'await-fixes', it, 'Succeeded', t, 600, { summary: 'closed by the author' }))
      break
    default:
      break
  }
  return out
}

// ---------------------------------------------------------------- runs

interface RunSeed {
  n: number
  repo: string
  pr: number
  title: string
  headRef: string
  author: string
  resolvedBy?: string
  owners: string[]
  teamKey: string
  status: ReviewRunStatus
  stage: ReviewStage
  round: number
  botOnlyRounds: number
  needsHumanApproval: boolean | null
  waitingOn: WaitingOn | null
  startedMinAgo: number
  updatedMinAgo: number
  blast: BlastRadiusDto | null
  categories: string[]
  iterations: Omit<IterationSpec, 'iteration' | 'current'>[]
  /** hand-written executions instead of synthesis (the rich run) */
  nodes?: (f: ExecFactory) => NodeExecutionDto[]
}

const R = (group: string, model: ModelKey, status?: NodeStatus): ReviewerPlan => ({ group, model, status })
const sha = (seed: string) => Math.floor(hash01(seed) * 0xfffffff).toString(16).padStart(7, '0')

/** payments-api#618, iteration 3 mid fan-out: the rich run whose executions are written by hand. */
function run618Nodes(f: ExecFactory): NodeExecutionDto[] {
  const it1 = 3 * 24 * 60 + 300
  const it2 = 1 * 24 * 60 + 180
  const it3 = 22
  const x = makeExec
  return [
    x(f, 'intake', 1, 'Succeeded', it1, 0.2, { summary: 'base 1f20c9e · head 8d3e012 · csharp, sql · areas: ledger, refunds · size L' }),
    x(f, 'delta', 1, 'Succeeded', it1 - 0.2, 0.05, { summary: 'full (first iteration)' }),
    x(f, 'categorise', 1, 'Succeeded', it1 - 0.3, 0.05, { model: 'jev', inputTokens: 18_200, outputTokens: 300, summary: 'feature · data 5 · reach 3 · rollback 4' }),
    x(f, 'select', 1, 'Succeeded', it1 - 0.4, 0.1, { model: 'jev', inputTokens: 41_000, outputTokens: 1_300, summary: '171 candidates, 2 calls → 13 selected' }),
    x(f, 'group', 1, 'Succeeded', it1 - 0.5, 0.02, { summary: '4 groups' }),
    x(f, 'packs', 1, 'Succeeded', it1 - 0.6, 0.35, { summary: 'prefix 54.1k tokens · 18 files' }),
    x(f, 'review', 1, 'Succeeded', it1 - 1, 8.5, { summary: '4/4 reviewers done · policy all' }),
    x(f, 'reviewer', 1, 'Succeeded', it1 - 1, 4.5, { model: 'opus', inputTokens: 57_100, outputTokens: 2_900, summary: 'security: 1 finding · 2/2 checks accounted' }),
    x(f, 'reviewer', 1, 'Succeeded', it1 - 1, 8.5, { model: 'opus', inputTokens: 60_400, outputTokens: 3_400, summary: 'concurrency: 3 findings (1 major)' }),
    x(f, 'reviewer', 1, 'Succeeded', it1 - 1, 3.6, { model: 'sonnet', inputTokens: 58_000, outputTokens: 2_700, summary: 'data: 3 findings (1 major) · 3/3 checks' }),
    x(f, 'reviewer', 1, 'Succeeded', it1 - 1, 2.8, { model: 'sonnet', inputTokens: 55_300, outputTokens: 2_100, summary: 'general: 2 findings (minor)' }),
    x(f, 'collate', 1, 'Succeeded', it1 - 9.6, 0.05, { summary: '9 → 7 after dedupe' }),
    x(f, 'verify', 1, 'Succeeded', it1 - 9.7, 0.7, { model: 'luna', inputTokens: 72_000, outputTokens: 4_100, summary: '1 disproved and dropped' }),
    x(f, 'publish', 1, 'Succeeded', it1 - 10.4, 0.1, { summary: 'review with 4 inline + summary; status failure' }),
    x(f, 'route', 1, 'Succeeded', it1 - 10.5, 0.01, { summary: 'findings' }),
    x(f, 'await-fixes', 1, 'Succeeded', it1 - 10.6, 1_170, { summary: 'push 4a77b10 after 19.5 wh · base moved once (drift recorded)' }),
    x(f, 'record-drift', 1, 'Succeeded', it1 - 400, 0.01, { summary: 'base 1f20c9e..7b3e9d0 (3 commits) · no overlap with PR files · branch not updated' }),

    x(f, 'intake', 2, 'Succeeded', it2, 0.1, { summary: 'head 4a77b10' }),
    x(f, 'delta', 2, 'Succeeded', it2 - 0.1, 0.15, { summary: '4 files changed since 8d3e012 · 3 findings re-anchored, 1 outdated' }),
    x(f, 'categorise', 2, 'Succeeded', it2 - 0.25, 0, { summary: 'inputs hash unchanged → reused from iteration 1' }),
    x(f, 'select', 2, 'Succeeded', it2 - 0.3, 0.2, { model: 'jev', inputTokens: 22_100, outputTokens: 800, summary: 'delta: 58 candidates → 6 (round 2 bar drops nits)' }),
    x(f, 'group', 2, 'Succeeded', it2 - 0.5, 0.02, { summary: '2 groups' }),
    x(f, 'packs', 2, 'Succeeded', it2 - 0.55, 0.15, { summary: 'prefix 38.0k · reused 61% of chunks' }),
    x(f, 'review', 2, 'Succeeded', it2 - 0.7, 5, { summary: '2/2 reviewers done · policy all' }),
    x(f, 'reviewer', 2, 'Succeeded', it2 - 0.7, 5, { model: 'opus', inputTokens: 59_800, outputTokens: 2_600, summary: 'concurrency: 1 finding (major)' }),
    x(f, 'reviewer', 2, 'Succeeded', it2 - 0.7, 3.2, { model: 'sonnet', inputTokens: 52_000, outputTokens: 1_900, summary: 'data: 0 findings · 3/3 checks' }),
    x(f, 'collate', 2, 'Succeeded', it2 - 5.7, 0.04, { summary: '4 → 4' }),
    x(f, 'verify', 2, 'Succeeded', it2 - 5.8, 0.5, { model: 'haiku', inputTokens: 40_000, outputTokens: 2_000, summary: 'luna seat over quota → haiku · 0 dropped' }),
    x(f, 'publish', 2, 'Succeeded', it2 - 6.3, 0.1, { summary: '2 inline; summary edited' }),
    x(f, 'route', 2, 'Succeeded', it2 - 6.4, 0.01, { summary: 'clean' }),
    x(f, 'needs-human-approval', 2, 'Succeeded', it2 - 6.42, 0.01, { summary: 'yes: data 5, rollback 4 (sticky)' }),
    x(f, 'human-approve', 2, 'Succeeded', it2 - 6.45, 252, { summary: 'Hugo Martin → another-round with a steer' }),

    x(f, 'intake', 3, 'Succeeded', it3, 0.1, { summary: 'head e41b07c · steer from Hugo Martin' }),
    x(f, 'delta', 3, 'Succeeded', it3 - 0.1, 0.1, { summary: '2 files changed since 4a77b10 · 2 open findings still present' }),
    x(f, 'categorise', 3, 'Succeeded', it3 - 0.2, 0.06, { model: 'jev', inputTokens: 19_900, outputTokens: 320, summary: '2 questions escalated Jev → luna (confidence < 0.60)' }),
    x(f, 'select', 3, 'Succeeded', it3 - 0.3, 0.07, { model: 'jev', inputTokens: 40_300, outputTokens: 1_250, summary: '163 candidates → 11 (round 3 bar: major+)' }),
    x(f, 'group', 3, 'Succeeded', it3 - 0.4, 0.02, { summary: '4 groups: security, concurrency, data, general' }),
    x(f, 'packs', 3, 'Succeeded', it3 - 0.45, 0.2, { summary: 'prefix 51.7k · 14 of 17 candidates included' }),
    x(f, 'review', 3, 'Running', it3 - 0.6, null, { summary: '3/4 reviewers done · policy all' }),
    x(f, 'reviewer', 3, 'Succeeded', it3 - 0.6, 4.1, { model: 'opus', inputTokens: 58_300, outputTokens: 3_100, summary: 'security: 0 findings · 2/2 checks pass' }),
    x(f, 'reviewer', 3, 'Running', it3 - 0.6, null, { model: 'opus', inputTokens: 61_900, outputTokens: 2_400, summary: 'concurrency: running · opened 2 files beyond the pack' }),
    x(f, 'reviewer', 3, 'Succeeded', it3 - 0.6, 3.3, { model: 'sonnet', inputTokens: 59_200, outputTokens: 2_900, summary: 'data: 2 findings (1 major) · 3/3 checks accounted' }),
    x(f, 'reviewer', 3, 'Succeeded', it3 - 0.6, 2.7, { model: 'sonnet', inputTokens: 56_100, outputTokens: 2_200, summary: 'general: 1 finding (minor, below bar) · 3/3 checks' }),
  ]
}

const FOUR = [R('security', 'opus'), R('concurrency', 'opus'), R('data', 'sonnet'), R('general', 'sonnet')]

const RUN_SEEDS: RunSeed[] = [
  {
    n: 618, repo: 'lumen/payments-api', pr: 618, title: 'Ledger: reconcile partial refunds nightly', headRef: 'cwei/partial-refund-recon', author: 'cwei',
    owners: ['payments-core', 'platform-sre'], teamKey: 'payments-core', status: 'Running', stage: 'Review', round: 3, botOnlyRounds: 0,
    needsHumanApproval: true, waitingOn: 'Bot', startedMinAgo: 3 * 24 * 60 + 300, updatedMinAgo: 0.2,
    blast: blast(3, 0.18, 0.93, 4, 3, 2), categories: ['feature', 'ledger', 'migration'], iterations: [], nodes: run618Nodes,
  },
  {
    n: 1842, repo: 'lumen/antiphon', pr: 1842, title: 'Review executor: lease-based node scheduling', headRef: 'bokafor/review-executor-leases', author: 'bokafor',
    owners: ['platform-runtime'], teamKey: 'platform-runtime', status: 'Running', stage: 'Review', round: 1, botOnlyRounds: 1,
    needsHumanApproval: true, waitingOn: 'Bot', startedMinAgo: 75, updatedMinAgo: 0.7,
    blast: blast(4, 0.12, 0.71, 3, 4, 1), categories: ['feature', 'session-runtime'],
    iterations: [{ startMinAgo: 12, headSha: '3c9a1d4', reviewers: [R('concurrency', 'opus'), R('data', 'sonnet', 'Running'), R('general', 'sonnet', 'Running')], verifyModel: 'luna', outcome: { kind: 'running', until: 'review' } }],
  },
  {
    n: 2290, repo: 'lumen/web-portal', pr: 2290, title: 'Settings: migrate preferences page to Mantine 8', headRef: 'druiz/settings-mantine8', author: 'druiz',
    owners: ['web-experience'], teamKey: 'web-experience', status: 'Running', stage: 'Categorise', round: 1, botOnlyRounds: 0,
    needsHumanApproval: null, waitingOn: 'Bot', startedMinAgo: 2, updatedMinAgo: 0.05, blast: null, categories: [],
    iterations: [{ startMinAgo: 1, headSha: 'a07f5e2', reviewers: [], verifyModel: 'haiku', outcome: { kind: 'running', until: 'categorise' } }],
  },
  {
    n: 621, repo: 'lumen/payments-api', pr: 621, title: 'Bump Npgsql from 9.0.3 to 9.0.4', headRef: 'renovate/npgsql-9.x', author: 'ksato', resolvedBy: 'Dependency owner in CODEOWNERS (/Directory.Packages.props)',
    owners: ['payments-core'], teamKey: 'payments-core', status: 'Queued', stage: 'Intake', round: 1, botOnlyRounds: 0,
    needsHumanApproval: null, waitingOn: 'Bot', startedMinAgo: 1, updatedMinAgo: 1, blast: null, categories: [],
    iterations: [{ startMinAgo: 1, headSha: '5d20e8b', reviewers: [], verifyModel: 'haiku', outcome: { kind: 'queued' } }],
  },
  {
    n: 604, repo: 'lumen/payments-api', pr: 604, title: 'Webhooks: retry delivery with jittered backoff', headRef: 'lnovak/webhook-jitter', author: 'lnovak',
    owners: ['payments-core'], teamKey: 'payments-core', status: 'Running', stage: 'Select', round: 2, botOnlyRounds: 2,
    needsHumanApproval: null, waitingOn: 'Bot', startedMinAgo: 7 * 60, updatedMinAgo: 0.15,
    blast: blast(2, 0.34, 0.08, 2, 2, 3), categories: ['reliability'],
    iterations: [
      { startMinAgo: 7 * 60, headSha: 'a19f0c2', reviewers: [R('general', 'sonnet'), R('reliability', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'findings' } },
      { startMinAgo: 1, headSha: 'b812c3a', reviewers: [], verifyModel: 'luna', outcome: { kind: 'running', until: 'select' }, summaries: { delta: '2 files changed since a19f0c2 · recheck' } },
    ],
  },
  {
    n: 1839, repo: 'lumen/antiphon', pr: 1839, title: 'Pack builder: ledger rows for related files', headRef: 'jweber/pack-ledger', author: 'jweber',
    owners: ['platform-runtime'], teamKey: 'platform-runtime', status: 'AwaitingFixes', stage: 'AwaitingFixes', round: 2, botOnlyRounds: 2,
    needsHumanApproval: null, waitingOn: 'Author', startedMinAgo: 26 * 60, updatedMinAgo: 5 * 60,
    blast: blast(2, 0.05, 0.62, 2, 3, 1), categories: ['feature'],
    iterations: [
      { startMinAgo: 26 * 60, headSha: '4e2a7f1', reviewers: [R('data', 'sonnet'), R('general', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'findings' } },
      { startMinAgo: 5 * 60 + 12, headSha: '91e04ab', reviewers: [R('data', 'sonnet'), R('general', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'findings' } },
    ],
  },
  {
    n: 2284, repo: 'lumen/web-portal', pr: 2284, title: 'Checkout: persist cart across sessions', headRef: 'icosta/cart-persist', author: 'icosta',
    owners: ['web-experience'], teamKey: 'web-experience', status: 'AwaitingFixes', stage: 'AwaitingFixes', round: 1, botOnlyRounds: 1,
    needsHumanApproval: true, waitingOn: 'Author', startedMinAgo: 2 * 60 + 20, updatedMinAgo: 2 * 60,
    blast: blast(3, 0.22, 0.88, 3, 4, 5), categories: ['feature', 'persistence'],
    iterations: [{ startMinAgo: 2 * 60 + 15, headSha: 'f3e9a10', reviewers: [R('persistence', 'opus'), R('frontend', 'sonnet'), R('general', 'haiku')], verifyModel: 'haiku', outcome: { kind: 'findings' } }],
  },
  {
    n: 1846, repo: 'lumen/antiphon', pr: 1846, title: 'Teams: sync org/team membership from GitHub daily', headRef: 'delegate/card-1873-team-sync', author: 'antiphon-delegate',
    owners: ['platform-runtime'], teamKey: 'platform-runtime', status: 'AwaitingFixes', stage: 'AwaitingFixes', round: 1, botOnlyRounds: 1,
    needsHumanApproval: null, waitingOn: 'Humans', startedMinAgo: 40, updatedMinAgo: 25,
    blast: blast(3, 0.41, 0.77, 2, 3, 1), categories: ['feature', 'integration'],
    iterations: [{ startMinAgo: 38, headSha: '0aa6f21', reviewers: [R('data', 'sonnet'), R('integration', 'haiku')], verifyModel: 'luna', outcome: { kind: 'findings' } }],
  },
  {
    n: 612, repo: 'lumen/payments-api', pr: 612, title: 'Refunds: idempotency keys and partial refunds', headRef: 'asmith/refund-idempotency', author: 'asmith',
    owners: ['payments-core', 'platform-sre'], teamKey: 'payments-core', status: 'AtGate', stage: 'HumanGate', round: 3, botOnlyRounds: 3,
    needsHumanApproval: true, waitingOn: 'Humans', startedMinAgo: 2 * 24 * 60 + 500, updatedMinAgo: 18,
    blast: blast(3, 0.91, 0.84, 4, 4, 4), categories: ['feature', 'public-api', 'migration'],
    iterations: [
      { startMinAgo: 2 * 24 * 60 + 500, headSha: '9a04e71', reviewers: FOUR, verifyModel: 'luna', outcome: { kind: 'findings' } },
      { startMinAgo: 24 * 60 + 400, headSha: '7c1e9a2', reviewers: [R('security', 'opus'), R('concurrency', 'opus'), R('api-compat', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'findings' } },
      { startMinAgo: 230, headSha: '3b81f0d', reviewers: [R('security', 'opus'), R('api-compat', 'sonnet')], verifyModel: 'haiku', outcome: { kind: 'round-limit' } },
    ],
  },
  {
    n: 609, repo: 'lumen/payments-api', pr: 609, title: 'Drop legacy settlement_v1 table', headRef: 'ksato/drop-settlement-v1', author: 'ksato',
    owners: ['payments-core'], teamKey: 'payments-core', status: 'AtGate', stage: 'HumanGate', round: 2, botOnlyRounds: 0,
    needsHumanApproval: true, waitingOn: 'Humans', startedMinAgo: 30 * 60, updatedMinAgo: 60,
    blast: blast(2, 0.07, 0.97, 5, 5, 2), categories: ['migration'],
    iterations: [
      { startMinAgo: 30 * 60, headSha: '2d7b9e0', reviewers: [R('migration', 'opus'), R('data', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'findings' } },
      { startMinAgo: 6 * 60, headSha: '77c0d5e', reviewers: [R('migration', 'opus')], verifyModel: 'luna', outcome: { kind: 'human-approve' } },
    ],
  },
  {
    n: 617, repo: 'lumen/payments-api', pr: 617, title: 'Hotfix: correct VAT rounding on invoices', headRef: 'hotfix/vat-rounding', author: 'hmartin', resolvedBy: 'Co-authored-by trailer on 2/2 commits (PR opened by release-train)',
    owners: ['payments-core'], teamKey: 'payments-core', status: 'AtGate', stage: 'HumanGate', round: 1, botOnlyRounds: 0,
    needsHumanApproval: true, waitingOn: 'Humans', startedMinAgo: 6 * 60 + 30, updatedMinAgo: 6 * 60,
    blast: blast(2, 0.2, 0.3, 1, 2, 4), categories: ['hotfix', 'invoicing'],
    iterations: [{ startMinAgo: 6 * 60 + 25, headSha: 'c40d1e7', reviewers: [R('correctness', 'sonnet')], verifyModel: 'haiku', outcome: { kind: 'human-approve' } }],
  },
  {
    n: 1835, repo: 'lumen/antiphon', pr: 1835, title: 'Status checks: throttle progress descriptions', headRef: 'delegate/card-1860-status-throttle', author: 'grao', resolvedBy: 'Co-authored-by trailer on 4/4 commits (PR opened by antiphon-delegate)',
    owners: ['platform-runtime'], teamKey: 'platform-runtime', status: 'AwaitingMerge', stage: 'BranchMaintenance', round: 2, botOnlyRounds: 2,
    needsHumanApproval: false, waitingOn: 'Author', startedMinAgo: 9 * 60, updatedMinAgo: 3 * 60,
    blast: blast(1, 0.1, 0.02, 1, 1, 2), categories: ['chore'],
    iterations: [
      { startMinAgo: 9 * 60, headSha: 'a8e2c51', reviewers: [R('general', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'findings' } },
      { startMinAgo: 3 * 60 + 10, headSha: 'c5d1a09', reviewers: [R('general', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'bot-approve' } },
    ],
  },
  {
    n: 2292, repo: 'lumen/web-portal', pr: 2292, title: 'Copy: rename "Workspace" to "Project" in navigation', headRef: 'fhaddad/rename-workspace', author: 'fhaddad',
    owners: ['web-experience'], teamKey: 'web-experience', status: 'Approved', stage: 'Approval', round: 1, botOnlyRounds: 0,
    needsHumanApproval: true, waitingOn: 'Author', startedMinAgo: 110, updatedMinAgo: 50,
    blast: blast(2, 0.03, 0.01, 1, 1, 4), categories: ['copy'],
    iterations: [{ startMinAgo: 105, headSha: '28be3f7', reviewers: [R('frontend', 'haiku')], verifyModel: 'haiku', outcome: { kind: 'human-approved', by: 'Dana Ruiz' } }],
  },
  {
    n: 2279, repo: 'lumen/web-portal', pr: 2279, title: 'Date picker: fix focus trap on Escape', headRef: 'icosta/datepicker-escape', author: 'icosta',
    owners: ['web-experience'], teamKey: 'web-experience', status: 'Merged', stage: 'Done', round: 1, botOnlyRounds: 0,
    needsHumanApproval: false, waitingOn: null, startedMinAgo: 28 * 60, updatedMinAgo: 22 * 60,
    blast: blast(1, 0.02, 0, 1, 1, 3), categories: ['bugfix'],
    iterations: [{ startMinAgo: 28 * 60, headSha: '6a1f93c', reviewers: [R('frontend', 'sonnet')], verifyModel: 'luna', outcome: { kind: 'bot-approve', merged: true } }],
  },
  {
    n: 1851, repo: 'lumen/antiphon', pr: 1851, title: 'Runner doctor: check agent CLI versions at registration', headRef: 'elindqvist/doctor-cli-versions', author: 'elindqvist',
    owners: ['platform-runtime', 'platform-sre'], teamKey: 'platform-runtime', status: 'Failed', stage: 'Review', round: 1, botOnlyRounds: 0,
    needsHumanApproval: null, waitingOn: null, startedMinAgo: 95, updatedMinAgo: 80,
    blast: blast(2, 0.15, 0.05, 1, 2, 2), categories: ['feature', 'runner'],
    iterations: [{ startMinAgo: 94, headSha: 'e7f3a21', reviewers: [R('general', 'sonnet'), R('runner', 'opus', 'Failed')], verifyModel: 'luna', outcome: { kind: 'failed', at: 'review', summary: 'reviewer runner: seat lost' } }],
  },
  {
    n: 2270, repo: 'lumen/web-portal', pr: 2270, title: 'Experiment: dark mode toggle in the header', headRef: 'fhaddad/dark-mode-toggle', author: 'fhaddad',
    owners: ['web-experience'], teamKey: 'web-experience', status: 'Closed', stage: 'Done', round: 1, botOnlyRounds: 1,
    needsHumanApproval: null, waitingOn: null, startedMinAgo: 3 * 24 * 60, updatedMinAgo: 2 * 24 * 60,
    blast: blast(1, 0.01, 0.01, 1, 1, 3), categories: ['experiment'],
    iterations: [{ startMinAgo: 3 * 24 * 60, headSha: '1b0c7d4', reviewers: [R('frontend', 'haiku')], verifyModel: 'haiku', outcome: { kind: 'closed' } }],
  },
]

// ---------------------------------------------------------------- findings

interface FindingSeed {
  run: number
  key: string
  severity: Severity
  title: string
  file: string
  lines?: [number, number]
  status?: FindingStatus
  flags?: string[]
  checkId?: string | null
  checkWhy?: string
  confidence?: number
  body?: string
  suggestion?: string
  anchor?: string
  round?: number
  posted?: boolean
  events?: EventSeed[]
}

interface EventSeed {
  action: FindingAction
  by: string
  minAgo: number
  text?: string
  reason?: ReasonCategory
  scope?: DismissScope
  sync?: SyncState
}

const BYPASS_FLAGS = ['security', 'data-loss']

const FINDING_SEEDS: FindingSeed[] = [
  // payments-api#612 — the PR review split view's data
  {
    run: 612, key: 'F1', severity: 'Blocker', flags: ['security'], checkId: 'CHK-0311', confidence: 0.93, round: 1,
    title: 'Idempotency key is not scoped to the merchant', file: 'src/Refunds/IdempotencyStore.cs', lines: [41, 44],
    anchor: 'var cacheKey = $"refund:{request.IdempotencyKey}";',
    body: "Two merchants that send the same `Idempotency-Key` read each other's cached response. Client SDKs that default the key to the order number make this likely, not theoretical. Merchant B would get Merchant A's refund id and amount back, and B's refund would never run.",
    suggestion: 'var cacheKey = $"refund:{merchant.Id}:{request.IdempotencyKey}";',
    checkWhy: "Added after INC-1873 (June 2025): a shared idempotency cache returned one merchant's refund confirmation to another. Any key that deduplicates requests must include the tenant.",
    events: [{ action: 'Reply', by: BOT_LOGIN, minAgo: 24 * 60 + 380, text: 'Recheck at 7c1e9a2: still present. The key is built on line 42 without the merchant id.' }],
  },
  {
    run: 612, key: 'F2', severity: 'Major', flags: ['data-loss'], checkId: 'CHK-0087', confidence: 0.88, round: 1, status: 'WillFix',
    title: 'Migration rewrites `amount` in place; the minor-unit values are lost', file: 'db/migrations/20261118_partial_refunds.sql', lines: [12, 12],
    anchor: 'ALTER TABLE refunds ALTER COLUMN amount TYPE numeric(12,2) USING amount_minor / 100.0;',
    body: 'The `USING` clause converts minor units to decimal and drops the original. A rollback cannot restore it, and any reader still on the old build reads pounds as pence. Add `amount_decimal`, backfill, and swap readers in a later release.',
    suggestion: 'ALTER TABLE refunds ADD COLUMN amount_decimal numeric(12,2);\nUPDATE refunds SET amount_decimal = amount_minor / 100.0;',
    checkWhy: 'Destructive migrations cannot be rolled back with the release. Expand → migrate → contract keeps every step reversible.',
    events: [
      { action: 'WillFix', by: 'asmith', minAgo: 2 * 24 * 60 + 200, text: 'Will split into add / backfill / swap.' },
      { action: 'Reply', by: BOT_LOGIN, minAgo: 24 * 60 + 380, text: 'Recheck at 7c1e9a2: still present. Tracking as will-fix; the next push is checked.' },
    ],
  },
  {
    run: 612, key: 'F3', severity: 'Major', checkId: 'CHK-0142', confidence: 0.81, round: 2,
    title: 'Over-refund race: the save runs after the payment lock is released', file: 'src/Refunds/RefundService.cs', lines: [88, 95],
    anchor: 'using (await _lock.AcquireAsync(refund.PaymentId)) { refund.Apply(request); }',
    body: '`SaveChangesAsync` runs after the `using` block disposes the lock. Two concurrent partial refunds on one payment can both pass the remaining-balance check before either saves, so the payment is refunded twice.',
    suggestion: 'using (await _lock.AcquireAsync(refund.PaymentId))\n{\n    refund.Apply(request);\n    await _db.SaveChangesAsync(ct);\n}',
    checkWhy: 'Seen in three why-missed investigations. A lock that guards a read-modify-write must cover the save, not just the in-memory change.',
  },
  {
    run: 612, key: 'F4', severity: 'Major', checkId: 'CHK-0402', confidence: 0.84, round: 3, status: 'Fixed',
    title: 'Key TTL (1h) is shorter than the SDK retry window (24h)', file: 'src/Refunds/IdempotencyStore.cs', lines: [17, 17],
    anchor: 'private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(1);',
    body: 'The partner SDK retries a failed refund for up to 24 hours. After an hour the key has expired, so a late retry creates a second refund.',
    checkWhy: "The dedupe window must outlive every client's retry window, or late retries become duplicates.",
    events: [{ action: 'Fixed', by: 'asmith', minAgo: 40, text: 'Fixed in 3b81f0d: TTL is now 48h and read from RefundOptions.', sync: 'Failed' }],
  },
  {
    run: 612, key: 'F5', severity: 'Minor', checkId: 'CHK-0219', confidence: 0.72, round: 2,
    title: 'New `PartialAmount` changes the public refund contract without an OpenAPI version bump', file: 'src/Api/Contracts/RefundRequest.cs', lines: [23, 23],
    anchor: 'public decimal? PartialAmount { get; init; }',
    body: 'web-portal checkout and two partner SDKs generate clients from `openapi.yaml`. The field is additive, but `info.version` is still 3.4.0, so generated clients will not pick it up and the changelog will miss it.',
    checkWhy: 'Consumers generate clients from openapi.yaml. Unversioned changes broke the partner SDK build twice in 2025.',
  },
  {
    run: 612, key: 'F6', severity: 'Minor', checkId: null, confidence: 0.69, round: 2,
    title: 'Webhook reports the requested amount, not the settled amount', file: 'src/Refunds/RefundWebhookPublisher.cs', lines: [52, 52],
    anchor: 'await _publisher.PublishAsync(new RefundCompleted(refund.Id, request.PartialAmount ?? refund.Amount));',
    body: 'The PSP can settle a partial refund for less than requested (fees, FX rounding). `RefundCompleted` should carry `refund.SettledAmount`, or merchants reconcile against the wrong number.',
    events: [
      { action: 'Question', by: 'cwei', minAgo: 24 * 60 + 250, text: 'Does the PSP actually do that for card refunds, or only for SEPA?' },
      { action: 'Reply', by: BOT_LOGIN, minAgo: 24 * 60 + 248, text: 'From the pack: `PspRefundResponse.SettledAmount` is documented as "may differ for FX and scheme fees", and both `SepaRefundAdapter` and `CardRefundAdapter` populate it. So both.' },
    ],
  },
  {
    run: 612, key: 'F7', severity: 'Minor', checkId: null, confidence: 1, round: 3,
    title: 'No metric for duplicate-key hits (raised by Hugo Martin)', file: 'src/Refunds/IdempotencyStore.cs', lines: [43, 43],
    anchor: 'var hit = await _cache.GetAsync<RefundResult>(cacheKey);',
    body: 'We need `refunds_idempotent_replay_total` so we can see replays in the dashboard before INC-style leaks happen.',
    events: [{ action: 'Reply', by: 'hmartin', minAgo: 100, text: 'We need a counter on replay hits here.' }],
  },
  {
    run: 612, key: 'F8', severity: 'Nit', checkId: 'CHK-0033', confidence: 0.66, round: 1, posted: false,
    title: 'Inject `TimeProvider` instead of `DateTime.UtcNow`', file: 'src/Refunds/RefundService.cs', lines: [112, 112],
    anchor: 'refund.RequestedAt = DateTime.UtcNow;', body: 'The rest of the service takes `TimeProvider`, which the tests fake.',
    suggestion: 'refund.RequestedAt = _time.GetUtcNow().UtcDateTime;',
    checkWhy: 'Tests fake time through TimeProvider; DateTime.UtcNow makes them flaky around midnight.',
  },
  {
    run: 612, key: 'F9', severity: 'Minor', checkId: 'CHK-0510', confidence: 0.58, round: 3, posted: false,
    title: 'Hard-coded 100 assumes two-decimal currencies (JPY has none)', file: 'src/Refunds/Money.cs', lines: [9, 9],
    anchor: 'return minor / 100.0m;', body: "Below this round's bar (round 3: major, ≥ 0.80). Stored, not posted.",
  },
  {
    run: 612, key: 'F10', severity: 'Minor', checkId: 'CHK-0219', confidence: 0.7, round: 1, status: 'Dismissed',
    title: 'Refund reason codes are not in the published enum', file: 'src/Api/Contracts/RefundReason.cs', lines: [5, 9],
    anchor: 'public enum RefundReason { Duplicate, Fraud, Requested, Other }', body: 'Partners validate against the published enum list.',
    events: [{ action: 'Dismiss', by: 'asmith', minAgo: 2 * 24 * 60 + 100, reason: 'ByDesign', scope: 'Instance', text: 'Other is the documented catch-all; the list is published in 3.4.0.' }],
  },

  // payments-api#618 (running round 3)
  {
    run: 618, key: 'F1', severity: 'Major', checkId: 'CHK-0142', confidence: 0.83, round: 2, status: 'Disputed',
    title: 'Reconcile job reads refunds outside the ledger transaction', file: 'src/Ledger/RefundReconciler.cs', lines: [64, 71],
    anchor: 'var refunds = await _db.Refunds.Where(r => r.SettledAt >= since).ToListAsync(ct);',
    body: 'The reconciler loads settled refunds before opening the ledger transaction, so a refund that settles in between is counted twice on the next run.',
    events: [
      { action: 'Dispute', by: 'cwei', minAgo: 24 * 60 + 100, text: 'The reconciler runs at 02:00 with settlement paused (SettlementWindowGuard).' },
      { action: 'Reply', by: BOT_LOGIN, minAgo: 24 * 60 + 95, text: 'Adjudicator held: SettlementWindowGuard is opt-in and the nightly job does not register it.' },
    ],
  },
  { run: 618, key: 'F2', severity: 'Minor', checkId: 'CHK-0510', confidence: 0.71, round: 3, title: 'Rounding mode differs from the invoicing service', file: 'src/Ledger/Money.cs', lines: [14, 14], anchor: 'Math.Round(amount, 2)', body: 'Invoicing uses banker’s rounding; the ledger uses away-from-zero, so totals drift by a cent.' },
  { run: 618, key: 'F3', severity: 'Major', checkId: 'CHK-0087', confidence: 0.86, round: 1, status: 'Fixed', flags: ['data-loss'], title: 'Backfill deletes rows it cannot map', file: 'db/migrations/20261115_refund_recon.sql', lines: [30, 34], anchor: 'DELETE FROM refund_recon WHERE ledger_id IS NULL;', body: 'Unmapped rows are deleted rather than parked.', events: [{ action: 'Fixed', by: 'cwei', minAgo: 2 * 24 * 60, text: 'Parked into refund_recon_unmapped instead.' }] },
  { run: 618, key: 'F4', severity: 'Minor', checkId: 'CHK-0033', confidence: 0.67, round: 1, status: 'Fixed', title: 'Use TimeProvider in the reconciler', file: 'src/Ledger/RefundReconciler.cs', lines: [22, 22], anchor: 'var since = DateTime.UtcNow.AddDays(-1);', body: 'Inject `TimeProvider`.', events: [{ action: 'Fixed', by: 'cwei', minAgo: 2 * 24 * 60 }] },

  // payments-api#604 (recheck selecting)
  { run: 604, key: 'F1', severity: 'Minor', checkId: 'CHK-0402', confidence: 0.74, round: 1, title: 'Jitter is applied before the cap', file: 'src/Webhooks/RetryPolicy.cs', lines: [31, 31], anchor: 'var delay = Cap(baseDelay * Math.Pow(2, attempt)) + Jitter();', body: 'The jitter can push the delay above the documented 10 minute cap.' },
  { run: 604, key: 'F2', severity: 'Minor', checkId: null, confidence: 0.69, round: 1, status: 'WillFix', title: 'Retry count is not logged on final failure', file: 'src/Webhooks/WebhookSender.cs', lines: [88, 88], anchor: '_logger.LogWarning("Webhook delivery failed");', body: 'Operators cannot tell a first failure from a final one.', events: [{ action: 'WillFix', by: 'lnovak', minAgo: 300 }] },

  // antiphon#1839 (waiting on author)
  { run: 1839, key: 'F1', severity: 'Major', checkId: 'CHK-0702', confidence: 0.82, round: 2, title: 'Ledger rows are written after the pack is sealed', file: 'server/Application/Review/PackBuilder.cs', lines: [140, 152], anchor: 'pack.Seal(); await WriteLedgerAsync(pack, ct);', body: 'A reader that opens the sealed pack sees no ledger rows for the last related files.' },
  { run: 1839, key: 'F2', severity: 'Minor', checkId: 'CHK-0033', confidence: 0.7, round: 1, title: 'Ledger timestamps use DateTime.UtcNow', file: 'server/Application/Review/PackLedger.cs', lines: [18, 18], anchor: 'At = DateTime.UtcNow', body: 'Inject `TimeProvider`.' },
  { run: 1839, key: 'F3', severity: 'Minor', checkId: null, confidence: 0.66, round: 2, title: 'Related-file reason strings are not localised in the UI', file: 'client/src/features/review/PackViewer.tsx', lines: [55, 55], anchor: '{row.reason}', body: 'Reasons are raw enum names.' },
  { run: 1839, key: 'F4', severity: 'Nit', checkId: null, confidence: 0.6, round: 1, status: 'Fixed', title: 'Typo in PackLedger doc comment', file: 'server/Application/Review/PackLedger.cs', lines: [3, 3], anchor: '/// Ledgr row', body: 'Ledgr → Ledger.', events: [{ action: 'Fixed', by: 'jweber', minAgo: 20 * 60 }] },

  // web-portal#2284 (blocker, data-loss)
  { run: 2284, key: 'F1', severity: 'Blocker', flags: ['data-loss'], checkId: 'CHK-0733', confidence: 0.9, round: 1, title: 'Cart state is persisted without a schema version', file: 'src/checkout/cartStorage.ts', lines: [12, 19], anchor: "localStorage.setItem('cart', JSON.stringify(cart))", body: 'Unversioned client storage cannot be migrated; the next cart shape change silently drops every saved cart.', checkWhy: 'Unversioned client storage cannot be migrated (INC-2210).' },
  { run: 2284, key: 'F2', severity: 'Major', checkId: 'CHK-0618', confidence: 0.77, round: 1, title: 'Storage listener is never removed', file: 'src/checkout/useCart.ts', lines: [40, 46], anchor: "window.addEventListener('storage', onStorage)", body: 'Every mount adds another listener; carts update twice after a remount.' },
  { run: 2284, key: 'F3', severity: 'Minor', checkId: null, confidence: 0.68, round: 1, title: 'Cart is restored before prices are refreshed', file: 'src/checkout/useCart.ts', lines: [22, 22], anchor: 'setCart(restore())', body: 'Users briefly see stale prices.' },
  { run: 2284, key: 'F4', severity: 'Minor', checkId: null, confidence: 0.65, round: 1, title: 'No size limit on the stored cart', file: 'src/checkout/cartStorage.ts', lines: [14, 14], anchor: 'JSON.stringify(cart)', body: 'A large cart can exceed the storage quota and throw.' },

  // antiphon#1846 (author unknown)
  { run: 1846, key: 'F1', severity: 'Major', checkId: 'CHK-0741', confidence: 0.8, round: 1, title: 'Membership sync enqueues jobs inside the DB transaction', file: 'server/Application/Teams/TeamSyncService.cs', lines: [77, 84], anchor: 'BackgroundJob.Enqueue(() => NotifyAsync(team.Id));', body: 'The job can run before the commit is visible and read the old membership.' },
  { run: 1846, key: 'F2', severity: 'Minor', checkId: null, confidence: 0.67, round: 1, title: 'Removed members keep their open queue items', file: 'server/Application/Teams/TeamSyncService.cs', lines: [101, 101], anchor: 'team.Members.Remove(member);', body: 'Their assigned gates are not returned to the team queue.' },

  // payments-api#617 (hotfix at gate)
  { run: 617, key: 'F1', severity: 'Nit', checkId: 'CHK-0510', confidence: 0.62, round: 1, posted: false, title: 'Rounding precision constant could be named', file: 'src/Invoicing/Vat.cs', lines: [8, 8], anchor: 'Math.Round(vat, 2, MidpointRounding.ToEven)', body: 'Name the 2 (VatPrecision).' },

  // payments-api#609 (clean round 2)
  { run: 609, key: 'F1', severity: 'Major', flags: ['data-loss'], checkId: 'CHK-0087', confidence: 0.9, round: 1, status: 'Fixed', title: 'Table is dropped before readers are switched', file: 'db/migrations/20261110_drop_settlement_v1.sql', lines: [3, 3], anchor: 'DROP TABLE settlement_v1;', body: 'SettlementReportJob still reads settlement_v1.', events: [{ action: 'Fixed', by: 'ksato', minAgo: 7 * 60, text: 'Report job moved to settlement_v2 in the same PR.' }] },
  { run: 609, key: 'F2', severity: 'Minor', checkId: null, confidence: 0.7, round: 1, status: 'Fixed', title: 'No Down step', file: 'db/migrations/20261110_drop_settlement_v1.sql', lines: [1, 1], anchor: '-- up only', body: 'Add a Down that fails safely.', events: [{ action: 'Fixed', by: 'ksato', minAgo: 7 * 60 }] },

  // antiphon#1835 (bot-approved)
  { run: 1835, key: 'F1', severity: 'Minor', checkId: null, confidence: 0.72, round: 1, status: 'Fixed', title: 'Throttle window is not configurable', file: 'server/Application/Review/StatusPublisher.cs', lines: [20, 20], anchor: 'TimeSpan.FromSeconds(5)', body: 'Read it from ReviewSettings.', events: [{ action: 'Fixed', by: 'grao', minAgo: 4 * 60 }] },
  { run: 1835, key: 'F2', severity: 'Nit', checkId: null, confidence: 0.61, round: 1, status: 'Fixed', title: 'Unused using directive', file: 'server/Application/Review/StatusPublisher.cs', lines: [4, 4], anchor: 'using System.Linq;', body: 'Remove it.', events: [{ action: 'Fixed', by: 'grao', minAgo: 4 * 60 }] },

  // web-portal#2279 (merged)
  { run: 2279, key: 'F1', severity: 'Minor', checkId: 'CHK-0618', confidence: 0.75, round: 1, status: 'Fixed', title: 'Escape handler not removed on unmount', file: 'src/components/DatePicker.tsx', lines: [61, 64], anchor: "document.addEventListener('keydown', onKey)", body: 'Leaks a listener per open.', events: [{ action: 'Fixed', by: 'icosta', minAgo: 25 * 60 }] },
]

// ---------------------------------------------------------------- gates

interface GateSeed {
  n: number
  run: number
  kind: GateKind
  status: QueueItemStatus
  teamKey: string
  assignee: string | null
  openedMinAgo: number
  reasons: string[]
  passedBy?: string[]
  decided?: { minAgo: number; decision: GateDecision; steer?: string }
}

const GATE_SEEDS: GateSeed[] = [
  {
    n: 1, run: 612, kind: 'ReviewGate', status: 'InProgress', teamKey: 'payments-core', assignee: 'hmartin', openedMinAgo: 216,
    reasons: ['Bot-only rounds 3/3 → human-review gate', 'External contract 5 ≥ 4', 'Reversal after months 4 ≥ 4', 'Category: public-api'],
  },
  {
    n: 2, run: 609, kind: 'ApprovalGate', status: 'Unassigned', teamKey: 'payments-core', assignee: null, openedMinAgo: 336, passedBy: ['cwei'],
    reasons: ['Category: migration', 'Reversal after months 5 ≥ 4', 'Destructive migration, no Down step'],
  },
  {
    n: 3, run: 618, kind: 'DisputedFindings', status: 'Unassigned', teamKey: 'payments-core', assignee: null, openedMinAgo: 72,
    reasons: ['Author disputed F1 twice; the adjudicator held both times', 'Escalated to the owning team, not the author'],
  },
  {
    n: 4, run: 1846, kind: 'ReviewGate', status: 'Unassigned', teamKey: 'platform-runtime', assignee: null, openedMinAgo: 24,
    reasons: ['Author unknown: opened by antiphon-delegate and no signal resolved a human', 'Findings need an author: anyone in platform-runtime can claim it'],
  },
  {
    n: 5, run: 1842, kind: 'ApprovalGate', status: 'Assigned', teamKey: 'platform-runtime', assignee: 'grao', openedMinAgo: 48,
    reasons: ['Size: large (1,380 lines)', 'Reversal after months 4 ≥ 4', 'Pre-assigned while reviewers run'],
  },
  {
    n: 6, run: 617, kind: 'ApprovalGate', status: 'Assigned', teamKey: 'payments-core', assignee: 'asmith', openedMinAgo: 366,
    reasons: ['Hotfix label → human required', 'Resolved author Hugo Martin is excluded from the pick'],
  },
  {
    n: 7, run: 2284, kind: 'ApprovalGate', status: 'Assigned', teamKey: 'web-experience', assignee: 'druiz', openedMinAgo: 118,
    reasons: ['Data 5 · persistent client storage', 'Exposure 5 · checkout is the critical path'],
  },
  {
    n: 8, run: 618, kind: 'ApprovalGate', status: 'Done', teamKey: 'payments-core', assignee: 'hmartin', openedMinAgo: 24 * 60 + 174,
    reasons: ['Data 5 · rollback 4 (sticky)'],
    decided: { minAgo: 24 * 60 - 78, decision: 'AnotherRound', steer: 'Check the refund ledger reconciliation path against the double-entry invariant in src/Ledger/README.md.' },
  },
  {
    n: 9, run: 2292, kind: 'ApprovalGate', status: 'Done', teamKey: 'web-experience', assignee: 'druiz', openedMinAgo: 100,
    reasons: ['Exposure 4 · navigation copy on every page'], decided: { minAgo: 52, decision: 'Approve' },
  },
]

// ---------------------------------------------------------------- assembly

export interface RunMeta {
  teamKey: string
  reviewers: ReviewerPlan[]
  verifyModel: ModelKey
  headSha: string
}

export interface FakeSeed {
  runs: ReviewRunDetailDto[]
  findings: ReviewFindingDto[]
  meta: Record<string, RunMeta>
  nextN: number
}

export function runIdFor(n: number): string {
  return seedGuid(ID_KIND.run, n)
}

export function createSeed(now: number): FakeSeed {
  let counter = 1
  const nextId = () => seedGuid(ID_KIND.node, counter++)
  const factory: ExecFactory = { nextId, now }
  const runs: ReviewRunDetailDto[] = []
  const meta: Record<string, RunMeta> = {}

  for (const s of RUN_SEEDS) {
    const id = runIdFor(s.n)
    const last = s.iterations[s.iterations.length - 1]
    const subject: ReviewSubjectDto = {
      id: seedGuid(ID_KIND.subject, s.n),
      host: 'GitHub',
      repo: s.repo,
      prNumber: s.pr,
      title: s.title,
      url: `https://github.com/${s.repo}/pull/${s.pr}`,
      authorLogin: s.author,
      authorDisplayName: personName(s.author),
      authorResolvedBy: s.resolvedBy ?? null,
      baseRef: 'main',
      headRef: s.headRef,
      baseSha: sha(`${s.n}:base`),
      headSha: last?.headSha ?? (s.n === 618 ? 'e41b07c' : sha(`${s.n}:head`)),
      owners: s.owners,
    }
    const nodes = s.nodes
      ? s.nodes(factory)
      : s.iterations.flatMap((it, i) =>
          synthIteration(factory, { ...it, iteration: i + 1, current: i === s.iterations.length - 1 }, `${s.n}`),
        )
    const gates: ReviewGateDto[] = GATE_SEEDS.filter((g) => g.run === s.n).map((g) => ({
      id: seedGuid(ID_KIND.gate, g.n),
      runId: id,
      kind: g.kind,
      status: g.status,
      teamKey: g.teamKey,
      assigneeLogin: g.assignee,
      openedAt: iso(now, g.openedMinAgo),
      decidedAt: g.decided ? iso(now, g.decided.minAgo) : null,
      decision: g.decided?.decision ?? null,
      steerPrompt: g.decided?.steer ?? null,
      reasons: g.reasons,
      passedBy: g.passedBy ?? [],
    }))
    runs.push({
      id,
      subject,
      status: s.status,
      stage: s.stage,
      round: s.round,
      botOnlyRounds: s.botOnlyRounds,
      needsHumanApproval: s.needsHumanApproval,
      openFindings: 0,
      openBlockers: 0,
      models: [],
      costUsd: 0,
      startedAt: iso(now, s.startedMinAgo),
      updatedAt: iso(now, s.updatedMinAgo),
      waitingOn: s.waitingOn,
      blastRadius: s.blast,
      categories: s.categories,
      workflowRevisionId: '5eed0007-0000-4000-8000-000000000001',
      nodes,
      gates,
    })
    meta[id] = {
      teamKey: s.teamKey,
      reviewers: last?.reviewers.length ? last.reviewers.map((r) => ({ group: r.group, model: r.model })) : FOUR.map((r) => ({ ...r })),
      verifyModel: last?.verifyModel ?? 'luna',
      headSha: subject.headSha,
    }
  }

  let eventN = 1
  const findings: ReviewFindingDto[] = FINDING_SEEDS.map((fs, i) => {
    const runId = runIdFor(fs.run)
    const posted = fs.posted ?? true
    const events: FindingEventDto[] = (fs.events ?? []).map((e) => ({
      id: seedGuid(ID_KIND.event, eventN++),
      action: e.action,
      actorLogin: e.by,
      actorDisplayName: personName(e.by),
      reasonCategory: e.reason ?? null,
      scope: e.scope ?? null,
      text: e.text ?? null,
      syncState: posted ? (e.sync ?? 'Synced') : 'NotApplicable',
      createdAt: iso(now, e.minAgo),
    }))
    const flags = fs.flags ?? []
    return {
      id: seedGuid(ID_KIND.finding, i + 1),
      runId,
      key: fs.key,
      checkId: fs.checkId === undefined ? null : fs.checkId,
      checkWhy: fs.checkWhy ?? null,
      severity: fs.severity,
      flags,
      bypassesRoundBar: flags.some((f) => BYPASS_FLAGS.includes(f)),
      confidence: fs.confidence ?? 0.7,
      filePath: fs.file,
      lineStart: fs.lines?.[0] ?? null,
      lineEnd: fs.lines?.[1] ?? null,
      anchor: fs.anchor ?? '',
      title: fs.title,
      body: fs.body ?? '',
      suggestion: fs.suggestion ?? null,
      status: fs.status ?? 'Open',
      roundRaised: fs.round ?? 1,
      posted,
      syncState: !posted ? 'NotApplicable' : events.some((e) => e.syncState === 'Failed') ? 'Failed' : events.some((e) => e.syncState === 'Pending') ? 'Pending' : 'Synced',
      externalThreadUrl: posted ? `https://github.com/${runs.find((r) => r.id === runId)?.subject.repo}/pull/${fs.run}#discussion_r${4_100_000 + i * 37}` : null,
      events,
    }
  })

  return { runs, findings, meta, nextN: Math.max(counter, eventN) + 1000 }
}
