/**
 * PROPOSAL MOCK — not production code. Fictional data for the review-engine Storybook
 * prototypes (`docs/features/012-review-engine/design.md`). Nothing outside
 * `src/stories/review-engine/` imports this file. Org, people, PRs and incidents are invented.
 */

// ---------------------------------------------------------------- stages, severities, models

export type Stage =
  | 'intake'
  | 'categorising'
  | 'selecting'
  | 'reviewing'
  | 'waiting-author'
  | 'human-gate'
  | 'approved'
  | 'merged'

export const STAGES: Stage[] = [
  'intake',
  'categorising',
  'selecting',
  'reviewing',
  'waiting-author',
  'human-gate',
  'approved',
  'merged',
]

export const STAGE_META: Record<Stage, { label: string; color: string; hint: string }> = {
  intake: { label: 'Intake', color: 'gray', hint: 'Pinned base/head, mirror fetched, code-only tags' },
  categorising: { label: 'Categorising', color: 'cyan', hint: 'One batched Jev call: kind, risk areas, blast radius' },
  selecting: { label: 'Selecting', color: 'teal', hint: 'Prefilter + Jev `applies` per candidate check' },
  reviewing: { label: 'Reviewing', color: 'active', hint: 'Reviewer groups fanned out on runner seats' },
  'waiting-author': { label: 'Waiting on author', color: 'warning', hint: 'Findings posted; parked on await-fixes' },
  'human-gate': { label: 'Human gate', color: 'pink', hint: 'Parked on a gate; queue item open' },
  approved: { label: 'Approved', color: 'success', hint: 'antiphon/approval = success; merge left to author' },
  merged: { label: 'Merged', color: 'violet', hint: 'Run closed' },
}

export type Severity = 'nit' | 'minor' | 'major' | 'blocker'
export const SEVERITIES: Severity[] = ['nit', 'minor', 'major', 'blocker']
export const SEVERITY_META: Record<Severity, { label: string; color: string; rank: number }> = {
  nit: { label: 'nit', color: 'gray', rank: 0 },
  minor: { label: 'minor', color: 'yellow', rank: 1 },
  major: { label: 'major', color: 'warning', rank: 2 },
  blocker: { label: 'blocker', color: 'danger', rank: 3 },
}

export type Flag = 'security' | 'data-loss'
export const FLAG_META: Record<Flag, { label: string; color: string }> = {
  security: { label: 'security', color: 'red' },
  'data-loss': { label: 'data-loss', color: 'grape' },
}

export type ModelKey = 'jev' | 'luna' | 'haiku' | 'grok' | 'sonnet' | 'opus'
export interface ModelInfo {
  key: ModelKey
  name: string
  short: string
  provider: string
  transport: 'HTTP' | 'seat' | 'CLI'
  billing: 'api' | 'subscription'
  color: string
  role: string
}

export const MODELS: Record<ModelKey, ModelInfo> = {
  jev: { key: 'jev', short: 'jev', name: 'jev-1.14.0', provider: 'LiteLLM proxy /v1/systemone', transport: 'HTTP', billing: 'api', color: 'cyan', role: 'decisions (chain head)' },
  luna: { key: 'luna', short: 'luna', name: 'gpt-5.6-luna', provider: 'Codex seat pool', transport: 'seat', billing: 'subscription', color: 'green', role: 'decision fallback 1 · verify' },
  haiku: { key: 'haiku', short: 'haiku', name: 'haiku', provider: 'Claude seat pool', transport: 'seat', billing: 'subscription', color: 'orange', role: 'decision fallback 2 · verify' },
  grok: { key: 'grok', short: 'grok-low', name: 'grok-4.7 low', provider: 'Grok seat pool', transport: 'seat', billing: 'subscription', color: 'gray', role: 'decision fallback 3' },
  sonnet: { key: 'sonnet', short: 'sonnet', name: 'sonnet', provider: 'Claude seat pool', transport: 'seat', billing: 'subscription', color: 'grape', role: 'reviewer (standard tier)' },
  opus: { key: 'opus', short: 'opus', name: 'opus', provider: 'Claude seat pool', transport: 'seat', billing: 'subscription', color: 'violet', role: 'reviewer (high tier)' },
}

/**
 * Subscription quota per seat model, as the runner's quota probe last read it. Dollar figures for
 * these models are API-equivalent estimates (list price × tokens), never a bill.
 */
export const SEAT_QUOTA: Partial<Record<ModelKey, { pctUsed: number; window: string; resets: string }>> = {
  luna: { pctUsed: 23, window: '5 h window', resets: 'resets today 18:40' },
  haiku: { pctUsed: 11, window: 'weekly', resets: 'resets Mon 09:00' },
  sonnet: { pctUsed: 38, window: 'weekly', resets: 'resets Mon 09:00' },
  opus: { pctUsed: 44, window: 'weekly (Opus cap)', resets: 'resets Mon 09:00' },
  grok: { pctUsed: 2, window: 'daily', resets: 'resets 00:00 UTC' },
}

// ---------------------------------------------------------------- org, teams, people

export const ORG = 'lumen'
export type Repo = 'antiphon' | 'payments-api' | 'web-portal'
export const REPOS: Repo[] = ['antiphon', 'payments-api', 'web-portal']

export type TeamKey = 'platform' | 'payments' | 'web'
export const TEAMS: Record<TeamKey, { key: TeamKey; name: string; slug: string; repo: Repo; lead: string; slaWh: number }> = {
  platform: { key: 'platform', name: 'Platform Runtime', slug: 'platform-runtime', repo: 'antiphon', lead: 'grao', slaWh: 8 },
  payments: { key: 'payments', name: 'Payments Core', slug: 'payments-core', repo: 'payments-api', lead: 'hmartin', slaWh: 4 },
  web: { key: 'web', name: 'Web Experience', slug: 'web-experience', repo: 'web-portal', lead: 'fhaddad', slaWh: 8 },
}

export interface Person {
  login: string
  name: string
  team?: TeamKey
  optedIn?: boolean
  service?: boolean
  openLoad?: number
}

export const PEOPLE: Record<string, Person> = {
  asmith: { login: 'asmith', name: 'Alice Smith', team: 'payments', optedIn: true, openLoad: 2 },
  hmartin: { login: 'hmartin', name: 'Hugo Martin', team: 'payments', optedIn: true, openLoad: 3 },
  cwei: { login: 'cwei', name: 'Chen Wei', team: 'payments', optedIn: true, openLoad: 1 },
  ksato: { login: 'ksato', name: 'Kenji Sato', team: 'payments', optedIn: true, openLoad: 2 },
  lnovak: { login: 'lnovak', name: 'Lena Novak', team: 'payments', optedIn: false, openLoad: 0 },
  bokafor: { login: 'bokafor', name: 'Ben Okafor', team: 'platform', optedIn: true, openLoad: 1 },
  elindqvist: { login: 'elindqvist', name: 'Erik Lindqvist', team: 'platform', optedIn: true, openLoad: 4 },
  grao: { login: 'grao', name: 'Gita Rao', team: 'platform', optedIn: true, openLoad: 2 },
  jweber: { login: 'jweber', name: 'Jonas Weber', team: 'platform', optedIn: false, openLoad: 0 },
  druiz: { login: 'druiz', name: 'Dana Ruiz', team: 'web', optedIn: true, openLoad: 2 },
  fhaddad: { login: 'fhaddad', name: 'Farah Haddad', team: 'web', optedIn: true, openLoad: 1 },
  icosta: { login: 'icosta', name: 'Ines Costa', team: 'web', optedIn: true, openLoad: 3 },
  'renovate[bot]': { login: 'renovate[bot]', name: 'renovate[bot]', service: true },
  'antiphon-delegate': { login: 'antiphon-delegate', name: 'antiphon-delegate', service: true },
  'release-train': { login: 'release-train', name: 'release-train', service: true },
}

export const VIEWER = PEOPLE.asmith
export const BOT_HANDLE = '@MikeysBot'

export function personName(login: string | undefined): string {
  if (!login) return 'Unassigned'
  return PEOPLE[login]?.name ?? login
}

// ---------------------------------------------------------------- code owners (teams and users, from CODEOWNERS)

export interface Owner {
  kind: 'team' | 'user'
  /** team slug or user login */
  name: string
  members?: number
}

const team = (name: string, members: number): Owner => ({ kind: 'team', name, members })
const user = (name: string): Owner => ({ kind: 'user', name })

/** CODEOWNERS entries the mock PRs touch. A path can have several owners: any of them can be routed to. */
export const CODE_OWNERS: Record<string, Owner[]> = {
  'payments-api:/src/Refunds/**': [team('payments-core', 5), team('platform-sre', 3)],
  'payments-api:/src/Invoicing/**': [team('payments-core', 5), user('hmartin')],
  'payments-api:/db/**': [team('payments-core', 5), team('data-platform', 4)],
  'payments-api:/src/Ledger/**': [team('payments-core', 5)],
  'payments-api:/tools/refund-replay/**': [team('refund-tooling', 1)],
  'antiphon:/server/Application/Review/**': [team('platform-runtime', 4), user('grao')],
  'antiphon:/server/Teams/**': [team('platform-runtime', 4)],
  'antiphon:/docs/investigations/**': [team('platform-runtime', 4), team('platform-sre', 3)],
  'web-portal:/src/checkout/**': [team('web-experience', 3), team('payments-core', 5)],
}

export function ownersText(owners: Owner[]): string {
  return owners.map((o) => (o.kind === 'user' ? `@${o.name}` : o.name)).join(', ')
}

// ---------------------------------------------------------------- trust (per user, per scope; stored in Antiphon)

export type TrustScopeKind = 'repo' | 'team' | 'area'

export interface TrustAdjustment {
  kind: 'offset' | 'pinned'
  value: number
  reason: string
  by: string
  at: string
  expires?: string
}

export interface TrustScore {
  login: string
  scopeKind: TrustScopeKind
  scope: string
  /** computed from outcomes over the last 180 days */
  computed: number
  basis: string
  adj?: TrustAdjustment
}

export const TRUST: TrustScore[] = [
  { login: 'hmartin', scopeKind: 'repo', scope: 'payments-api', computed: 88, basis: '19 gates · 4 disputes won · 0 wrong dismissals', adj: { kind: 'offset', value: 3, reason: 'Wrote the refund and settlement modules; on-call lead', by: 'grao', at: '2026-09-14' } },
  { login: 'asmith', scopeKind: 'repo', scope: 'payments-api', computed: 82, basis: '14 gates · 11 suggestions merged · 0 wrong dismissals' },
  { login: 'cwei', scopeKind: 'repo', scope: 'payments-api', computed: 64, basis: '9 gates · 1 wrong dismissal · 3 disputes lost' },
  { login: 'ksato', scopeKind: 'repo', scope: 'payments-api', computed: 49, basis: '22 gates · 3 rubber-stamp approvals · 1 wrong dismissal', adj: { kind: 'offset', value: -8, reason: '3 approvals in under 2 minutes on large PRs (Oct); revisit after pairing with Hugo', by: 'hmartin', at: '2026-10-30', expires: '2026-12-31' } },
  { login: 'lnovak', scopeKind: 'repo', scope: 'payments-api', computed: 22, basis: 'joined Oct · 2 gates' },
  { login: 'hmartin', scopeKind: 'team', scope: 'payments-core', computed: 90, basis: 'across payments-api, ledger-jobs' },
  { login: 'asmith', scopeKind: 'team', scope: 'payments-core', computed: 79, basis: 'across payments-api, ledger-jobs' },
  { login: 'ksato', scopeKind: 'team', scope: 'payments-core', computed: 52, basis: 'across payments-api, ledger-jobs' },
  { login: 'hmartin', scopeKind: 'area', scope: 'ledger', computed: 84, basis: 'files under src/Ledger/**', adj: { kind: 'pinned', value: 95, reason: 'Owner of the double-entry invariant (src/Ledger/README.md)', by: 'grao', at: '2026-08-02' } },
  { login: 'asmith', scopeKind: 'area', scope: 'ledger', computed: 58, basis: 'files under src/Ledger/**' },
  { login: 'grao', scopeKind: 'repo', scope: 'antiphon', computed: 93, basis: '31 gates · 6 learnings merged' },
  { login: 'bokafor', scopeKind: 'repo', scope: 'antiphon', computed: 71, basis: '12 gates · 2 learnings merged' },
  { login: 'elindqvist', scopeKind: 'repo', scope: 'antiphon', computed: 66, basis: '17 gates · 2 SLA breaches' },
  { login: 'druiz', scopeKind: 'repo', scope: 'web-portal', computed: 77, basis: '15 gates · 1 dispute lost' },
  { login: 'icosta', scopeKind: 'repo', scope: 'web-portal', computed: 61, basis: '8 gates' },
]

export function effectiveTrust(t: TrustScore): number {
  if (!t.adj) return t.computed
  const v = t.adj.kind === 'pinned' ? t.adj.value : t.computed + t.adj.value
  return Math.max(0, Math.min(100, v))
}

export function trustOf(login: string, repo: string): number | undefined {
  const t = TRUST.find((x) => x.login === login && x.scopeKind === 'repo' && x.scope === repo)
  return t ? effectiveTrust(t) : undefined
}

export type ThresholdKey = 'clear-blocker' | 'dismiss-wide' | 'decide-gate' | 'random-assign'

export interface TrustThreshold {
  key: ThresholdKey
  label: string
  /** the action it gates */
  gates: string
  instance: number
  team: Partial<Record<TeamKey, number>>
  above: string
  below: string
  belowKind: 'second-reviewer' | 'proposal' | 'not-allowed' | 'not-assignable'
}

export const TRUST_THRESHOLDS: TrustThreshold[] = [
  {
    key: 'clear-blocker', label: 'Clear a blocker alone', gates: '"Not a blocker" on a blocker finding', instance: 60, team: { web: 55 },
    above: 'The blocker is cleared. The bot can approve once everything else is clean.', below: 'Needs a second trusted reviewer: a queue item opens for the owning team.', belowKind: 'second-reviewer',
  },
  {
    key: 'dismiss-wide', label: 'Dismiss at repo scope', gates: 'Dismiss with scope "this path" or "this repo"', instance: 70, team: { payments: 75 },
    above: 'The learn PR opens directly with the rule.', below: 'Becomes a proposal for a repo maintainer to accept.', belowKind: 'proposal',
  },
  {
    key: 'decide-gate', label: 'Decide a gate', gates: 'Approve / another round / close on a human gate', instance: 50, team: {},
    above: 'Can be assigned a gate and decide it.', below: "Can comment on the gate but can't decide it; the gate is assigned to someone else.", belowKind: 'not-allowed',
  },
  {
    key: 'random-assign', label: 'Eligible for random assignment', gates: 'Picked by the queue router (weighted by open load)', instance: 30, team: {},
    above: 'In the random-assignment pool for the owning team.', below: "Can't be assigned; still sees the team queue.", belowKind: 'not-assignable',
  },
]

