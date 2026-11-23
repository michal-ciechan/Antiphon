import { apiGet, apiPost } from './client'

/**
 * Review engine API (docs/features/012-review-engine/api.md, contract v0.5). Types mirror the
 * contract exactly; enums are the server's PascalCase strings. The client builds against the
 * `ReviewApi` seam so the in-memory fake (features/review/fake) and the HTTP implementation below
 * are interchangeable. HTTP is the default; `reviewApiMode()` picks the fake only on request. The
 * hooks are in features/review.
 *
 * Not to be confused with `./review.ts`, which is the agent files-review surface.
 */

// ---------------------------------------------------------------- enums

export type ReviewHost = 'GitHub' | 'Local'
export type ReviewRunStatus =
  | 'Queued'
  | 'Running'
  | 'AwaitingFixes'
  | 'AtGate'
  | 'Approved'
  | 'AwaitingMerge'
  | 'Merged'
  | 'Closed'
  | 'Failed'
export type ReviewStage =
  | 'Intake'
  | 'Categorise'
  | 'Select'
  | 'Review'
  | 'Collate'
  | 'Verify'
  | 'Publish'
  | 'AwaitingFixes'
  | 'HumanGate'
  | 'Approval'
  | 'BranchMaintenance'
  | 'Done'
export type NodeKind = 'Code' | 'Decision' | 'Completion' | 'Agent' | 'FanOut' | 'Join' | 'Gate' | 'Wait'
/** `FailedContinued`: the step failed and its onFailure: continue policy let the run go on. */
export type NodeStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped' | 'Waiting' | 'NotReached' | 'FailedContinued'
export type Severity = 'Nit' | 'Minor' | 'Major' | 'Blocker'
export type FindingStatus = 'Open' | 'WillFix' | 'Fixed' | 'Disputed' | 'Dismissed' | 'Deferred' | 'Outdated' | 'Accepted'
export type SyncState = 'NotApplicable' | 'Pending' | 'Synced' | 'Failed'
export type FindingAction = 'WillFix' | 'Fixed' | 'Dismiss' | 'Dispute' | 'Question' | 'Defer' | 'NotABlocker' | 'Reply'
export type ReasonCategory =
  | 'FalsePositive'
  | 'ByDesign'
  | 'OutOfScope'
  | 'AcceptedRisk'
  | 'FixLater'
  | 'Duplicate'
  | 'StylePreference'
export type DismissScope = 'Instance' | 'Path' | 'Repo' | 'Team'
export type GateKind = 'ReviewGate' | 'ApprovalGate' | 'DisputedFindings' | 'LearnProposal'
export type GateDecision = 'Approve' | 'AnotherRound' | 'Close'
export type QueueItemStatus = 'Unassigned' | 'Assigned' | 'InProgress' | 'Done'
export type Transport = 'Http' | 'Seat' | 'OneShot'
export type WaitingOn = 'Author' | 'Humans' | 'Bot'
export type SlaState = 'Ok' | 'Warn' | 'Breached'
export type QueueScope = 'Mine' | 'Teams' | 'All'
export type CodeOwnerKind = 'Team' | 'User'
export type DiagnosticSeverity = 'Error' | 'Warning'
/**
 * What started an iteration. Open-ended on the wire; these are the values the engine emits today:
 * `manual` is a manual intake, `push` a Refresh (or webhook) that saw a new head, `recheck` a
 * Refresh of a failed run with the same head.
 */
export type ReviewIterationTrigger = 'opened' | 'manual' | 'push' | 'recheck' | 'another-round' | 'base-moved' | (string & {})
/** Problem Details `code` on a 503 from intake or Refresh when the code host fails (contract v0.2). */
export type ReviewCodeHostErrorCode = 'review_code_host_rejected' | 'review_code_host_rate_limited' | 'review_code_host_unavailable' | 'review_code_host_too_large'

// ---------------------------------------------------------------- DTOs

