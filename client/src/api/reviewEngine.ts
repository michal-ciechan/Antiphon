import { apiGet, apiPost } from './client'

/**
 * Review engine API (docs/features/012-review-engine/api.md, contract v0). Types mirror the
 * contract exactly; enums are the server's PascalCase strings. The client builds against the
 * `ReviewApi` seam so the in-memory fake (features/review/fake) and the HTTP implementation below
 * are interchangeable. Selection lives in `reviewApiMode()`; the hooks are in features/review.
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
export type NodeStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped' | 'Waiting' | 'NotReached'
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
  baseRef: string
  headRef: string
  baseSha: string
  headSha: string
  /** code-owner teams/users of the changed paths */
  owners: string[]
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
  costUsd: number
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

export interface ReviewGateDto {
  id: string
  runId: string
  kind: GateKind
  status: QueueItemStatus
  teamKey: string | null
  assigneeLogin: string | null
  openedAt: string
  decidedAt: string | null
  decision: GateDecision | null
  steerPrompt: string | null
  /** why a human is needed */
  reasons: string[]
  passedBy: string[]
}

export interface ReviewRunDetailDto extends ReviewRunSummaryDto {
  blastRadius: BlastRadiusDto | null
  categories: string[]
  workflowRevisionId: string | null
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
  slaState: SlaState
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

// ---------------------------------------------------------------- the seam

/** One method per contract route. Both implementations reject with `ApiError` on 404/409/422. */
export interface ReviewApi {
  listRuns(filter?: ReviewRunFilter): Promise<ListResult<ReviewRunSummaryDto>>
  getKpis(): Promise<ReviewKpisDto>
  createRun(request: CreateReviewRunRequest): Promise<ReviewRunDetailDto>
  getRun(runId: string): Promise<ReviewRunDetailDto>
  listFindings(runId: string, status?: FindingStatus): Promise<ListResult<ReviewFindingDto>>
  refreshRun(runId: string): Promise<void>
  respondToFinding(findingId: string, request: FindingResponseRequest): Promise<ReviewFindingDto>
  respondToRun(runId: string, responses: RunFindingResponse[]): Promise<{ items: ReviewFindingDto[] }>
  getQueue(scope: QueueScope, login?: string): Promise<ListResult<QueueItemDto>>
  assignGate(gateId: string, login?: string): Promise<ReviewGateDto>
  passGate(gateId: string, reason: string): Promise<ReviewGateDto>
  decideGate(gateId: string, request: GateDecisionRequest): Promise<ReviewRunDetailDto>
  getDefaultWorkflow(): Promise<WorkflowGraphDto>
  validateWorkflow(graph: WorkflowGraphDto): Promise<WorkflowValidationDto>
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
  respondToFinding: (findingId, request) => apiPost(`/review-findings/${enc(findingId)}/responses`, request),
  respondToRun: (runId, responses) => apiPost(`/review-runs/${enc(runId)}/responses`, { responses }),
  getQueue: (scope, login) => apiGet(`/review-queue${query({ scope, login })}`),
  assignGate: (gateId, login) => apiPost(`/review-gates/${enc(gateId)}/assign`, login ? { login } : {}),
  passGate: (gateId, reason) => apiPost(`/review-gates/${enc(gateId)}/pass`, { reason }),
  decideGate: (gateId, request) => apiPost(`/review-gates/${enc(gateId)}/decision`, request),
  getDefaultWorkflow: () => apiGet('/review-workflows/default'),
  validateWorkflow: (graph) => apiPost('/review-workflows/validate', graph),
}

// ---------------------------------------------------------------- selection

export type ReviewApiMode = 'http' | 'fake'

/** localStorage key; 'http' or 'fake' overrides the build-time VITE_REVIEW_API. */
export const REVIEW_API_STORAGE_KEY = 'antiphon.reviewApi'

/**
 * Which implementation the review UI talks to. A localStorage override wins (so the operator can
 * flip a running client without a rebuild); otherwise `VITE_REVIEW_API=http` selects HTTP and
 * anything else selects the in-memory fake. Storage can throw (private mode, blocked site data),
 * so every access is guarded and falls through to the env value.
 */
export function reviewApiMode(env: { VITE_REVIEW_API?: unknown } = import.meta.env as { VITE_REVIEW_API?: unknown }): ReviewApiMode {
  try {
    const stored = window.localStorage.getItem(REVIEW_API_STORAGE_KEY)
    if (stored === 'http' || stored === 'fake') return stored
  } catch {
    // storage unavailable: use the build-time value
  }
  return env.VITE_REVIEW_API === 'http' ? 'http' : 'fake'
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