export function thresholdFor(key: ThresholdKey, teamKey?: TeamKey): { value: number; source: 'instance' | 'team' } {
  const t = TRUST_THRESHOLDS.find((x) => x.key === key)!
  const o = teamKey ? t.team[teamKey] : undefined
  return o === undefined ? { value: t.instance, source: 'instance' } : { value: o, source: 'team' }
}

/** Who changed a settings table and when. Settings are rows in Antiphon's database, not files in a repo. */
export interface SettingRevision {
  rev: number
  at: string
  by: string
  layer: 'instance' | TeamKey
  change: string
  reason?: string
}

export const TRUST_THRESHOLD_REVISIONS: SettingRevision[] = [
  { rev: 6, at: '2026-11-18 10:12', by: 'hmartin', layer: 'payments', change: 'dismiss-wide 70 → 75 (team override)', reason: 'Two repo-scope dismissals in Nov later linked to INC-2291' },
  { rev: 5, at: '2026-11-02 15:40', by: 'fhaddad', layer: 'web', change: 'clear-blocker 60 → 55 (team override)', reason: 'Small team; 60 left only one eligible person' },
  { rev: 4, at: '2026-10-21 09:05', by: 'grao', layer: 'instance', change: 'random-assign 25 → 30' },
  { rev: 3, at: '2026-10-03 11:30', by: 'grao', layer: 'instance', change: 'added decide-gate at 50' },
]

export interface BypassCategory {
  key: string
  label: string
  on: boolean
  hint: string
  changed?: { by: string; at: string }
}

/** Categories whose findings post in any round, ignoring the round's severity/confidence bar. */
export const BYPASS_CATEGORIES: BypassCategory[] = [
  { key: 'security', label: 'security', on: true, hint: 'Auth, secrets, injection, tenant isolation' },
  { key: 'data-loss', label: 'data-loss', on: true, hint: 'Destructive migrations, overwrites, unrecoverable deletes' },
  { key: 'compliance', label: 'compliance', on: false, hint: 'PCI scope, audit-log gaps, retention rules', changed: { by: 'hmartin', at: '2026-10-12' } },
]

// ---------------------------------------------------------------- blast radius

export interface Blast {
  /** score 1..5 */
  reach: number
  /** noul probability 0..1 */
  externalContract: number
  /** noul probability 0..1 */
  data: number
  /** score 1..5 */
  rollback: number
  /** score 1..5 */
  reversal: number
  /** score 1..5 */
  exposure: number
}

export type BlastKey = keyof Blast
export interface BlastDim {
  key: BlastKey
  short: string
  label: string
  kind: 'score' | 'noul'
  levels?: string[]
  evidence: string
}

export const BLAST_DIMS: BlastDim[] = [
  { key: 'reach', short: 'R', label: 'Reach', kind: 'score', levels: ['One place', 'A few', 'A module', 'Many modules', 'System-wide'], evidence: 'Fan-in of changed symbols, referencing projects, consumers in other repos' },
  { key: 'externalContract', short: 'C', label: 'External contract', kind: 'noul', evidence: 'API/proto/schema globs, public types, config keys' },
  { key: 'data', short: 'D', label: 'Persistent data', kind: 'noul', evidence: 'Migrations, ORM models, storage clients, serialisation formats' },
  { key: 'rollback', short: 'Rb', label: 'Rollback at release', kind: 'score', levels: ['Trivial', 'Easy', 'Moderate', 'Hard', 'Very hard'], evidence: 'Feature flag, stateless, additive vs destructive migration, Down step' },
  { key: 'reversal', short: 'Rv', label: 'Reversal after months', kind: 'score', levels: ['Trivial', 'Easy', 'Moderate', 'Hard', 'One-way door'], evidence: 'New persisted formats or identifiers, newly published contracts' },
  { key: 'exposure', short: 'E', label: 'Exposure', kind: 'score', levels: ['Internal only', 'Low', 'Medium', 'High', 'Critical path'], evidence: 'Routes, UI paths, traffic' },
]

/** 0..1 for drawing. */
export function blastNorm(b: Blast, key: BlastKey): number {
  const d = BLAST_DIMS.find((x) => x.key === key)!
  const v = b[key]
  return d.kind === 'noul' ? v : (v - 1) / 4
}

export function blastText(b: Blast, key: BlastKey): string {
  const d = BLAST_DIMS.find((x) => x.key === key)!
  const v = b[key]
  return d.kind === 'noul' ? `${v >= 0.5 ? 'yes' : 'no'} (p=${v.toFixed(2)})` : `${v} · ${d.levels![v - 1]}`
}

/** Thresholds from policy.yaml §5 humanRequiredWhen. */
export function blastTrips(b: Blast): string[] {
  const out: string[] = []
  if (b.externalContract >= 0.6) out.push(`external_contract ${b.externalContract.toFixed(2)} ≥ 0.6`)
  if (b.reversal >= 4) out.push(`reversal_after_months ${b.reversal} ≥ 4`)
  return out
}

// ---------------------------------------------------------------- formatting

// Cost is formatted by fmtCost in shared.tsx (one formatter for every story).

export function fmtTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`
  if (n >= 1000) return `${(n / 1000).toFixed(n >= 100_000 ? 0 : 1)}k`
  return String(n)
}

export function fmtWh(h: number): string {
  if (h < 1) return `${Math.round(h * 60)} wm`
  return `${h.toFixed(h < 10 ? 1 : 0)} wh`
}

// ---------------------------------------------------------------- review runs

export interface Cost {
  /** actual metered API spend (Jev via LiteLLM `x-litellm-response-cost`) */
  api: number
  /** API-equivalent estimate of subscription seat usage (list price × tokens; not billed) */
  seatEq: number
}

export interface ReviewRun {
  id: string
  repo: Repo
  number: number
  title: string
  branch: string
  author: string
  resolvedAuthor?: { login: string; via: string; confidence: number } | 'unknown'
  team: TeamKey
  stage: Stage
  stageDetail: string
  round: number
  botOnlyRounds: number
  iteration: number
  fanOut?: { done: number; total: number }
  models: ModelKey[]
  tokensIn: number
  tokensOut: number
  cost: Cost
  findings: { open: number; posted: number; accepted: number; dismissed: number; belowBar: number }
  topSeverity?: Severity
  blast?: Blast
  statusReview: { state: 'pending' | 'success' | 'failure'; text: string }
  statusApproval: 'pending' | 'success' | 'none'
  updated: string
  ageWh: number
  assignee?: string
  changed: { files: number; add: number; del: number }
  humanRequired?: string[]
  headSha: string
}

export const RUNS: ReviewRun[] = [
  {
    id: 'RR-3118', repo: 'payments-api', number: 618, title: 'Ledger: reconcile partial refunds nightly', branch: 'cwei/partial-refund-recon',
    author: 'cwei', team: 'payments', stage: 'reviewing', stageDetail: 'reviewers 3/4 · steer', round: 3, botOnlyRounds: 0, iteration: 3,
    fanOut: { done: 3, total: 4 }, models: ['jev', 'luna', 'haiku', 'sonnet', 'opus'], tokensIn: 1_284_000, tokensOut: 41_200,
    cost: { api: 0.061, seatEq: 9.84 }, findings: { open: 2, posted: 6, accepted: 4, dismissed: 0, belowBar: 3 }, topSeverity: 'major',
    blast: { reach: 3, externalContract: 0.18, data: 0.93, rollback: 4, reversal: 3, exposure: 2 },
    statusReview: { state: 'pending', text: 'Round 3 · 11 checks · 3/4 reviewers done' }, statusApproval: 'pending',
    updated: '12s ago', ageWh: 14.5, changed: { files: 17, add: 642, del: 118 }, headSha: 'e41b07c',
  },
  {
    id: 'RR-3121', repo: 'antiphon', number: 1842, title: 'Review executor: lease-based node scheduling', branch: 'bokafor/review-executor-leases',
    author: 'bokafor', team: 'platform', stage: 'reviewing', stageDetail: 'reviewers 1/3', round: 1, botOnlyRounds: 1, iteration: 1,
    fanOut: { done: 1, total: 3 }, models: ['jev', 'haiku', 'sonnet', 'opus'], tokensIn: 486_000, tokensOut: 12_900,
    cost: { api: 0.031, seatEq: 3.4 }, findings: { open: 0, posted: 0, accepted: 0, dismissed: 0, belowBar: 0 },
    blast: { reach: 4, externalContract: 0.12, data: 0.71, rollback: 3, reversal: 4, exposure: 1 },
    statusReview: { state: 'pending', text: 'Categorised · 14 checks · 1/3 reviewers done' }, statusApproval: 'pending',
    updated: '40s ago', ageWh: 1.2, changed: { files: 23, add: 1284, del: 96 }, headSha: '3c9a1d4',
    humanRequired: ['reversal_after_months 4 ≥ 4', 'size: large (1,380 lines)'],
  },
  {
    id: 'RR-3124', repo: 'web-portal', number: 2290, title: 'Settings: migrate preferences page to Mantine 8', branch: 'druiz/settings-mantine8',
    author: 'druiz', team: 'web', stage: 'categorising', stageDetail: 'Jev · 27 questions', round: 1, botOnlyRounds: 0, iteration: 1,
    models: ['jev'], tokensIn: 22_400, tokensOut: 0, cost: { api: 0.0009, seatEq: 0 },
    findings: { open: 0, posted: 0, accepted: 0, dismissed: 0, belowBar: 0 },
    statusReview: { state: 'pending', text: 'Categorising (Jev, 27 questions)' }, statusApproval: 'pending',
    updated: '3s ago', ageWh: 0.1, changed: { files: 9, add: 311, del: 287 }, headSha: 'a07f5e2',
  },
  {
    id: 'RR-3125', repo: 'payments-api', number: 621, title: 'Bump Npgsql from 9.0.3 to 9.0.4', branch: 'renovate/npgsql-9.x',
    author: 'renovate[bot]', resolvedAuthor: { login: 'ksato', via: 'Dependency owner in CODEOWNERS (/Directory.Packages.props)', confidence: 0.9 },
    team: 'payments', stage: 'intake', stageDetail: 'queued, 2 ahead', round: 1, botOnlyRounds: 0, iteration: 1,
    models: [], tokensIn: 0, tokensOut: 0, cost: { api: 0, seatEq: 0 },
    findings: { open: 0, posted: 0, accepted: 0, dismissed: 0, belowBar: 0 },
    statusReview: { state: 'pending', text: 'Picked up by Antiphon (queued, 2 ahead)' }, statusApproval: 'pending',
    updated: '1m ago', ageWh: 0.05, changed: { files: 1, add: 1, del: 1 }, headSha: '5d20e8b',
  },
  {
    id: 'RR-3119', repo: 'payments-api', number: 604, title: 'Webhooks: retry delivery with jittered backoff', branch: 'lnovak/webhook-jitter',
    author: 'lnovak', team: 'payments', stage: 'selecting', stageDetail: 'Jev applies · 148 candidates', round: 2, botOnlyRounds: 2, iteration: 2,
    models: ['jev', 'luna'], tokensIn: 211_000, tokensOut: 1_300, cost: { api: 0.009, seatEq: 0.41 },
    findings: { open: 2, posted: 3, accepted: 1, dismissed: 0, belowBar: 1 }, topSeverity: 'minor',
    blast: { reach: 2, externalContract: 0.34, data: 0.08, rollback: 2, reversal: 2, exposure: 3 },
    statusReview: { state: 'pending', text: 'Recheck (delta 2 files) · selecting checks' }, statusApproval: 'pending',
    updated: '9s ago', ageWh: 6.2, changed: { files: 4, add: 88, del: 31 }, headSha: 'b812c3a',
  },
  {
    id: 'RR-3102', repo: 'antiphon', number: 1839, title: 'Pack builder: ledger rows for related files', branch: 'jweber/pack-ledger',
    author: 'jweber', team: 'platform', stage: 'waiting-author', stageDetail: '3 open · last push 5h ago', round: 2, botOnlyRounds: 2, iteration: 2,
    models: ['jev', 'luna', 'sonnet'], tokensIn: 702_000, tokensOut: 19_800, cost: { api: 0.022, seatEq: 4.95 },
    findings: { open: 3, posted: 5, accepted: 2, dismissed: 0, belowBar: 2 }, topSeverity: 'major',
    blast: { reach: 2, externalContract: 0.05, data: 0.62, rollback: 2, reversal: 3, exposure: 1 },
    statusReview: { state: 'failure', text: '3 findings (1 major)' }, statusApproval: 'pending',
    updated: '5h ago', ageWh: 9.0, changed: { files: 11, add: 506, del: 42 }, headSha: '91e04ab',
  },
  {
    id: 'RR-3110', repo: 'web-portal', number: 2284, title: 'Checkout: persist cart across sessions', branch: 'icosta/cart-persist',
    author: 'icosta', team: 'web', stage: 'waiting-author', stageDetail: '1 blocker (data-loss)', round: 1, botOnlyRounds: 1, iteration: 1,
    models: ['jev', 'haiku', 'sonnet', 'opus'], tokensIn: 932_000, tokensOut: 27_400, cost: { api: 0.04, seatEq: 7.1 },
    findings: { open: 4, posted: 4, accepted: 0, dismissed: 0, belowBar: 5 }, topSeverity: 'blocker',
    blast: { reach: 3, externalContract: 0.22, data: 0.88, rollback: 3, reversal: 4, exposure: 5 },
    statusReview: { state: 'failure', text: '4 findings (1 blocker, data-loss)' }, statusApproval: 'pending',
    updated: '2h ago', ageWh: 3.1, changed: { files: 14, add: 488, del: 77 }, headSha: 'f3e9a10',
  },
  {
    id: 'RR-3127', repo: 'antiphon', number: 1846, title: 'Teams: sync org/team membership from GitHub daily', branch: 'delegate/card-1873-team-sync',
    author: 'antiphon-delegate', resolvedAuthor: 'unknown', team: 'platform', stage: 'waiting-author', stageDetail: 'author unknown → team queue',
    round: 1, botOnlyRounds: 1, iteration: 1, models: ['jev', 'luna', 'haiku', 'sonnet'], tokensIn: 548_000, tokensOut: 15_100,
    cost: { api: 0.027, seatEq: 3.86 }, findings: { open: 2, posted: 2, accepted: 0, dismissed: 0, belowBar: 1 }, topSeverity: 'major',
    blast: { reach: 3, externalContract: 0.41, data: 0.77, rollback: 2, reversal: 3, exposure: 1 },
    statusReview: { state: 'failure', text: '2 findings (1 major)' }, statusApproval: 'pending',
    updated: '25m ago', ageWh: 2.4, changed: { files: 19, add: 903, del: 12 }, headSha: '0aa6f21',
  },
  {
    id: 'RR-3088', repo: 'payments-api', number: 612, title: 'Refunds: idempotency keys and partial refunds', branch: 'asmith/refund-idempotency',
    author: 'asmith', team: 'payments', stage: 'human-gate', stageDetail: 'human-review · bot-round limit 3/3', round: 3, botOnlyRounds: 3, iteration: 3,
    models: ['jev', 'luna', 'haiku', 'sonnet', 'opus'], tokensIn: 2_310_000, tokensOut: 66_000, cost: { api: 0.094, seatEq: 17.6 },
    findings: { open: 6, posted: 9, accepted: 3, dismissed: 1, belowBar: 4 }, topSeverity: 'blocker',
    blast: { reach: 3, externalContract: 0.91, data: 0.84, rollback: 4, reversal: 4, exposure: 4 },
    statusReview: { state: 'failure', text: '6 open (1 blocker) · waiting on human review (payments-core)' }, statusApproval: 'pending',
    updated: '18m ago', ageWh: 21.5, assignee: 'hmartin', changed: { files: 21, add: 774, del: 140 }, headSha: '3b81f0d',
    humanRequired: ['external_contract 0.91 ≥ 0.6', 'reversal_after_months 4 ≥ 4', 'category: public-api', 'bot-only rounds 3/3'],
  },
  {
    id: 'RR-3093', repo: 'payments-api', number: 609, title: 'Drop legacy settlement_v1 table', branch: 'ksato/drop-settlement-v1',
    author: 'ksato', team: 'payments', stage: 'human-gate', stageDetail: 'human-approve · clean round', round: 2, botOnlyRounds: 0, iteration: 2,
    models: ['jev', 'luna', 'sonnet', 'opus'], tokensIn: 611_000, tokensOut: 14_700, cost: { api: 0.028, seatEq: 4.42 },
    findings: { open: 0, posted: 2, accepted: 2, dismissed: 0, belowBar: 1 },
    blast: { reach: 2, externalContract: 0.07, data: 0.97, rollback: 5, reversal: 5, exposure: 2 },
    statusReview: { state: 'success', text: 'Clean round 2 · waiting on human approval (payments-core)' }, statusApproval: 'pending',
    updated: '1h ago', ageWh: 5.6, changed: { files: 3, add: 24, del: 391 }, headSha: '77c0d5e',
    humanRequired: ['category: migration', 'reversal_after_months 5 ≥ 4', 'destructive migration, no Down step'],
  },
  {
    id: 'RR-3079', repo: 'antiphon', number: 1835, title: 'Status checks: throttle progress descriptions', branch: 'delegate/card-1860-status-throttle',
    author: 'antiphon-delegate', resolvedAuthor: { login: 'grao', via: 'Co-authored-by trailer on 4/4 commits', confidence: 0.97 },
    team: 'platform', stage: 'approved', stageDetail: 'bot-approved · left to author', round: 2, botOnlyRounds: 2, iteration: 2,
    models: ['jev', 'luna', 'sonnet'], tokensIn: 388_000, tokensOut: 9_900, cost: { api: 0.017, seatEq: 2.6 },
    findings: { open: 0, posted: 2, accepted: 2, dismissed: 0, belowBar: 1 },
    blast: { reach: 1, externalContract: 0.1, data: 0.02, rollback: 1, reversal: 1, exposure: 2 },
    statusReview: { state: 'success', text: 'Clean · 2 findings fixed' }, statusApproval: 'success',
    updated: '3h ago', ageWh: 7.5, changed: { files: 5, add: 142, del: 38 }, headSha: 'c5d1a09',
  },
  {
    id: 'RR-3112', repo: 'web-portal', number: 2292, title: 'Copy: rename "Workspace" to "Project" in navigation', branch: 'fhaddad/rename-workspace',
    author: 'fhaddad', team: 'web', stage: 'approved', stageDetail: 'approved by Dana Ruiz', round: 1, botOnlyRounds: 0, iteration: 1,
    models: ['jev', 'haiku'], tokensIn: 61_000, tokensOut: 1_200, cost: { api: 0.003, seatEq: 0.22 },
    findings: { open: 0, posted: 0, accepted: 0, dismissed: 0, belowBar: 2 },
    blast: { reach: 2, externalContract: 0.03, data: 0.01, rollback: 1, reversal: 1, exposure: 4 },
    statusReview: { state: 'success', text: 'Clean round 1' }, statusApproval: 'success',
    updated: '50m ago', ageWh: 1.9, changed: { files: 12, add: 46, del: 46 }, headSha: '28be3f7',
  },
  {
    id: 'RR-3071', repo: 'web-portal', number: 2279, title: 'Date picker: fix focus trap on Escape', branch: 'icosta/datepicker-escape',
    author: 'icosta', team: 'web', stage: 'merged', stageDetail: 'merged by Ines Costa', round: 1, botOnlyRounds: 0, iteration: 1,
    models: ['jev', 'sonnet'], tokensIn: 144_000, tokensOut: 3_100, cost: { api: 0.006, seatEq: 0.95 },
    findings: { open: 0, posted: 1, accepted: 1, dismissed: 0, belowBar: 0 },
    blast: { reach: 1, externalContract: 0.02, data: 0.0, rollback: 1, reversal: 1, exposure: 3 },
    statusReview: { state: 'success', text: '1 finding fixed' }, statusApproval: 'success',
    updated: 'yesterday', ageWh: 4.0, changed: { files: 2, add: 19, del: 6 }, headSha: '6a1f93c',
  },
  {
    id: 'RR-3066', repo: 'antiphon', number: 1830, title: 'Comment triage: debounce review submissions', branch: 'elindqvist/triage-debounce',
    author: 'elindqvist', team: 'platform', stage: 'merged', stageDetail: 'merged by Erik Lindqvist', round: 2, botOnlyRounds: 0, iteration: 2,
    models: ['jev', 'luna', 'sonnet'], tokensIn: 512_000, tokensOut: 13_600, cost: { api: 0.02, seatEq: 3.3 },
    findings: { open: 0, posted: 3, accepted: 2, dismissed: 1, belowBar: 2 },
    blast: { reach: 2, externalContract: 0.08, data: 0.12, rollback: 1, reversal: 2, exposure: 1 },
    statusReview: { state: 'success', text: '2 fixed, 1 dismissed (by design)' }, statusApproval: 'success',
    updated: 'yesterday', ageWh: 11.0, changed: { files: 6, add: 210, del: 54 }, headSha: 'd09e6b2',
  },
]

export function runRef(r: Pick<ReviewRun, 'repo' | 'number'>): string {
  return `${r.repo}#${r.number}`
}