export interface ReviewSubjectDto {
  id: string
  host: ReviewHost
  /** owner/name */
  repo: string
  prNumber: number | null
  title: string
  url: string | null
  authorLogin: string
  authorDisplayName: string | null
  /** e.g. 'Co-authored-by' when the PR author was a service account */
  authorResolvedBy: string | null
  /** the account that opened the PR; differs from authorLogin when the author was resolved */
  openedByLogin: string
  baseRef: string
  headRef: string
  baseSha: string
  headSha: string
  /** code owners of the changed paths */
  owners: CodeOwnerDto[]
}

export interface CodeOwnerDto {
  kind: CodeOwnerKind
  name: string
}

/** 1-5 each. The whole object is null while the run is not categorised. */
export interface BlastRadiusDto {
  reach: number
  externalContract: number
  data: number
  rollbackAtRelease: number
  reversalAfterMonths: number
  exposure: number
}

export interface ReviewRunSummaryDto {
  id: string
  subject: ReviewSubjectDto
  status: ReviewRunStatus
  stage: ReviewStage
  round: number
  botOnlyRounds: number
  needsHumanApproval: boolean | null
  openFindings: number
  openBlockers: number
  /** distinct resolved models used, e.g. 'jev-1.14.0', 'gpt-5.6-luna' */
  models: string[]
  /** actual + estimated */
  costUsd: number
  /** metered API calls */
  costActualUsd: number
  /** seat API-equivalent (not billed) */
  costEstimatedUsd: number
  blastRadius: BlastRadiusDto | null
  startedAt: string
  updatedAt: string
  waitingOn: WaitingOn | null
}

export interface NodeExecutionDto {
  id: string
  nodeId: string
  iteration: number
  kind: NodeKind
  status: NodeStatus
  /** fan-out item, e.g. the reviewer group 'security' */
  itemKey: string | null
  /** the outgoing edge label taken, e.g. 'moved', 'clean' */
  outcome: string | null
  provider: string | null
  model: string | null
  transport: Transport | null
  inputTokens: number
  outputTokens: number
  costUsd: number
  startedAt: string | null
  completedAt: string | null
  summary: string | null
}

export interface GatePassDto {
  login: string
  reason: string
  at: string
}

export interface ReviewGateDto {
  id: string
  runId: string
  kind: GateKind
  status: QueueItemStatus
  teamKey: string | null
  assigneeLogin: string | null
  assigneeDisplayName: string | null
  openedAt: string
  decidedAt: string | null
  decidedBy: string | null
  decision: GateDecision | null
  steerPrompt: string | null
  /** why a human is needed */
  reasons: string[]
  allowedDecisions: GateDecision[]
  /** computed by the server for the current viewer (`GET /api/review-me`) */
  viewerCanDecide: boolean
  /** e.g. 'You are the author'; null when the viewer can decide */
  viewerCannotDecideReason: string | null
  /** "not me" passes, oldest first; passers are excluded from the next random pick */
  passes: GatePassDto[]
}

export interface ReviewIterationDto {
  iteration: number
  round: number
  trigger: ReviewIterationTrigger
  startedAt: string
  /** what this iteration reviewed (null until intake pins it); the next delta starts from headSha */
  baseSha: string | null
  headSha: string | null
}

export interface ReviewRunDetailDto extends ReviewRunSummaryDto {
  categories: string[]
  workflowRevisionId: string | null
  iterations: ReviewIterationDto[]
  /** every execution across iterations */
  nodes: NodeExecutionDto[]
  gates: ReviewGateDto[]
}

export interface FindingEventDto {
  id: string
  action: FindingAction
  actorLogin: string
  actorDisplayName: string | null
  reasonCategory: ReasonCategory | null
  scope: DismissScope | null
  text: string | null
  syncState: SyncState
  createdAt: string
}

export interface ReviewFindingDto {
  id: string
  runId: string
  /** F1 */
  key: string
  checkId: string | null
  /** the check's @humans text */
  checkWhy: string | null
  severity: Severity
  /** 'security', 'data-loss' */
  flags: string[]
  bypassesRoundBar: boolean
  confidence: number
  filePath: string
  lineStart: number | null
  lineEnd: number | null
  anchor: string
  title: string
  body: string
  suggestion: string | null
  /** unified-diff hunk around the anchor at the reviewed head */
  hunk: string | null
  status: FindingStatus
  roundRaised: number
  posted: boolean
  syncState: SyncState
  externalThreadUrl: string | null
  events: FindingEventDto[]
}