// ---------------------------------------------------------------- provider usage (today)

export interface ProviderUsage {
  model: ModelKey
  calls: number
  escalationsIn: number
  tokensIn: number
  cost: Cost
  quotaPct?: number
}

export const PROVIDER_USAGE_TODAY: ProviderUsage[] = [
  { model: 'jev', calls: 1_412, escalationsIn: 0, tokensIn: 9_860_000, cost: { api: 0.41, seatEq: 0 } },
  { model: 'luna', calls: 88, escalationsIn: 61, tokensIn: 1_140_000, cost: { api: 0, seatEq: 6.9 }, quotaPct: 23 },
  { model: 'haiku', calls: 41, escalationsIn: 9, tokensIn: 620_000, cost: { api: 0, seatEq: 1.1 }, quotaPct: 11 },
  { model: 'grok', calls: 3, escalationsIn: 3, tokensIn: 41_000, cost: { api: 0, seatEq: 0.05 }, quotaPct: 2 },
  { model: 'sonnet', calls: 29, escalationsIn: 0, tokensIn: 5_420_000, cost: { api: 0, seatEq: 31.8 }, quotaPct: 38 },
  { model: 'opus', calls: 9, escalationsIn: 0, tokensIn: 2_160_000, cost: { api: 0, seatEq: 27.4 }, quotaPct: 44 },
]

// ---------------------------------------------------------------- findings (payments-api#612)

export type FindingStatus = 'open' | 'will-fix' | 'fixed' | 'dismissed' | 'pending-sync' | 'outdated'
export type ResponseAction = 'will-fix' | 'fixed' | 'dismiss' | 'dispute' | 'question' | 'defer' | 'not-blocker'
export type DismissReason = 'false-positive' | 'by-design' | 'out-of-scope' | 'accepted-risk' | 'fix-later' | 'duplicate' | 'style-preference'
export type DismissScope = 'instance' | 'path' | 'repo' | 'team'

export const ACTION_META: Record<ResponseAction, { label: string; color: string; key: string; verb: string }> = {
  'will-fix': { label: 'Will fix', color: 'active', key: 'w', verb: 'Will fix in the next push.' },
  fixed: { label: 'Fixed', color: 'success', key: 'f', verb: 'Fixed.' },
  dismiss: { label: 'Dismiss', color: 'gray', key: 'd', verb: 'Dismissed' },
  dispute: { label: 'Dispute', color: 'danger', key: 'x', verb: 'Disputed:' },
  question: { label: 'Question', color: 'cyan', key: 'q', verb: 'Question:' },
  defer: { label: 'Defer', color: 'warning', key: 'l', verb: 'Deferred to a follow-up.' },
  /** blocker findings only; clears the blocker alone at or above the clear-blocker trust threshold */
  'not-blocker': { label: 'Not a blocker', color: 'orange', key: 'b', verb: 'Not a blocker:' },
}

export const REASON_META: Record<DismissReason, { label: string; key: string }> = {
  'false-positive': { label: 'False positive', key: '1' },
  'by-design': { label: 'By design', key: '2' },
  'out-of-scope': { label: 'Out of scope', key: '3' },
  'accepted-risk': { label: 'Accepted risk', key: '4' },
  'fix-later': { label: 'Fix later', key: '5' },
  duplicate: { label: 'Duplicate', key: '6' },
  'style-preference': { label: 'Style preference', key: '7' },
}

export const SCOPE_META: Record<DismissScope, { label: string; hint: string; key: string }> = {
  instance: { label: 'This instance', hint: 'Only this finding', key: 'i' },
  path: { label: 'This path', hint: 'Suppress the check for src/Refunds/**', key: 'p' },
  repo: { label: 'This repo', hint: 'Learn proposal PR to .antiphon/review/ in payments-api', key: 'r' },
  team: { label: 'Suggest to team', hint: 'Proposal to the payments-core prompt repo (needs a maintainer)', key: 't' },
}

export interface ThreadEntry {
  who: string
  kind: 'bot' | 'human'
  at: string
  body: string
  via?: boolean
}

export interface DiffLine {
  n: number
  t: ' ' | '+' | '-'
  code: string
}

export interface Finding {
  id: string
  check: string
  checkTitle: string
  severity: Severity
  flags: Flag[]
  confidence: number
  file: string
  lines: string
  anchor: string
  title: string
  body: string
  suggestion?: string
  status: FindingStatus
  round: number
  inline: boolean
  humansWhy?: string
  checkPrecision?: number
  thread: ThreadEntry[]
  hunk: DiffLine[]
  reviewer: string
  sync?: { error: string; attempt: number; max: number; next: string; pendingReply: string }
  humanRaisedBy?: string
  belowBar?: boolean
}

export const PR612 = RUNS.find((r) => r.number === 612)!

export const FINDINGS: Finding[] = [
  {
    id: 'F1', check: 'CHK-0311', checkTitle: 'Dedupe keys must include the tenant', severity: 'blocker', flags: ['security'], confidence: 0.93,
    file: 'src/Refunds/IdempotencyStore.cs', lines: '41-44', anchor: 'var cacheKey = $"refund:{request.IdempotencyKey}";',
    title: 'Idempotency key is not scoped to the merchant',
    body: 'Two merchants that send the same `Idempotency-Key` read each other\'s cached response. Client SDKs that default the key to the order number make this likely, not theoretical. Merchant B would get Merchant A\'s refund id and amount back, and B\'s refund would never run.',
    suggestion: 'var cacheKey = $"refund:{merchant.Id}:{request.IdempotencyKey}";',
    status: 'open', round: 1, inline: true, reviewer: 'security · opus', checkPrecision: 0.86,
    humansWhy: 'Added after INC-1873 (June 2025): a shared idempotency cache returned one merchant\'s refund confirmation to another. Any key that deduplicates requests must include the tenant.',
    thread: [
      { who: BOT_HANDLE, kind: 'bot', at: 'Thu 14:02', body: 'Idempotency key is not scoped to the merchant (blocker, security).' },
      { who: BOT_HANDLE, kind: 'bot', at: 'Fri 09:15', body: 'Recheck at 7c1e9a2: still present. The key is built on line 42 without the merchant id.' },
    ],
    hunk: [
      { n: 39, t: ' ', code: '    public async Task<RefundResult?> TryGetAsync(Merchant merchant, RefundRequest request)' },
      { n: 40, t: ' ', code: '    {' },
      { n: 41, t: '-', code: '        var cacheKey = $"refund:{request.OrderId}";' },
      { n: 42, t: '+', code: '        var cacheKey = $"refund:{request.IdempotencyKey}";' },
      { n: 43, t: '+', code: '        var hit = await _cache.GetAsync<RefundResult>(cacheKey);' },
      { n: 44, t: '+', code: '        return hit;' },
      { n: 45, t: ' ', code: '    }' },
    ],
  },
  {
    id: 'F2', check: 'CHK-0087', checkTitle: 'Destructive column change without backfill', severity: 'major', flags: ['data-loss'], confidence: 0.88,
    file: 'db/migrations/20261118_partial_refunds.sql', lines: '12', anchor: 'ALTER TABLE refunds ALTER COLUMN amount TYPE numeric(12,2) USING amount_minor / 100.0;',
    title: 'Migration rewrites `amount` in place; the minor-unit values are lost',
    body: 'The `USING` clause converts minor units to decimal and drops the original. A rollback cannot restore it, and any reader still on the old build reads pounds as pence. Add `amount_decimal`, backfill, and swap readers in a later release.',
    suggestion: 'ALTER TABLE refunds ADD COLUMN amount_decimal numeric(12,2);\nUPDATE refunds SET amount_decimal = amount_minor / 100.0;',
    status: 'will-fix', round: 1, inline: true, reviewer: 'data · sonnet', checkPrecision: 0.79,
    humansWhy: 'Destructive migrations cannot be rolled back with the release. Expand → migrate → contract keeps every step reversible.',
    thread: [
      { who: BOT_HANDLE, kind: 'bot', at: 'Thu 14:02', body: 'Migration rewrites `amount` in place (major, data-loss).' },
      { who: 'asmith', kind: 'human', at: 'Thu 16:40', body: 'Will split into add / backfill / swap.', via: true },
      { who: BOT_HANDLE, kind: 'bot', at: 'Fri 09:15', body: 'Recheck at 7c1e9a2: still present. Tracking as will-fix; the next push is checked.' },
    ],
    hunk: [
      { n: 10, t: ' ', code: 'BEGIN;' },
      { n: 11, t: ' ', code: '' },
      { n: 12, t: '+', code: 'ALTER TABLE refunds ALTER COLUMN amount TYPE numeric(12,2) USING amount_minor / 100.0;' },
      { n: 13, t: '+', code: 'ALTER TABLE refunds ADD COLUMN parent_refund_id uuid NULL REFERENCES refunds(id);' },
      { n: 14, t: ' ', code: 'COMMIT;' },
    ],
  },
  {
    id: 'F3', check: 'CHK-0142', checkTitle: 'Lock released before async save completes', severity: 'major', flags: [], confidence: 0.81,
    file: 'src/Refunds/RefundService.cs', lines: '88-95', anchor: 'using (await _lock.AcquireAsync(refund.PaymentId)) { refund.Apply(request); }',
    title: 'Over-refund race: the save runs after the payment lock is released',
    body: '`SaveChangesAsync` runs after the `using` block disposes the lock. Two concurrent partial refunds on one payment can both pass the remaining-balance check before either saves, so the payment is refunded twice.',
    suggestion: 'using (await _lock.AcquireAsync(refund.PaymentId))\n{\n    refund.Apply(request);\n    await _db.SaveChangesAsync(ct);\n}',
    status: 'open', round: 2, inline: true, reviewer: 'concurrency · opus', checkPrecision: 0.74,
    humansWhy: 'Seen in three why-missed investigations (CARD-1234 and two others). A lock that guards a read-modify-write must cover the save, not just the in-memory change.',
    thread: [{ who: BOT_HANDLE, kind: 'bot', at: 'Fri 09:15', body: 'Over-refund race: the save runs after the payment lock is released (major).' }],
    hunk: [
      { n: 86, t: ' ', code: '        var refund = await _db.Refunds.SingleAsync(r => r.Id == id, ct);' },
      { n: 87, t: ' ', code: '        EnsureRemainingBalance(refund, request.PartialAmount);' },
      { n: 88, t: '+', code: '        using (await _lock.AcquireAsync(refund.PaymentId)) { refund.Apply(request); }' },
      { n: 89, t: '+', code: '' },
      { n: 90, t: '+', code: '        await _db.SaveChangesAsync(ct);' },
      { n: 91, t: ' ', code: '        return RefundResult.From(refund);' },
    ],
  },
  {
    id: 'F4', check: 'CHK-0402', checkTitle: 'Dedupe TTL shorter than the retry window', severity: 'major', flags: [], confidence: 0.84,
    file: 'src/Refunds/IdempotencyStore.cs', lines: '17', anchor: 'private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(1);',
    title: 'Key TTL (1h) is shorter than the SDK retry window (24h)',
    body: 'The partner SDK retries a failed refund for up to 24 hours. After an hour the key has expired, so a late retry creates a second refund.',
    status: 'pending-sync', round: 3, inline: true, reviewer: 'api-compat · sonnet', checkPrecision: 0.91,
    humansWhy: 'The dedupe window must outlive every client\'s retry window, or late retries become duplicates.',
    thread: [{ who: BOT_HANDLE, kind: 'bot', at: 'Fri 15:51', body: 'Key TTL (1h) is shorter than the SDK retry window (24h) (major).' }],
    sync: { error: 'GitHub 502 Bad Gateway on POST /pulls/612/comments/…/replies', attempt: 2, max: 5, next: 'in 40s', pendingReply: 'Fixed in 3b81f0d: TTL is now 48h and read from RefundOptions.' },
    hunk: [
      { n: 15, t: ' ', code: 'public sealed class IdempotencyStore' },
      { n: 16, t: ' ', code: '{' },
      { n: 17, t: '+', code: '    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(1);' },
      { n: 18, t: ' ', code: '    private readonly IDistributedCache _cache;' },
    ],
  },
  {
    id: 'F5', check: 'CHK-0219', checkTitle: 'Public contract change without a version bump', severity: 'minor', flags: [], confidence: 0.72,
    file: 'src/Api/Contracts/RefundRequest.cs', lines: '23', anchor: 'public decimal? PartialAmount { get; init; }',
    title: 'New `PartialAmount` changes the public refund contract without an OpenAPI version bump',
    body: 'web-portal checkout and two partner SDKs generate clients from `openapi.yaml`. The field is additive, but `info.version` is still 3.4.0, so generated clients will not pick it up and the changelog will miss it.',
    status: 'open', round: 2, inline: true, reviewer: 'api-compat · sonnet', checkPrecision: 0.68,
    humansWhy: 'Consumers generate clients from openapi.yaml. Unversioned changes broke the partner SDK build twice in 2025.',
    thread: [{ who: BOT_HANDLE, kind: 'bot', at: 'Fri 09:15', body: 'New `PartialAmount` changes the public refund contract without a version bump (minor).' }],
    hunk: [
      { n: 21, t: ' ', code: '    public required string IdempotencyKey { get; init; }' },
      { n: 22, t: ' ', code: '    public string? Reason { get; init; }' },
      { n: 23, t: '+', code: '    public decimal? PartialAmount { get; init; }' },
      { n: 24, t: ' ', code: '}' },
    ],
  },
  {
    id: 'F6', check: 'none', checkTitle: 'Unprompted (no check)', severity: 'minor', flags: [], confidence: 0.69,
    file: 'src/Refunds/RefundWebhookPublisher.cs', lines: '52', anchor: 'await _publisher.PublishAsync(new RefundCompleted(refund.Id, request.PartialAmount ?? refund.Amount));',
    title: 'Webhook reports the requested amount, not the settled amount',
    body: 'The PSP can settle a partial refund for less than requested (fees, FX rounding). `RefundCompleted` should carry `refund.SettledAmount`, or merchants reconcile against the wrong number.',
    status: 'open', round: 2, inline: true, reviewer: 'general · sonnet',
    thread: [
      { who: BOT_HANDLE, kind: 'bot', at: 'Fri 09:15', body: 'Webhook reports the requested amount, not the settled amount (minor).' },
      { who: 'cwei', kind: 'human', at: 'Fri 11:02', body: 'Does the PSP actually do that for card refunds, or only for SEPA?' },
      { who: BOT_HANDLE, kind: 'bot', at: 'Fri 11:04', body: 'From the pack: `PspRefundResponse.SettledAmount` is documented as "may differ for FX and scheme fees", and `SepaRefundAdapter` and `CardRefundAdapter` both populate it. So both.' },
    ],
    hunk: [
      { n: 50, t: ' ', code: '        if (refund.State != RefundState.Settled) return;' },
      { n: 51, t: '-', code: '        await _publisher.PublishAsync(new RefundCompleted(refund.Id, refund.Amount));' },
      { n: 52, t: '+', code: '        await _publisher.PublishAsync(new RefundCompleted(refund.Id, request.PartialAmount ?? refund.Amount));' },
    ],
  },
  {
    id: 'F7', check: 'human', checkTitle: 'Human-raised', severity: 'minor', flags: [], confidence: 1,
    file: 'src/Refunds/IdempotencyStore.cs', lines: '43', anchor: 'var hit = await _cache.GetAsync<RefundResult>(cacheKey);',
    title: 'No metric for duplicate-key hits',
    body: 'We need `refunds_idempotent_replay_total` so we can see replays in the dashboard before INC-style leaks happen.',
    status: 'open', round: 3, inline: true, reviewer: 'Hugo Martin', humanRaisedBy: 'hmartin',
    thread: [{ who: 'hmartin', kind: 'human', at: 'Fri 16:20', body: 'We need a counter on replay hits here.' }],
    hunk: [
      { n: 42, t: '+', code: '        var cacheKey = $"refund:{request.IdempotencyKey}";' },
      { n: 43, t: '+', code: '        var hit = await _cache.GetAsync<RefundResult>(cacheKey);' },
      { n: 44, t: '+', code: '        return hit;' },
    ],
  },
  {
    id: 'F8', check: 'CHK-0033', checkTitle: 'Use TimeProvider, not DateTime.UtcNow', severity: 'nit', flags: [], confidence: 0.66,
    file: 'src/Refunds/RefundService.cs', lines: '112', anchor: 'refund.RequestedAt = DateTime.UtcNow;',
    title: 'Inject `TimeProvider` instead of `DateTime.UtcNow`',
    body: 'The rest of the service takes `TimeProvider`, which the tests fake.',
    suggestion: 'refund.RequestedAt = _time.GetUtcNow().UtcDateTime;',
    status: 'open', round: 1, inline: false, reviewer: 'general · sonnet', checkPrecision: 0.52,
    humansWhy: 'Tests fake time through TimeProvider; DateTime.UtcNow makes them flaky around midnight.',
    thread: [], hunk: [{ n: 112, t: '+', code: '        refund.RequestedAt = DateTime.UtcNow;' }],
  },
  {
    id: 'F9', check: 'CHK-0510', checkTitle: 'Magic numbers in money arithmetic', severity: 'minor', flags: [], confidence: 0.58,
    file: 'src/Refunds/Money.cs', lines: '9', anchor: 'return minor / 100.0m;',
    title: 'Hard-coded 100 assumes two-decimal currencies (JPY has none)',
    body: 'Below this round\'s bar (round 3: major, ≥ 0.80). Stored, not posted.',
    status: 'open', round: 3, inline: false, reviewer: 'general · sonnet', belowBar: true, checkPrecision: 0.44,
    thread: [], hunk: [{ n: 9, t: '+', code: '        return minor / 100.0m;' }],
  },
]

export interface RoundPolicy {
  round: number
  minSeverity: Severity
  minConfidence: number
  note: string
}

export const ROUND_POLICY: RoundPolicy[] = [
  { round: 1, minSeverity: 'nit', minConfidence: 0.5, note: 'max 5 nits, summary only' },
  { round: 2, minSeverity: 'minor', minConfidence: 0.65, note: '' },
  { round: 3, minSeverity: 'major', minConfidence: 0.8, note: 'and every later round' },
]

export const BYPASS: Flag[] = ['security', 'data-loss']

export interface RoundSummary {
  round: number
  sha: string
  kind: 'bot' | 'steer' | 'human'
  posted: number
  belowBar: number
  started: string
  by?: string
  steer?: string
}

export const PR612_ROUNDS: RoundSummary[] = [
  { round: 1, sha: '9a04e71', kind: 'bot', posted: 4, belowBar: 3, started: 'Thu 13:48' },
  { round: 2, sha: '7c1e9a2', kind: 'bot', posted: 4, belowBar: 2, started: 'Fri 09:02' },
  { round: 3, sha: '3b81f0d', kind: 'bot', posted: 1, belowBar: 4, started: 'Fri 15:40' },
]

// ---------------------------------------------------------------- checks catalogue

export type CheckStatus = 'candidate' | 'shadow' | 'active' | 'retired'
export interface Check {
  id: string
  title: string
  category: string
  severity: Severity
  flags: Flag[]
  status: CheckStatus
  requiresHuman?: boolean
  selected30d: number
  posted30d: number
  accepted: number
  dismissed: number
  ignored: number
  precision: number
  lastHit: string
  provenance: string
  humansWhy: string
  topDismissReason?: DismissReason
  byRepo: Partial<Record<Repo, number>>
  shadowEval?: string
}