export interface FindingResponseRequest {
  action: FindingAction
  reasonCategory?: ReasonCategory
  scope?: DismissScope
  text?: string
}

export type RunFindingResponse = FindingResponseRequest & { findingId: string }

export interface GateDecisionRequest {
  decision: GateDecision
  steerPrompt?: string
}

export interface QueueItemDto {
  gate: ReviewGateDto
  run: ReviewRunSummaryDto
  ageWorkingMinutes: number
  slaWorkingMinutes: number
  slaState: SlaState
}

export interface ReviewViewerDto {
  login: string
  displayName: string | null
  teams: string[]
}

export interface ModelUsageDto {
  model: string
  provider: string
  transport: Transport
  calls: number
  costUsd: number
}

export interface ReviewKpisDto {
  inReview: number
  waitingOnHumans: number
  averageRounds: number
  costTodayUsd: number
  costPerAcceptedFindingUsd: number | null
  modelUsage: ModelUsageDto[]
}

export interface WorkflowSectionDto {
  id: string
  title: string
  nodeIds: string[]
  /** default display state; the viewer's own toggles win */
  collapsed: boolean
}

export interface WorkflowNodeDto {
  id: string
  kind: NodeKind
  run: string | null
  description: string | null
  params: Record<string, unknown>
}

export interface WorkflowEdgeDto {
  from: string
  to: string
  label: string | null
  maxIterations: number | null
  isBackEdge: boolean
  /** The gate that opens when maxIterations is exceeded (design D-2); required on limited back edges. */
  overflow?: string | null
  description?: string | null
}

export interface WorkflowGraphDto {
  name: string
  revisionId: string | null
  hash: string
  sections: WorkflowSectionDto[]
  nodes: WorkflowNodeDto[]
  edges: WorkflowEdgeDto[]
}

export interface WorkflowDiagnosticDto {
  severity: DiagnosticSeverity
  code: string
  message: string
  nodeIds: string[]
  edge: { from: string; to: string } | null
}

export interface WorkflowValidationDto {
  valid: boolean
  diagnostics: WorkflowDiagnosticDto[]
}

export interface ListResult<T> {
  items: T[]
  total: number
}

export interface ReviewRunFilter {
  status?: ReviewRunStatus
  repo?: string
  stage?: ReviewStage
  q?: string
}

export interface CreateReviewRunRequest {
  host: ReviewHost
  repo: string
  prNumber?: number
  baseSha?: string
  headSha?: string
  title?: string
}

/** `POST /api/review-runs/{id}/rounds` (contract v0.5): a manual round, same round, with an optional steer. */
export interface ReviewRoundRequest {
  steerPrompt?: string
}

export interface ReviewPollingRepoDto {
  repo: string
  host: ReviewHost
  /** empty means every base branch */
  baseBranches: string[]
  includeDrafts: boolean
  skipLabels: string[]
  requireLabel: string | null
}

export interface ReviewPollingRepoPassDto {
  repo: string
  openPullRequests: number
  runsStarted: number
  runsRefreshed: number
  commandsHandled: number
  /** the last error this pass hit for the repo (rate limit, 403, network), or null */
  error: string | null
}

export interface ReviewPollingPassDto {
  trigger: 'timer' | 'manual' | (string & {})
  startedAt: string
  finishedAt: string
  /** true when no repo reported an error */
  ok: boolean
  repos: ReviewPollingRepoPassDto[]
}

/** `GET /api/review-polling` (contract v0.5): the polling settings and the last pass, kept in memory. */
export interface ReviewPollingDto {
  enabled: boolean
  intervalSeconds: number
  commandPrefix: string
  repos: ReviewPollingRepoDto[]
  /** a pass is running now */
  running: boolean
  /** null until the first pass since the server started */
  lastPass: ReviewPollingPassDto | null
}