export const CHECKS: Check[] = [
  { id: 'CHK-0311', title: 'Dedupe keys must include the tenant', category: 'security', severity: 'blocker', flags: ['security'], status: 'active', requiresHuman: true, selected30d: 41, posted30d: 9, accepted: 8, dismissed: 1, ignored: 0, precision: 0.89, lastHit: '2h ago', provenance: 'INC-1873', humansWhy: 'A shared idempotency cache returned one merchant\'s refund confirmation to another.', byRepo: { 'payments-api': 0.9, antiphon: 0.8 } },
  { id: 'CHK-0142', title: 'Lock released before async save completes', category: 'concurrency', severity: 'major', flags: [], status: 'active', selected30d: 118, posted30d: 23, accepted: 17, dismissed: 5, ignored: 1, precision: 0.74, lastHit: '18m ago', provenance: 'CARD-1234 + 2 why-missed docs', humansWhy: 'A lock guarding a read-modify-write must cover the save.', topDismissReason: 'by-design', byRepo: { 'payments-api': 0.81, antiphon: 0.69 } },
  { id: 'CHK-0087', title: 'Destructive column change without backfill', category: 'migration', severity: 'major', flags: ['data-loss'], status: 'active', requiresHuman: true, selected30d: 22, posted30d: 6, accepted: 5, dismissed: 1, ignored: 0, precision: 0.79, lastHit: 'Thu', provenance: 'postmortem PM-0412', humansWhy: 'Destructive migrations cannot be rolled back with the release.', byRepo: { 'payments-api': 0.83 } },
  { id: 'CHK-0402', title: 'Dedupe TTL shorter than the retry window', category: 'reliability', severity: 'major', flags: [], status: 'active', selected30d: 14, posted30d: 3, accepted: 3, dismissed: 0, ignored: 0, precision: 0.91, lastHit: 'Fri', provenance: 'seed: payments-api PR review history', humansWhy: 'Late retries become duplicates.', byRepo: { 'payments-api': 0.91 } },
  { id: 'CHK-0219', title: 'Public contract change without a version bump', category: 'api', severity: 'minor', flags: [], status: 'active', selected30d: 63, posted30d: 19, accepted: 12, dismissed: 6, ignored: 1, precision: 0.68, lastHit: 'Fri', provenance: '2 partner SDK breakages (2025)', humansWhy: 'Consumers generate clients from openapi.yaml.', topDismissReason: 'out-of-scope', byRepo: { 'payments-api': 0.72, 'web-portal': 0.55 } },
  { id: 'CHK-0033', title: 'Use TimeProvider, not DateTime.UtcNow', category: 'testability', severity: 'nit', flags: [], status: 'active', selected30d: 204, posted30d: 61, accepted: 27, dismissed: 22, ignored: 12, precision: 0.52, lastHit: '1h ago', provenance: 'docs/testing-and-build.md', humansWhy: 'Faked time keeps tests deterministic.', topDismissReason: 'style-preference', byRepo: { antiphon: 0.71, 'payments-api': 0.38 } },
  { id: 'CHK-0510', title: 'Magic numbers in money arithmetic', category: 'correctness', severity: 'minor', flags: [], status: 'active', selected30d: 77, posted30d: 25, accepted: 7, dismissed: 14, ignored: 4, precision: 0.28, lastHit: '18m ago', provenance: 'seed: why-missed-2025-11-03', humansWhy: 'Currencies differ in minor units.', topDismissReason: 'false-positive', byRepo: { 'payments-api': 0.31, 'web-portal': 0.2 } },
  { id: 'CHK-0618', title: 'React effect missing cleanup for subscription', category: 'frontend', severity: 'minor', flags: [], status: 'active', selected30d: 96, posted30d: 31, accepted: 9, dismissed: 17, ignored: 5, precision: 0.29, lastHit: '2h ago', provenance: 'web-portal postmortem PM-0388', humansWhy: 'Leaked subscriptions caused duplicate toasts.', topDismissReason: 'false-positive', byRepo: { 'web-portal': 0.29 } },
  { id: 'CHK-0702', title: 'Session runner: release must kill, pool or hand off', category: 'session-runtime', severity: 'major', flags: [], status: 'active', requiresHuman: true, selected30d: 37, posted30d: 4, accepted: 4, dismissed: 0, ignored: 0, precision: 1, lastHit: 'Wed', provenance: 'AGENTS.md sessions rule', humansWhy: 'Orphaned processes hold seats and quota.', byRepo: { antiphon: 1 } },
  { id: 'CHK-0733', title: 'Cart state persisted without schema version', category: 'persistence', severity: 'blocker', flags: ['data-loss'], status: 'shadow', selected30d: 12, posted30d: 0, accepted: 0, dismissed: 0, ignored: 0, precision: 0.83, lastHit: '2h ago', provenance: 'seed: web-portal incident INC-2210', humansWhy: 'Unversioned client storage cannot be migrated.', byRepo: { 'web-portal': 0.83 }, shadowEval: '10/12 agreed by reviewers · replay 4/5' },
  { id: 'CHK-0741', title: 'Hangfire job enqueued inside a DB transaction', category: 'concurrency', severity: 'major', flags: [], status: 'shadow', selected30d: 19, posted30d: 0, accepted: 0, dismissed: 0, ignored: 0, precision: 0.61, lastHit: 'Thu', provenance: 'seed: git log --grep "enqueue after commit"', humansWhy: 'The job can run before the commit is visible.', byRepo: { antiphon: 0.61 }, shadowEval: '11/18 agreed · replay 2/3' },
  { id: 'CHK-0745', title: 'Steer: "check the refund ledger reconciliation path"', category: 'payments', severity: 'major', flags: [], status: 'candidate', selected30d: 0, posted30d: 0, accepted: 0, dismissed: 0, ignored: 0, precision: 0, lastHit: '—', provenance: '3 recurring steers (Hugo Martin, Chen Wei)', humansWhy: 'Recurring human steer promoted to a candidate check.', byRepo: {} },
  { id: 'CHK-0122', title: 'Prefer records over classes for DTOs', category: 'style', severity: 'nit', flags: [], status: 'retired', selected30d: 0, posted30d: 0, accepted: 31, dismissed: 88, ignored: 40, precision: 0.2, lastHit: 'Sep', provenance: 'common layer v1', humansWhy: 'Retired: 74% dismissed as style preference.', topDismissReason: 'style-preference', byRepo: {} },
]

// ---------------------------------------------------------------- queues

export type QueueKind = 'review-gate' | 'approval-gate' | 'escalated-dispute' | 'blocker-second' | 'prompt-merge' | 'seed-candidate' | 'learn-proposal'
export const QUEUE_KIND_META: Record<QueueKind, { label: string; color: string }> = {
  'review-gate': { label: 'Review gate', color: 'pink' },
  'approval-gate': { label: 'Approval gate', color: 'success' },
  'escalated-dispute': { label: 'Escalated dispute', color: 'danger' },
  'blocker-second': { label: 'Blocker: second reviewer', color: 'orange' },
  'prompt-merge': { label: 'Prompt merge', color: 'indigo' },
  'seed-candidate': { label: 'Seed candidate', color: 'teal' },
  'learn-proposal': { label: 'Learn proposal', color: 'cyan' },
}

export type QueueState = 'unassigned' | 'assigned' | 'in-progress' | 'passed-back' | 'done'

export interface QueueItem {
  id: string
  kind: QueueKind
  title: string
  subject: string
  repo: Repo
  team: TeamKey
  author: string
  authorResolution?: { status: 'resolved' | 'unknown'; to?: string; via: string; confidence?: number; signals: { name: string; ok: boolean | null; detail: string }[] }
  assignee?: string
  state: QueueState
  ageWh: number
  slaWh: number
  why: string[]
  passedBy?: { login: string; reason: string }[]
  routing: string
  /** CODEOWNERS owners of the changed paths: one or more teams and/or users */
  owners: Owner[]
  blast?: Blast
  openFindings?: number
  steerHint?: string
}

export const QUEUE: QueueItem[] = [
  {
    id: 'Q-5512', kind: 'review-gate', title: 'Refunds: idempotency keys and partial refunds', subject: 'payments-api#612 · round 3', repo: 'payments-api', team: 'payments',
    author: 'asmith', assignee: 'hmartin', state: 'in-progress', ageWh: 3.6, slaWh: 4, openFindings: 6, blast: PR612.blast,
    why: ['Bot-only rounds 3/3 → human-review gate', 'external_contract 0.91 ≥ 0.6', 'category: public-api'],
    routing: 'CODEOWNERS /src/Refunds/** @lumen/payments-core @lumen/platform-sre (612 of 774 changed lines). Gate goes to payments-core (most changed lines). Random pick from 4 opted-in members with trust ≥ 30, weighted by open load; author Alice Smith excluded.',
    owners: CODE_OWNERS['payments-api:/src/Refunds/**'],
  },
  {
    id: 'Q-5523', kind: 'blocker-second', title: 'Second reviewer: "Not a blocker" on F1 (idempotency key not scoped)', subject: 'payments-api#612 · F1 · blocker', repo: 'payments-api', team: 'payments',
    author: 'asmith', state: 'unassigned', ageWh: 0.3, slaWh: 4, openFindings: 6,
    why: ['Kenji Sato marked F1 "not a blocker" with Trust 41 · payments-api', 'clear-blocker threshold 60: below it a second trusted reviewer must agree', 'Bot approval stays blocked until then'],
    routing: 'Routed to owners with trust ≥ 60 on payments-api: Hugo Martin (91), Alice Smith (82, author: excluded), Chen Wei (64). Random pick weighted by open load.',
    owners: CODE_OWNERS['payments-api:/src/Refunds/**'],
  },
  {
    id: 'Q-5518', kind: 'approval-gate', title: 'Drop legacy settlement_v1 table', subject: 'payments-api#609 · clean round 2', repo: 'payments-api', team: 'payments',
    author: 'ksato', state: 'passed-back', ageWh: 5.6, slaWh: 4, blast: RUNS.find((r) => r.number === 609)!.blast,
    why: ['category: migration', 'reversal_after_months 5 ≥ 4', 'destructive migration, no Down step'],
    passedBy: [{ login: 'cwei', reason: 'Not familiar with settlement — Hugo or Kenji wrote it' }],
    routing: 'CODEOWNERS /db/** @lumen/payments-core. Chen Wei passed; excluded from the next pick with Kenji Sato (author).',
    owners: CODE_OWNERS['payments-api:/db/**'],
  },
  {
    id: 'Q-5520', kind: 'escalated-dispute', title: 'Dispute on F3 "Over-refund race" — adjudicator held twice', subject: 'payments-api#618 · F3', repo: 'payments-api', team: 'payments',
    author: 'cwei', state: 'unassigned', ageWh: 1.2, slaWh: 4,
    why: ['Author disputed twice; adjudicator (luna) held both times', 'rebuttal_strength 0.62 (Jev)'],
    routing: 'Escalated by the comment workflow. Goes to the owning team, not the author.',
    owners: CODE_OWNERS['payments-api:/src/Ledger/**'],
  },
  {
    id: 'Q-5521', kind: 'review-gate', title: 'Teams: sync org/team membership from GitHub daily', subject: 'antiphon#1846 · round 1 findings', repo: 'antiphon', team: 'platform',
    author: 'antiphon-delegate', state: 'unassigned', ageWh: 2.4, slaWh: 8, openFindings: 2, blast: RUNS.find((r) => r.number === 1846)!.blast,
    authorResolution: {
      status: 'unknown', via: 'No signal resolved the author', signals: [
        { name: 'Co-authored-by trailers', ok: false, detail: 'none on 6 commits' },
        { name: 'Commit authors', ok: false, detail: 'all antiphon-delegate' },
        { name: 'Linked card assignee', ok: false, detail: 'CARD-1873 has no assignee' },
        { name: 'Antiphon task requester', ok: false, detail: 'scheduled action (keep-six), no human requester' },
        { name: 'Jev choice (3 candidates)', ok: null, detail: 'confidence 0.31 < 0.60 → escalated' },
        { name: 'Agent (haiku seat)', ok: false, detail: 'inconclusive: "two people discussed it on CARD-1873"' },
      ],
    },
    why: ['Findings need an author; author unknown → team queue'],
    routing: 'Tagged "author unknown". Anyone in platform-runtime can claim authorship or set it.',
    owners: CODE_OWNERS['antiphon:/server/Teams/**'],
  },
  {
    id: 'Q-5509', kind: 'approval-gate', title: 'Review executor: lease-based node scheduling', subject: 'antiphon#1842 · pre-assigned (large)', repo: 'antiphon', team: 'platform',
    author: 'bokafor', assignee: 'grao', state: 'assigned', ageWh: 0.8, slaWh: 8, blast: RUNS.find((r) => r.number === 1842)!.blast,
    why: ['size: large (1,380 lines)', 'reversal_after_months 4 ≥ 4'],
    routing: 'CODEOWNERS /server/Application/Review/** @lumen/platform-runtime. Pre-assigned while reviewers run so the human is ready.',
    owners: CODE_OWNERS['antiphon:/server/Application/Review/**'],
  },
  {
    id: 'Q-5502', kind: 'review-gate', title: 'Hotfix: correct VAT rounding on invoices', subject: 'payments-api#617 · round 1', repo: 'payments-api', team: 'payments',
    author: 'release-train', state: 'assigned', assignee: 'asmith', ageWh: 6.1, slaWh: 4, openFindings: 1,
    authorResolution: {
      status: 'resolved', to: 'hmartin', via: 'Resolved by AI from Co-authored-by trailer', confidence: 0.94, signals: [
        { name: 'Co-authored-by trailers', ok: true, detail: 'Hugo Martin <hugo.martin@lumen.dev> on 2/2 commits' },
        { name: 'Commit authors', ok: false, detail: 'release-train' },
        { name: 'Branch name', ok: false, detail: 'hotfix/vat-rounding' },
        { name: 'Jev choice', ok: true, detail: 'hmartin p=0.94' },
      ],
    },
    why: ['Hotfix label → human required', 'Findings routed to resolved author Hugo Martin'],
    routing: 'CODEOWNERS /src/Invoicing/** @lumen/payments-core. Random pick: Alice Smith (load 2); excluded Hugo Martin (resolved author).',
    owners: CODE_OWNERS['payments-api:/src/Invoicing/**'],
  },
  {
    id: 'Q-5497', kind: 'learn-proposal', title: 'Suppress CHK-0510 for src/Ledger/** ("amounts are always minor units here")', subject: 'payments-api · PR #623 to .antiphon/review/', repo: 'payments-api', team: 'payments',
    author: 'antiphon-delegate', state: 'unassigned', ageWh: 9.5, slaWh: 16,
    why: ['7 dismissals as false positive, 3 people, 30 days', 'scope: this path'],
    routing: 'Learn proposals go to repo maintainers in the owning team.',
    owners: CODE_OWNERS['payments-api:/src/Ledger/**'],
  },
  {
    id: 'Q-5490', kind: 'prompt-merge', title: 'Merge common@v14 into payments-core layer (2 conflicts)', subject: '.antiphon/review/sources.lock', repo: 'payments-api', team: 'payments',
    author: 'antiphon-delegate', state: 'unassigned', ageWh: 12.0, slaWh: 16,
    why: ['Upstream hash changed: common/checks/api/CHK-0219.md', 'Agent merge produced resolved/checks/api/CHK-0219.md'],
    routing: 'Prompt-merge approvals go to the team that owns the layer.',
    owners: [{ kind: 'team', name: 'payments-core', members: 5 }],
  },
  {
    id: 'Q-5488', kind: 'seed-candidate', title: '4 candidate checks from docs/investigations (cursor 2026-11-14)', subject: 'seed run SR-0091', repo: 'antiphon', team: 'platform',
    author: 'antiphon-delegate', assignee: 'elindqvist', state: 'in-progress', ageWh: 4.5, slaWh: 16,
    why: ['Seed workflow: why-missed docs since last cursor'],
    routing: 'Seed candidates go to the target layer owner.',
    owners: CODE_OWNERS['antiphon:/docs/investigations/**'],
  },
  {
    id: 'Q-5485', kind: 'approval-gate', title: 'Checkout: persist cart across sessions', subject: 'web-portal#2284 · waiting', repo: 'web-portal', team: 'web',
    author: 'icosta', assignee: 'druiz', state: 'assigned', ageWh: 11.2, slaWh: 8, openFindings: 4,
    why: ['data p=0.88', 'exposure 5 · Critical path'],
    routing: 'CODEOWNERS /src/checkout/** @lumen/web-experience.',
    owners: CODE_OWNERS['web-portal:/src/checkout/**'],
  },
]

// ---------------------------------------------------------------- reviewer stats

export interface ReviewerStat {
  login: string
  gatesDecided: number
  sentBack: number
  steers: number
  suggestionsMerged: number
  humanIssuesConfirmed: number
  learningsMerged: number
  learningsPromoted: number
  disputesWon: number
  disputesLost: number
  wrongDismissals: number
  p50Wh: number
  p90Wh: number
  passes: number
  slaBreaches: number
  comments: number
  words: number
  botAnswerUp: number
  botAnswerDown: number
  rubberStamps: number
}

export const REVIEWER_STATS: ReviewerStat[] = [
  { login: 'hmartin', gatesDecided: 19, sentBack: 6, steers: 4, suggestionsMerged: 14, humanIssuesConfirmed: 5, learningsMerged: 3, learningsPromoted: 1, disputesWon: 4, disputesLost: 1, wrongDismissals: 0, p50Wh: 1.1, p90Wh: 3.4, passes: 1, slaBreaches: 1, comments: 88, words: 6120, botAnswerUp: 9, botAnswerDown: 2, rubberStamps: 0 },
  { login: 'asmith', gatesDecided: 14, sentBack: 3, steers: 1, suggestionsMerged: 11, humanIssuesConfirmed: 3, learningsMerged: 2, learningsPromoted: 0, disputesWon: 2, disputesLost: 2, wrongDismissals: 0, p50Wh: 0.8, p90Wh: 2.2, passes: 0, slaBreaches: 0, comments: 61, words: 3880, botAnswerUp: 5, botAnswerDown: 0, rubberStamps: 0 },
  { login: 'ksato', gatesDecided: 22, sentBack: 1, steers: 0, suggestionsMerged: 4, humanIssuesConfirmed: 0, learningsMerged: 0, learningsPromoted: 0, disputesWon: 0, disputesLost: 0, wrongDismissals: 1, p50Wh: 0.2, p90Wh: 0.6, passes: 0, slaBreaches: 0, comments: 23, words: 410, botAnswerUp: 1, botAnswerDown: 0, rubberStamps: 3 },
  { login: 'cwei', gatesDecided: 9, sentBack: 4, steers: 2, suggestionsMerged: 8, humanIssuesConfirmed: 4, learningsMerged: 1, learningsPromoted: 0, disputesWon: 3, disputesLost: 3, wrongDismissals: 1, p50Wh: 2.4, p90Wh: 6.8, passes: 3, slaBreaches: 2, comments: 140, words: 12400, botAnswerUp: 3, botAnswerDown: 4, rubberStamps: 0 },
  { login: 'lnovak', gatesDecided: 2, sentBack: 0, steers: 0, suggestionsMerged: 1, humanIssuesConfirmed: 1, learningsMerged: 0, learningsPromoted: 0, disputesWon: 0, disputesLost: 0, wrongDismissals: 0, p50Wh: 1.5, p90Wh: 1.9, passes: 0, slaBreaches: 0, comments: 7, words: 520, botAnswerUp: 0, botAnswerDown: 0, rubberStamps: 0 },
]

export type WeightKey = 'suggestionsMerged' | 'humanIssuesConfirmed' | 'learningsMerged' | 'learningsPromoted' | 'disputesWon' | 'disputesLost' | 'wrongDismissals'

/** Impact-score weights: instance defaults plus per-team overrides. Stored in Antiphon (database rows), not in a repo. */
export const IMPACT_WEIGHTS: { key: WeightKey; label: string; help: string; instance: number; team: Partial<Record<TeamKey, number>> }[] = [
  { key: 'suggestionsMerged', label: 'Suggestion merged', help: 'A reviewer suggestion the author merged as written', instance: 2, team: {} },
  { key: 'humanIssuesConfirmed', label: 'Human-raised issue confirmed', help: 'An issue the human raised that a fix or the verifier confirmed', instance: 5, team: { payments: 6 } },
  { key: 'learningsMerged', label: 'Learning merged', help: 'A learn PR (suppression, context note, check) merged into a layer', instance: 4, team: {} },
  { key: 'learningsPromoted', label: 'Learning promoted', help: 'A learning promoted to the team or common layer', instance: 6, team: {} },
  { key: 'disputesWon', label: 'Dispute won', help: 'A dispute the adjudicator or gate conceded', instance: 2, team: {} },
  { key: 'disputesLost', label: 'Dispute lost', help: 'A dispute held against the human', instance: -1, team: { platform: -2 } },
  { key: 'wrongDismissals', label: 'Dismissal linked to a bug', help: 'A dismissal later linked to an incident by a why-missed investigation', instance: -8, team: { payments: -10 } },
]

export function weightFor(key: WeightKey, teamKey?: TeamKey): { value: number; source: 'instance' | 'team' } {
  const w = IMPACT_WEIGHTS.find((x) => x.key === key)!
  const o = teamKey ? w.team[teamKey] : undefined
  return o === undefined ? { value: w.instance, source: 'instance' } : { value: o, source: 'team' }
}

export const IMPACT_WEIGHT_REVISIONS: SettingRevision[] = [
  { rev: 9, at: '2026-11-12 16:05', by: 'hmartin', layer: 'payments', change: 'wrongDismissals −8 → −10 (team override)', reason: 'INC-2291 follow-up: dismissals must cost more than they save' },
  { rev: 8, at: '2026-11-04 09:20', by: 'hmartin', layer: 'payments', change: 'humanIssuesConfirmed 5 → 6 (team override)' },
  { rev: 7, at: '2026-10-28 14:00', by: 'grao', layer: 'platform', change: 'disputesLost −1 → −2 (team override)' },
  { rev: 6, at: '2026-10-01 10:00', by: 'grao', layer: 'instance', change: 'learningsPromoted 5 → 6', reason: 'Promotion is rarer and worth more than a local learning' },
  { rev: 5, at: '2026-09-15 11:45', by: 'grao', layer: 'instance', change: 'initial weights imported from design.md §18' },
]

/** Outcome (impact) score: what the leaderboard ranks, using the team's effective weights. Volume (comments, words, gates) never scores on its own. */
export function outcomeScore(s: ReviewerStat, teamKey: TeamKey = 'payments'): number {
  return IMPACT_WEIGHTS.reduce((a, w) => a + s[w.key] * weightFor(w.key, teamKey).value, 0)
}

export interface DailyStat {
  date: string
  dow: string
  gatesOpened: number
  gatesClosed: number
  backlog: number
  p50Wh: number
  accepted: number
  dismissed: number
  costApi: number
  costSeatEq: number
}

export const DAILY: DailyStat[] = [
  { date: '2026-11-08', dow: 'Su', gatesOpened: 0, gatesClosed: 0, backlog: 3, p50Wh: 0, accepted: 0, dismissed: 0, costApi: 0.01, costSeatEq: 0.4 },
  { date: '2026-11-09', dow: 'Mo', gatesOpened: 11, gatesClosed: 9, backlog: 5, p50Wh: 1.4, accepted: 14, dismissed: 5, costApi: 0.38, costSeatEq: 52.1 },
  { date: '2026-11-10', dow: 'Tu', gatesOpened: 9, gatesClosed: 10, backlog: 4, p50Wh: 1.1, accepted: 11, dismissed: 3, costApi: 0.33, costSeatEq: 47.9 },
  { date: '2026-11-11', dow: 'We', gatesOpened: 14, gatesClosed: 11, backlog: 7, p50Wh: 1.9, accepted: 19, dismissed: 8, costApi: 0.51, costSeatEq: 71.3 },
  { date: '2026-11-12', dow: 'Th', gatesOpened: 10, gatesClosed: 12, backlog: 5, p50Wh: 1.6, accepted: 13, dismissed: 4, costApi: 0.42, costSeatEq: 58.0 },
  { date: '2026-11-13', dow: 'Fr', gatesOpened: 8, gatesClosed: 9, backlog: 4, p50Wh: 1.2, accepted: 9, dismissed: 6, costApi: 0.29, costSeatEq: 40.6 },
  { date: '2026-11-14', dow: 'Sa', gatesOpened: 1, gatesClosed: 0, backlog: 5, p50Wh: 0, accepted: 1, dismissed: 0, costApi: 0.02, costSeatEq: 2.2 },
  { date: '2026-11-15', dow: 'Su', gatesOpened: 0, gatesClosed: 1, backlog: 4, p50Wh: 0.4, accepted: 0, dismissed: 0, costApi: 0.0, costSeatEq: 0.3 },
  { date: '2026-11-16', dow: 'Mo', gatesOpened: 13, gatesClosed: 10, backlog: 7, p50Wh: 1.7, accepted: 17, dismissed: 7, costApi: 0.47, costSeatEq: 66.4 },
  { date: '2026-11-17', dow: 'Tu', gatesOpened: 12, gatesClosed: 13, backlog: 6, p50Wh: 1.3, accepted: 16, dismissed: 4, costApi: 0.44, costSeatEq: 61.2 },
  { date: '2026-11-18', dow: 'We', gatesOpened: 15, gatesClosed: 12, backlog: 9, p50Wh: 2.2, accepted: 21, dismissed: 9, costApi: 0.58, costSeatEq: 80.7 },
  { date: '2026-11-19', dow: 'Th', gatesOpened: 11, gatesClosed: 13, backlog: 7, p50Wh: 1.5, accepted: 15, dismissed: 5, costApi: 0.4, costSeatEq: 57.5 },
  { date: '2026-11-20', dow: 'Fr', gatesOpened: 9, gatesClosed: 8, backlog: 8, p50Wh: 1.8, accepted: 12, dismissed: 6, costApi: 0.37, costSeatEq: 49.9 },
  { date: '2026-11-21', dow: 'Sa', gatesOpened: 2, gatesClosed: 1, backlog: 9, p50Wh: 0.9, accepted: 3, dismissed: 1, costApi: 0.41, costSeatEq: 67.25 },
]

// ---------------------------------------------------------------- context pack (payments-api#618, round 3)

export type PackSource = 'diff' | 'impact-index' | 'callers' | 'co-change' | 'di' | 'check-hint' | 'related.yaml' | 'model' | 'tool'
export const PACK_SOURCE_META: Record<PackSource, { label: string; color: string; hitRate: number }> = {
  diff: { label: 'diff', color: 'active', hitRate: 0.97 },
  'impact-index': { label: 'impact index', color: 'cyan', hitRate: 0.41 },
  callers: { label: 'callers', color: 'teal', hitRate: 0.58 },
  'co-change': { label: 'git co-change', color: 'lime', hitRate: 0.22 },
  di: { label: 'DI/registration', color: 'yellow', hitRate: 0.35 },
  'check-hint': { label: 'check relatedFiles', color: 'grape', hitRate: 0.49 },
  'related.yaml': { label: 'related.yaml rule', color: 'pink', hitRate: 0.63 },
  model: { label: 'model suggestion', color: 'orange', hitRate: 0.18 },
  tool: { label: 'tool output', color: 'gray', hitRate: 0.3 },
}

export interface PackFile {
  path: string
  source: PackSource
  reason: string
  score: number
  tokens: number
  included: boolean
  mode: 'whole' | 'hunks+members' | 'outline' | 'output'
  cited?: number
  opened?: boolean
}

export interface PackReviewer {
  group: string
  model: ModelKey
  checks: string[]
  suffixTokens: number
  status: 'done' | 'running' | 'queued'
  cacheHit: number
}

export const PACK_REVIEWERS: PackReviewer[] = [
  { group: 'security', model: 'opus', checks: ['CHK-0311', 'CHK-0402'], suffixTokens: 4_100, status: 'done', cacheHit: 0.94 },
  { group: 'concurrency', model: 'opus', checks: ['CHK-0142', 'CHK-0741'], suffixTokens: 5_300, status: 'running', cacheHit: 0.94 },
  { group: 'data', model: 'sonnet', checks: ['CHK-0087', 'CHK-0510', 'CHK-0745'], suffixTokens: 6_800, status: 'done', cacheHit: 0.93 },
  { group: 'general', model: 'sonnet', checks: ['CHK-0033', 'CHK-0219', 'CHK-0122'], suffixTokens: 3_900, status: 'done', cacheHit: 0.95 },
]

export const PACK_BUDGET = { prefixBudget: 60_000, prefixUsed: 51_700 }