// ---------------------------------------------------------------- the seam

/** One method per contract route. Both implementations reject with `ApiError` on 404/409/422. */
export interface ReviewApi {
  listRuns(filter?: ReviewRunFilter): Promise<ListResult<ReviewRunSummaryDto>>
  getKpis(): Promise<ReviewKpisDto>
  createRun(request: CreateReviewRunRequest): Promise<ReviewRunDetailDto>
  getRun(runId: string): Promise<ReviewRunDetailDto>
  listFindings(runId: string, status?: FindingStatus): Promise<ListResult<ReviewFindingDto>>
  refreshRun(runId: string): Promise<void>
  /**
   * A manual review round with an optional steer. 409 when the run waits at a gate (decide it with
   * another round instead), is finished, or is already reviewing.
   */
  requestRound(runId: string, request: ReviewRoundRequest): Promise<ReviewRunDetailDto>
  /** Code host polling: the settings and the last pass. */
  getPolling(): Promise<ReviewPollingDto>
  /** Runs one polling pass now (even with the timer off) and returns the same DTO. */
  pollNow(): Promise<ReviewPollingDto>
  respondToFinding(findingId: string, request: FindingResponseRequest): Promise<ReviewFindingDto>
  respondToRun(runId: string, responses: RunFindingResponse[]): Promise<{ items: ReviewFindingDto[] }>
  getQueue(scope: QueueScope, login?: string): Promise<ListResult<QueueItemDto>>
  assignGate(gateId: string, login?: string): Promise<ReviewGateDto>
  passGate(gateId: string, reason: string): Promise<ReviewGateDto>
  decideGate(gateId: string, request: GateDecisionRequest): Promise<ReviewRunDetailDto>
  getDefaultWorkflow(): Promise<WorkflowGraphDto>
  /** The snapshot a run used (`workflowRevisionId`). */
  getWorkflowRevision(revisionId: string): Promise<WorkflowGraphDto>
  validateWorkflow(graph: WorkflowGraphDto): Promise<WorkflowValidationDto>
  /** Who the server acts as; mutations record this viewer as the actor. */
  getMe(): Promise<ReviewViewerDto>
  /**
   * `POST /api/review-runs/demo`: seeds the demo runs and returns their summaries. A 404 means the
   * server has seeding off (`Review:DemoSeedEnabled`). Not in contract v0.2's route table yet.
   */
  seedDemo(): Promise<ListResult<ReviewRunSummaryDto>>
}