export const PACK_FILES: PackFile[] = [
  { path: 'src/Ledger/RefundReconciler.cs', source: 'diff', reason: 'changed (+212 −0), whole file < 600 lines', score: 1, tokens: 3_900, included: true, mode: 'whole', cited: 3, opened: true },
  { path: 'src/Ledger/LedgerEntry.cs', source: 'diff', reason: 'changed (+41 −9), whole file', score: 1, tokens: 1_200, included: true, mode: 'whole', cited: 1 },
  { path: 'src/Refunds/RefundService.cs', source: 'diff', reason: 'changed (+38 −12), 1,140 lines → hunks widened to enclosing members + outline', score: 1, tokens: 4_600, included: true, mode: 'hunks+members', cited: 2, opened: true },
  { path: 'db/migrations/20261120_ledger_recon.sql', source: 'diff', reason: 'changed (+33), migration', score: 1, tokens: 600, included: true, mode: 'whole', cited: 1 },
  { path: 'src/Jobs/NightlyJobs.cs', source: 'diff', reason: 'changed (+6 −1)', score: 1, tokens: 900, included: true, mode: 'hunks+members' },
  { path: 'src/Ledger/LedgerDbContext.cs', source: 'check-hint', reason: 'CHK-0142 relatedFiles: **/DbContext*.cs', score: 0.88, tokens: 2_100, included: true, mode: 'whole', cited: 1 },
  { path: 'src/Refunds/RefundLock.cs', source: 'callers', reason: 'AcquireAsync called from changed RefundReconciler.RunAsync', score: 0.84, tokens: 700, included: true, mode: 'whole', cited: 1 },
  { path: 'src/Psp/PspRefundResponse.cs', source: 'callers', reason: 'SettledAmount read by changed LedgerEntry.FromPsp', score: 0.8, tokens: 500, included: true, mode: 'whole' },
  { path: 'src/Program.Jobs.cs', source: 'di', reason: 'registers RefundReconciler as a recurring job', score: 0.71, tokens: 800, included: true, mode: 'hunks+members' },
  { path: 'tests/Ledger.Tests/RefundReconcilerTests.cs', source: 'impact-index', reason: 'impact index: RefundReconciler → RefundReconcilerTests (2 edges)', score: 0.69, tokens: 3_300, included: true, mode: 'whole' },
  { path: 'src/Ledger/README.md', source: 'related.yaml', reason: 'related.yaml: "src/Ledger/** → src/Ledger/README.md (ledger invariants)"', score: 0.66, tokens: 1_400, included: true, mode: 'whole', cited: 1 },
  { path: 'src/Refunds/IdempotencyStore.cs', source: 'co-change', reason: 'changed together in 7 of last 20 commits touching RefundService.cs', score: 0.52, tokens: 1_100, included: true, mode: 'whole' },
  { path: 'analyzers: dotnet build warnings (3)', source: 'tool', reason: 'CA2007 ×2, CS8602 ×1 on changed lines', score: 0.5, tokens: 300, included: true, mode: 'output' },
  { path: 'src/Ledger/Exports/SettlementCsv.cs', source: 'model', reason: 'Jev suggestion: "reconciliation output is exported nightly" (p=0.58)', score: 0.41, tokens: 1_900, included: true, mode: 'whole' },
  { path: 'src/Payments/PaymentAggregate.cs', source: 'impact-index', reason: 'impact index: 1 edge via LedgerEntry', score: 0.38, tokens: 6_200, included: false, mode: 'whole' },
  { path: 'src/Refunds/RefundController.cs', source: 'co-change', reason: 'changed together in 3 of last 20 commits', score: 0.31, tokens: 2_400, included: false, mode: 'whole' },
  { path: 'docs/adr/0007-ledger-double-entry.md', source: 'model', reason: 'Jev suggestion (p=0.34)', score: 0.27, tokens: 2_800, included: false, mode: 'whole' },
]

export const PACK_OPENED_NOT_IN_PACK: { kind: 'opened' | 'missing'; path: string; reason: string; by: string; suggestion: string }[] = [
  { kind: 'opened', path: 'src/Payments/PaymentAggregate.cs', reason: 'needed RemainingRefundable() to judge the balance check', by: 'concurrency · opus', suggestion: 'Raise impact-index score for aggregate roots, or add related.yaml rule src/Ledger/** → src/Payments/PaymentAggregate.cs' },
  { kind: 'opened', path: 'src/Ledger/Exports/SettlementCsvWriter.cs', reason: 'followed SettlementCsv to its writer', by: 'data · sonnet', suggestion: 'Callers source should follow one more hop for writer/serializer pairs' },
  { kind: 'missing', path: 'docs/runbooks/ledger-recon.md', reason: 'wanted the on-call procedure for a failed nightly reconcile; file does not exist', by: 'data · sonnet', suggestion: 'Not a lookup miss: the repo has no runbook. Consider a learn note.' },
]

// ---------------------------------------------------------------- run detail (payments-api#618)

export type NodeKind = 'code' | 'decision' | 'completion' | 'agent' | 'fan-out' | 'join' | 'gate' | 'wait'
export type NodeStatus = 'done' | 'reused' | 'running' | 'waiting' | 'pending' | 'skipped' | 'failed'

export const NODE_KIND_META: Record<NodeKind, { label: string; color: string; blurb: string }> = {
  code: { label: 'code', color: 'gray', blurb: 'A registered C# step. No model.' },
  decision: { label: 'decision', color: 'cyan', blurb: 'Typed IDecisionModel questions over a chain.' },
  completion: { label: 'completion', color: 'teal', blurb: 'One ICompletionModel call returning ABF.' },
  agent: { label: 'agent', color: 'grape', blurb: 'An agent session on a runner seat.' },
  'fan-out': { label: 'fan-out', color: 'indigo', blurb: 'Runs a child per item.' },
  join: { label: 'join', color: 'indigo', blurb: 'Waits for every child; failure policy.' },
  gate: { label: 'gate', color: 'pink', blurb: 'Human decision; opens a queue item.' },
  wait: { label: 'wait', color: 'warning', blurb: 'External event: push, recheck, timer.' },
}

export const NODE_STATUS_META: Record<NodeStatus, { label: string; color: string }> = {
  done: { label: 'done', color: 'success' },
  reused: { label: 'reused', color: 'teal' },
  running: { label: 'running', color: 'active' },
  waiting: { label: 'parked', color: 'warning' },
  pending: { label: 'pending', color: 'gray' },
  skipped: { label: 'not reached', color: 'dark' },
  failed: { label: 'failed', color: 'danger' },
}

export interface NodeExec {
  iteration: number
  status: NodeStatus
  started: string
  durationS: number
  model?: ModelKey
  tokensIn?: number
  tokensOut?: number
  cost?: Cost
  note?: string
  output?: string
}

export interface WorkflowNodeDef {
  id: string
  kind: NodeKind
  run?: string
  detail?: string
  status: NodeStatus
  execs: NodeExec[]
}

const nx = (iteration: number, status: NodeStatus, started: string, durationS: number, extra: Partial<NodeExec> = {}): NodeExec => ({ iteration, status, started, durationS, ...extra })

export const RUN618_NODES: WorkflowNodeDef[] = [
  { id: 'intake', kind: 'code', run: 'Intake', status: 'done', execs: [nx(1, 'done', 'Wed 10:02', 14, { output: 'base 1f20c9e · head 8d3e012 · csharp, sql · areas: ledger, refunds · size L' }), nx(2, 'done', 'Thu 11:30', 6, { output: 'head 4a77b10' }), nx(3, 'done', 'Fri 16:05', 5, { output: 'head e41b07c · steer from Hugo Martin' })] },
  { id: 'delta', kind: 'code', run: 'Delta', status: 'done', execs: [nx(1, 'done', 'Wed 10:02', 2, { output: 'full (first iteration)' }), nx(2, 'done', 'Thu 11:30', 9, { output: '4 files changed since 8d3e012 · 3 findings re-anchored, 1 outdated' }), nx(3, 'done', 'Fri 16:05', 7, { output: '2 files changed since 4a77b10 · 2 open findings still present (Jev)' })] },
  { id: 'categorise', kind: 'decision', run: 'Categorise', detail: 'decisions-default', status: 'reused', execs: [nx(1, 'done', 'Wed 10:03', 2.1, { model: 'jev', tokensIn: 18_200, cost: { api: 0.0008, seatEq: 0 }, output: 'feature · data p=0.93 · reach 3 · rollback 4' }), nx(2, 'reused', 'Thu 11:30', 0, { note: 'inputs hash unchanged → reused from iteration 1' }), nx(3, 'done', 'Fri 16:05', 3.4, { model: 'jev', tokensIn: 19_900, cost: { api: 0.0008, seatEq: 0 }, note: '2 questions escalated Jev → luna (confidence < 0.60)', output: 'unchanged; concurrency p 0.48 → 0.71' })] },
  { id: 'select', kind: 'decision', run: 'SelectChecks', detail: '163 candidates → 11', status: 'done', execs: [nx(1, 'done', 'Wed 10:03', 4.2, { model: 'jev', tokensIn: 41_000, cost: { api: 0.0017, seatEq: 0 }, output: '171 candidates, 2 calls → 13 selected' }), nx(2, 'done', 'Thu 11:31', 2.9, { model: 'jev', tokensIn: 22_100, cost: { api: 0.0009, seatEq: 0 }, output: 'delta: 58 candidates → 6 (round 2 bar drops nits)' }), nx(3, 'done', 'Fri 16:05', 4.0, { model: 'jev', tokensIn: 40_300, cost: { api: 0.0017, seatEq: 0 }, output: '163 candidates → 11 (round 3 bar: major+; steer adds CHK-0745 candidate)' })] },
  { id: 'group', kind: 'code', run: 'GroupChecks', status: 'done', execs: [nx(1, 'done', 'Wed 10:03', 1, { output: '4 groups' }), nx(2, 'done', 'Thu 11:31', 1, { output: '2 groups' }), nx(3, 'done', 'Fri 16:05', 1, { output: '4 groups: security, concurrency, data, general' })] },
  { id: 'packs', kind: 'code', run: 'BuildPacks', status: 'done', execs: [nx(1, 'done', 'Wed 10:04', 21, { output: 'prefix 54.1k tokens · 18 files' }), nx(2, 'done', 'Thu 11:31', 9, { output: 'prefix 38.0k · reused 61% of chunks' }), nx(3, 'done', 'Fri 16:06', 12, { output: 'prefix 51.7k · 14 of 17 candidates included' })] },
  { id: 'review', kind: 'fan-out', run: 'over: groups', detail: 'policy: all', status: 'running', execs: [nx(1, 'done', 'Wed 10:04', 512), nx(2, 'done', 'Thu 11:31', 301), nx(3, 'running', 'Fri 16:06', 0, { note: '3/4 children done' })] },
  { id: 'reviewer:security', kind: 'agent', run: 'Reviewer', detail: 'security', status: 'done', execs: [nx(3, 'done', 'Fri 16:06', 244, { model: 'opus', tokensIn: 58_300, tokensOut: 3_100, cost: { api: 0, seatEq: 1.12 }, output: '0 findings · 2/2 checks pass' })] },
  { id: 'reviewer:concurrency', kind: 'agent', run: 'Reviewer', detail: 'concurrency', status: 'running', execs: [nx(3, 'running', 'Fri 16:06', 0, { model: 'opus', tokensIn: 61_900, tokensOut: 2_400, cost: { api: 0, seatEq: 1.08 }, note: 'running 4m 12s · opened 2 files beyond the pack' })] },
  { id: 'reviewer:data', kind: 'agent', run: 'Reviewer', detail: 'data', status: 'done', execs: [nx(3, 'done', 'Fri 16:06', 198, { model: 'sonnet', tokensIn: 59_200, tokensOut: 2_900, cost: { api: 0, seatEq: 0.29 }, output: '2 findings (1 major) · 3/3 checks accounted' })] },
  { id: 'reviewer:general', kind: 'agent', run: 'Reviewer', detail: 'general', status: 'done', execs: [nx(3, 'done', 'Fri 16:06', 161, { model: 'sonnet', tokensIn: 56_100, tokensOut: 2_200, cost: { api: 0, seatEq: 0.26 }, output: '1 finding (minor, below bar) · 3/3 checks' })] },
  { id: 'collate', kind: 'join', run: 'CollateFindings', detail: 'from: review', status: 'pending', execs: [nx(1, 'done', 'Wed 10:13', 3, { output: '9 → 7 after dedupe' }), nx(2, 'done', 'Thu 11:36', 2, { output: '4 → 4' })] },
  { id: 'verify', kind: 'completion', run: 'VerifyFindings', detail: 'completions-cheap', status: 'pending', execs: [nx(1, 'done', 'Wed 10:13', 41, { model: 'luna', tokensIn: 72_000, tokensOut: 4_100, cost: { api: 0, seatEq: 0.38 }, output: '1 disproved and dropped' }), nx(2, 'done', 'Thu 11:37', 29, { model: 'haiku', tokensIn: 40_000, tokensOut: 2_000, cost: { api: 0, seatEq: 0.07 }, note: 'luna seat over quota → haiku', output: '0 dropped' })] },
  { id: 'publish', kind: 'code', run: 'Publish', status: 'pending', execs: [nx(1, 'done', 'Wed 10:14', 4, { output: 'review with 4 inline + summary; status failure' }), nx(2, 'done', 'Thu 11:38', 3, { output: '2 inline; summary edited' })] },
  { id: 'route', kind: 'code', run: 'RouteAfterRound', status: 'pending', execs: [nx(1, 'done', 'Wed 10:14', 0.2, { output: 'findings' }), nx(2, 'done', 'Thu 11:38', 0.2, { output: 'clean' })] },
  { id: 'await-fixes', kind: 'wait', run: 'on: push, recheck, close, base-moved', detail: 'timeout 14d', status: 'pending', execs: [nx(1, 'done', 'Wed 10:14', 91_000, { output: 'push 4a77b10 after 19.5 wh', note: 'Base moved (3 commits) at 6.4 wh · drift recorded · branch not updated: waiting on author' })] },
  { id: 'base-drift', kind: 'decision', run: 'BaseDriftCheck', detail: 'decisions-default', status: 'pending', execs: [nx(1, 'done', 'Wed 16:40', 0.4, { output: 'base 1f20c9e..7b3e9d0 (3 commits) · prefilter: no overlap with PR files, related files or contract globs → unaffected-open (no model call)', note: 'Drift recorded on the run · branch not updated: waiting on author (the run stays parked on await-fixes)' })] },
  { id: 'needs-human-approval', kind: 'code', run: 'NeedsHumanApproval', status: 'pending', execs: [nx(2, 'done', 'Thu 11:38', 0.3, { output: 'yes: data p=0.93, rollback 4 (sticky)' })] },
  { id: 'bot-approve', kind: 'code', run: 'BotApprove', status: 'skipped', execs: [] },
  { id: 'human-approve', kind: 'gate', run: 'queue: owning-team', detail: 'approve · another-round · close', status: 'pending', execs: [nx(2, 'done', 'Thu 11:38', 98_000, { output: 'Hugo Martin → another-round', note: 'Steer: "check the refund ledger reconciliation path against the double-entry invariant in src/Ledger/README.md"' })] },
  { id: 'human-review', kind: 'gate', run: 'queue: owning-team', detail: 'approve · another-round · close', status: 'skipped', execs: [] },
  { id: 'base-moved', kind: 'code', run: 'CheckBaseMoved', status: 'skipped', execs: [] },
  { id: 'update-branch', kind: 'code', run: 'UpdateBranch', status: 'skipped', execs: [] },
  { id: 'await-merge', kind: 'wait', run: 'on: merge, close, push, base-moved', status: 'skipped', execs: [] },
]