function query(params: Record<string, string | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') search.set(key, value)
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

const enc = encodeURIComponent

/** The contract routes, through the shared client helpers (Problem Details → `ApiError`). */
export const httpReviewApi: ReviewApi = {
  listRuns: (filter = {}) =>
    apiGet(`/review-runs${query({ status: filter.status, repo: filter.repo, stage: filter.stage, q: filter.q })}`),
  getKpis: () => apiGet('/review-runs/kpis'),
  createRun: (request) => apiPost('/review-runs', request),
  getRun: (runId) => apiGet(`/review-runs/${enc(runId)}`),
  listFindings: (runId, status) => apiGet(`/review-runs/${enc(runId)}/findings${query({ status })}`),
  refreshRun: (runId) => apiPost(`/review-runs/${enc(runId)}/refresh`, {}),
  requestRound: (runId, request) => apiPost(`/review-runs/${enc(runId)}/rounds`, request),
  getPolling: () => apiGet('/review-polling'),
  pollNow: () => apiPost('/review-polling/poll', {}),
  respondToFinding: (findingId, request) => apiPost(`/review-findings/${enc(findingId)}/responses`, request),
  respondToRun: (runId, responses) => apiPost(`/review-runs/${enc(runId)}/responses`, { responses }),
  getQueue: (scope, login) => apiGet(`/review-queue${query({ scope, login })}`),
  assignGate: (gateId, login) => apiPost(`/review-gates/${enc(gateId)}/assign`, login ? { login } : {}),
  passGate: (gateId, reason) => apiPost(`/review-gates/${enc(gateId)}/pass`, { reason }),
  decideGate: (gateId, request) => apiPost(`/review-gates/${enc(gateId)}/decision`, request),
  getDefaultWorkflow: () => apiGet('/review-workflows/default'),
  getWorkflowRevision: (revisionId) => apiGet(`/review-workflows/revisions/${enc(revisionId)}`),
  validateWorkflow: (graph) => apiPost('/review-workflows/validate', graph),
  getMe: () => apiGet('/review-me'),
  seedDemo: () => apiPost('/review-runs/demo', {}),
}

// ---------------------------------------------------------------- selection

export type ReviewApiMode = 'http' | 'fake'

/** localStorage key; 'fake' (or 'http') overrides the build-time VITE_REVIEW_API. */
export const REVIEW_API_STORAGE_KEY = 'antiphon.reviewApi'

/**
 * Which implementation the review UI talks to. HTTP is the default; the in-memory fake is used only
 * when asked for, by localStorage `antiphon.reviewApi=fake` (so a running client can flip without a
 * rebuild) or `VITE_REVIEW_API=fake`. A stored 'http' wins over a fake build, so the fake banner's
 * "Use HTTP API" works in either case. Storage can throw (private mode, blocked site data), so the
 * read is guarded and falls through to the env value. Storybook and Vitest force the fake through
 * `forceReviewApiMode` (features/review/reviewApi.ts) instead of relying on this.
 */
export function reviewApiMode(env: { VITE_REVIEW_API?: unknown } = import.meta.env as { VITE_REVIEW_API?: unknown }): ReviewApiMode {
  try {
    const stored = window.localStorage.getItem(REVIEW_API_STORAGE_KEY)
    if (stored === 'http' || stored === 'fake') return stored
  } catch {
    // storage unavailable: use the build-time value
  }
  return env.VITE_REVIEW_API === 'fake' ? 'fake' : 'http'
}

/** Sets or clears (null) the localStorage override. Returns false when storage is unavailable. */
export function setReviewApiOverride(mode: ReviewApiMode | null): boolean {
  try {
    if (mode) window.localStorage.setItem(REVIEW_API_STORAGE_KEY, mode)
    else window.localStorage.removeItem(REVIEW_API_STORAGE_KEY)
    return true
  } catch {
    return false
  }
}

// ---------------------------------------------------------------- query keys

/**
 * Query keys, exactly as the contract's SignalR section names them. `run(id)` prefix-matches
 * `findings(id)`; the list/queue keys are prefixes of their filtered variants.
 */
export const reviewEngineKeys = {
  runs: ['reviewRuns'] as const,
  runList: (filter: ReviewRunFilter) => ['reviewRuns', filter] as const,
  run: (runId: string) => ['reviewRun', runId] as const,
  findings: (runId: string) => ['reviewRun', runId, 'findings'] as const,
  queue: ['reviewQueue'] as const,
  queueScope: (scope: QueueScope, login: string | undefined) => ['reviewQueue', scope, login ?? null] as const,
  kpis: ['reviewKpis'] as const,
  defaultWorkflow: ['reviewWorkflows', 'default'] as const,
  workflowRevision: (revisionId: string) => ['reviewWorkflows', 'revisions', revisionId] as const,
  /** not in the SignalR list: the viewer changes only with identity (or the fake's viewer switch) */
  me: ['reviewMe'] as const,
  /** not in the SignalR list: polled, and replaced by the poll-now result */
  polling: ['reviewPolling'] as const,
}

/** `ReviewRunChanged { runId }` → the keys it invalidates (contract, SignalR section). */
export function reviewRunChangedKeys(runId: string | undefined): ReadonlyArray<readonly unknown[]> {
  return [
    reviewEngineKeys.runs,
    ...(runId ? [reviewEngineKeys.run(runId), reviewEngineKeys.findings(runId)] : []),
    reviewEngineKeys.queue,
    reviewEngineKeys.kpis,
  ]
}