/** One sentence per node: what it is for in the pr-review workflow (design.md §3.3, §4). */
export const NODE_PURPOSE: Record<string, string> = {
  intake: 'Pins base and head SHAs, fetches the mirror, reads policy and checks at the base SHA, and adds code-only tags (§4.1).',
  delta: 'On a recheck, narrows work to what changed since the last reviewed head and re-anchors open findings (§4.2). Full on the first iteration.',
  categorise: 'One batched Jev call: change kind, risk areas and the six blast-radius dimensions (§4.3).',
  select: "Prefilters the check catalogue, then asks Jev “applies?” per candidate; the round's posting bar drops checks that cannot clear it (§4.4).",
  group: 'Bin-packs the selected checks into reviewer groups by category and token budget; bypass categories get their own group (§4.5).',
  packs: "Builds one context file per reviewer: a byte-identical shared prefix (PR, diff, related files with ledger reasons) plus the group's check prompts (§4.6).",
  review: 'Fans out one reviewer per group on runner seats and waits for all of them (policy: all).',
  reviewer: 'A read-only agent session on a runner seat that reviews its pack and returns review-findings/1 (§4.7).',
  collate: "Joins the reviewers' outputs: collapses duplicates across groups, open findings and human comments, and re-resolves anchors at the reviewed SHA (§4.8).",
  verify: 'A cheaper model tries to disprove each finding; disproved ones are dropped and recorded (§4.8).',
  publish: "Posts findings that clear the round's bar as one PR review, edits the summary comment, and sets commit statuses (§4.9, §4.11).",
  route: 'Decides what happens after the round: findings → wait for changes, clean → needs-human-approval, bot-round limit → human review (§5).',
  'await-fixes': 'Parks the run, with no seat held, until the author pushes fixes or someone asks for a recheck. Times out after 14 days.',
  'base-drift':
    'When the base branch moves under the PR (while parked, or since an approval), or with keepUpToDate on the approved branch was already behind the tip: a code-only prefilter checks whether the new base commits touch the PR’s files, related files or contract globs; only on overlap does one cheap Jev call ask “could these changes affect this PR?”. Affected → delta re-review with an automatic rebase steer; unaffected and approved → update-branch with the approval carried forward.',
  'needs-human-approval': 'For a clean round, decides whether a human must approve, by applying the policy rules (risk categories, blast-radius thresholds, requiresHuman checks, size, first-time author, conceded disputes, unverified high-severity). The answer is sticky for the rest of the PR (§4.10).',
  'bot-approve': 'Sets antiphon/approval to success for the reviewed SHA when every auto-approve condition holds (§4.10).',
  'human-approve': 'Gate: a team member approves a clean round, sends it back for another round with a steer, or closes (§11).',
  'human-review': 'Gate opened after 3 bot-only rounds: a human approves, steers another round, or closes (§5).',
  'base-moved':
    'After approval (bot or human), checks in code whether the approved branch is behind the base tip. keepUpToDate on: compares the branch’s merge-base with the tip, so a branch already behind at review time is caught too; off: compares the reviewed base with the tip (drift only). Unchanged → await-merge with no update and no model call; moved → base-drift decides whether it affects the PR (§4.12).',
  'update-branch': 'When the base moved without affecting the approved PR (base-drift): brings the branch up to date by rebase or merge, set per repo, and carries the approval forward if patch ids are unchanged. With keepUpToDate off it only acts on conflicts. An agent resolves conflicts; only conflict hunks are rechecked.',
  'await-merge': 'Holds the approved PR until it’s merged or closed. Keeps the approval while only the base branch moves.',
  // optional pattern, only in the editor's "Re-review disputed findings" proposal (design.md §4.8)
  'disputed-findings':
    'Gate for findings the verifier disputes. The first dispute of a finding goes back to packs automatically (autoRetries 1) with the verifier’s objection in the pack; a further dispute opens this gate for the owning team: re-review, drop the finding, or keep it as is. Times out to drop.',
}

export function nodePurpose(id: string): string {
  if (id.startsWith('reviewer:')) return `${NODE_PURPOSE.reviewer} Group: ${id.slice('reviewer:'.length)}.`
  return NODE_PURPOSE[id] ?? ''
}

/** Why a node with no executions has none; default is "Not reached in this run yet." */
export const NODE_NOT_RUN_NOTE: Record<string, string> = {
  'bot-approve': 'Not taken in iteration 2 (needs-human-approval answered yes).',
  'base-moved': 'Not reached: the PR has not been approved yet.',
  'update-branch': 'Not reached: the PR has not been approved yet.',
  'await-merge': 'Not reached: the PR has not been approved yet.',
}

export interface WorkflowEdgeDef {
  id: string
  from: string
  to: string
  label?: string
  back?: { taken: number; max: number }
  taken: boolean
}

export const RUN618_EDGES: WorkflowEdgeDef[] = [
  { id: 'e1', from: 'intake', to: 'delta', taken: true },
  { id: 'e2', from: 'delta', to: 'categorise', taken: true },
  { id: 'e3', from: 'categorise', to: 'select', taken: true },
  { id: 'e4', from: 'select', to: 'group', taken: true },
  { id: 'e5', from: 'group', to: 'packs', taken: true },
  { id: 'e6', from: 'packs', to: 'review', taken: true },
  { id: 'e7a', from: 'review', to: 'reviewer:security', taken: true },
  { id: 'e7b', from: 'review', to: 'reviewer:concurrency', taken: true },
  { id: 'e7c', from: 'review', to: 'reviewer:data', taken: true },
  { id: 'e7d', from: 'review', to: 'reviewer:general', taken: true },
  { id: 'e8a', from: 'reviewer:security', to: 'collate', taken: true },
  { id: 'e8b', from: 'reviewer:concurrency', to: 'collate', taken: false },
  { id: 'e8c', from: 'reviewer:data', to: 'collate', taken: true },
  { id: 'e8d', from: 'reviewer:general', to: 'collate', taken: true },
  { id: 'e9', from: 'collate', to: 'verify', taken: false },
  { id: 'e10', from: 'verify', to: 'publish', taken: false },
  { id: 'e11', from: 'publish', to: 'route', taken: false },
  { id: 'e12', from: 'route', to: 'human-review', label: 'bot-round-limit', taken: false },
  { id: 'e13', from: 'route', to: 'await-fixes', label: 'findings', taken: false },
  { id: 'e14', from: 'route', to: 'needs-human-approval', label: 'clean', taken: false },
  { id: 'e15', from: 'needs-human-approval', to: 'bot-approve', label: 'no', taken: false },
  { id: 'e16', from: 'needs-human-approval', to: 'human-approve', label: 'yes', taken: false },
  { id: 'e17', from: 'bot-approve', to: 'base-moved', taken: false },
  { id: 'e18', from: 'human-approve', to: 'base-moved', label: 'approve', taken: false },
  { id: 'e19', from: 'human-review', to: 'base-moved', label: 'approve', taken: false },
  { id: 'e25', from: 'base-moved', to: 'await-merge', label: 'unchanged', taken: false },
  { id: 'e26', from: 'base-moved', to: 'base-drift', label: 'moved', taken: false },
  { id: 'e20', from: 'update-branch', to: 'await-merge', taken: false },
  { id: 'e21', from: 'await-fixes', to: 'base-drift', label: 'base-moved', taken: false },
  { id: 'e22', from: 'await-merge', to: 'base-drift', label: 'base-moved', taken: false },
  { id: 'e23', from: 'base-drift', to: 'update-branch', label: 'unaffected-approved', taken: false },
  { id: 'e24', from: 'base-drift', to: 'await-fixes', label: 'unaffected-open', taken: false },
  { id: 'b1', from: 'await-fixes', to: 'intake', label: 'push | recheck', back: { taken: 1, max: 20 }, taken: false },
  { id: 'b2', from: 'human-approve', to: 'intake', label: 'another-round', back: { taken: 1, max: 5 }, taken: false },
  { id: 'b3', from: 'human-review', to: 'intake', label: 'another-round', back: { taken: 0, max: 5 }, taken: false },
  { id: 'b4', from: 'await-merge', to: 'intake', label: 'push', back: { taken: 0, max: 20 }, taken: false },
  { id: 'b5', from: 'base-drift', to: 'intake', label: 'affected', back: { taken: 0, max: 10 }, taken: false },
]

/** Base-branch moves seen while the run was parked. Recorded on the run; the PR branch is never updated while it waits on the author. */
export interface BaseMove {
  iteration: number
  at: string
  atWh: number
  from: string
  to: string
  commits: { sha: string; title: string; author: string }[]
  verdict: 'unaffected-open' | 'affected' | 'unaffected-approved'
  how: string
  branch: string
}

export const RUN618_BASE_MOVES: BaseMove[] = [
  {
    iteration: 1, at: 'Wed 16:40', atWh: 6.4, from: '1f20c9e', to: '7b3e9d0',
    commits: [
      { sha: '5d1a7c2', title: 'Invoicing: VAT rounding uses banker’s rounding', author: 'hmartin' },
      { sha: '9e04b33', title: 'CI: cache NuGet packages between jobs', author: 'bokafor' },
      { sha: '7b3e9d0', title: 'Docs: settlement runbook typo', author: 'lnovak' },
    ],
    verdict: 'unaffected-open', how: 'code-only prefilter: no overlap with PR files, related files or contract globs (no model call)',
    branch: 'not updated: waiting on author',
  },
]

export interface IterationEvent {
  at: string
  label: string
  kind: 'push' | 'post' | 'gate' | 'steer' | 'status'
}

export interface IterationTrack {
  iteration: number
  round: number
  trigger: string
  startedAt: string
  /** offsets in minutes from iteration start */
  spans: { node: string; start: number; end: number; model?: ModelKey; lane?: number; status: NodeStatus }[]
  park?: { node: string; wh: number; outcome: string; during?: { node: string; atWh: number; label: string }[] }
  events: IterationEvent[]
  cost: Cost
}

export const RUN618_ITERATIONS: IterationTrack[] = [
  {
    iteration: 1, round: 1, trigger: 'PR opened', startedAt: 'Wed 10:02', cost: { api: 0.022, seatEq: 4.61 },
    spans: [
      { node: 'intake', start: 0, end: 0.3, status: 'done' }, { node: 'delta', start: 0.3, end: 0.4, status: 'done' },
      { node: 'categorise', start: 0.4, end: 0.6, model: 'jev', status: 'done' }, { node: 'select', start: 0.6, end: 0.9, model: 'jev', status: 'done' },
      { node: 'packs', start: 0.9, end: 1.6, status: 'done' },
      { node: 'reviewer:security', start: 1.6, end: 6.1, model: 'opus', lane: 0, status: 'done' },
      { node: 'reviewer:concurrency', start: 1.6, end: 10.1, model: 'opus', lane: 1, status: 'done' },
      { node: 'reviewer:data', start: 1.6, end: 5.2, model: 'sonnet', lane: 2, status: 'done' },
      { node: 'reviewer:general', start: 1.6, end: 4.4, model: 'sonnet', lane: 3, status: 'done' },
      { node: 'verify', start: 10.2, end: 10.9, model: 'luna', status: 'done' }, { node: 'publish', start: 10.9, end: 11.0, status: 'done' },
    ],
    park: {
      node: 'await-fixes',
      wh: 19.5,
      outcome: 'push 4a77b10',
      during: [{ node: 'base-drift', atWh: 6.4, label: 'Base moved (3 commits) · drift recorded · branch not updated: waiting on author' }],
    },
    events: [{ at: '11.0', label: '4 findings posted · status failure', kind: 'post' }],
  },
  {
    iteration: 2, round: 2, trigger: 'push 4a77b10', startedAt: 'Thu 11:30', cost: { api: 0.009, seatEq: 1.63 },
    spans: [
      { node: 'intake', start: 0, end: 0.1, status: 'done' }, { node: 'delta', start: 0.1, end: 0.3, status: 'done' },
      { node: 'categorise', start: 0.3, end: 0.3, status: 'reused' }, { node: 'select', start: 0.3, end: 0.5, model: 'jev', status: 'done' },
      { node: 'packs', start: 0.5, end: 0.7, status: 'done' },
      { node: 'reviewer:concurrency', start: 0.7, end: 5.7, model: 'opus', lane: 0, status: 'done' },
      { node: 'reviewer:data', start: 0.7, end: 3.9, model: 'sonnet', lane: 1, status: 'done' },
      { node: 'verify', start: 5.8, end: 6.3, model: 'haiku', status: 'done' }, { node: 'publish', start: 6.3, end: 6.4, status: 'done' },
    ],
    park: { node: 'human-approve', wh: 4.2, outcome: 'Hugo Martin: another-round + steer' },
    events: [{ at: '6.4', label: 'clean round → needs-human-approval[yes] (data p=0.93, sticky)', kind: 'status' }],
  },
  {
    iteration: 3, round: 3, trigger: 'another-round (steer)', startedAt: 'Fri 16:05', cost: { api: 0.03, seatEq: 3.6 },
    spans: [
      { node: 'intake', start: 0, end: 0.1, status: 'done' }, { node: 'delta', start: 0.1, end: 0.2, status: 'done' },
      { node: 'categorise', start: 0.2, end: 0.3, model: 'jev', status: 'done' }, { node: 'select', start: 0.3, end: 0.4, model: 'jev', status: 'done' },
      { node: 'packs', start: 0.4, end: 0.6, status: 'done' },
      { node: 'reviewer:security', start: 0.6, end: 4.7, model: 'opus', lane: 0, status: 'done' },
      { node: 'reviewer:concurrency', start: 0.6, end: 4.8, model: 'opus', lane: 1, status: 'running' },
      { node: 'reviewer:data', start: 0.6, end: 3.9, model: 'sonnet', lane: 2, status: 'done' },
      { node: 'reviewer:general', start: 0.6, end: 3.3, model: 'sonnet', lane: 3, status: 'done' },
    ],
    events: [{ at: '0', label: 'Steer: "check the refund ledger reconciliation path…"', kind: 'steer' }],
  },
]
