import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'
import type { AgentModelLevel } from './agents'
import type { AgentKind } from './boards'

/**
 * Delegated agent tasks (feature 007). One task is one delegated unit of work — deliberately not a
 * Card: tasks are cheap, they nest, and hundreds can exist for a single run.
 */

/** The only structural choice when delegating: do a piece of work, or own a chunk and run agents. */
export type AgentTaskKind = 'Worker' | 'Orchestrator'

export type AgentTaskRole =
  | 'Custom'
  | 'Plan'
  | 'Code'
  | 'Review'
  | 'Debug'
  | 'Coverage'
  | 'Docs'
  | 'Commit'
  | 'Test'
  | 'Deploy'
  | 'Merge'
  /**
   * Interpret one check-in bundle (CARD-0047). Machinery, not delegated work: the check worker
   * creates these pinned to the standing check interpreter, and the list endpoint hides them
   * unless `includeChecks` is asked for. Deliberately absent from AGENT_TASK_ROLES — it is not a
   * role a human picks.
   */
  | 'Check'

export type AgentTaskStatus =
  | 'Queued'
  | 'Dispatched'
  | 'Working'
  /** The delegate asked a question — it needs an answer, not a retry. */
  | 'Blocked'
  | 'Succeeded'
  | 'Failed'
  | 'Canceled'

/** How a settled report was classified (CARD-0159). Legacy on every pre-existing row. */
export type AgentTaskReportEvidence =
  | 'Legacy'
  | 'Marked'
  | 'UnmarkedAfterNudge'
  | 'QuestionHeuristic'
  | 'FinalMessageMissing'
  | 'Exempt'

export type WorkspaceMode = 'Shared' | 'Worktree' | 'ReadOnly'

export type AgentTaskEventType =
  | 'Created'
  | 'Dispatched'
  | 'Escalated'
  | 'Blocked'
  | 'Replied'
  | 'Merged'
  | 'Conflicted'
  | 'Retried'
  | 'Completed'
  | 'Failed'
  | 'Canceled'
  | 'Rejected'
  /** Something legal but risky — an orchestrator sharing its caller's directory. */
  | 'Warning'
  /** A scheduled check-in ran (CARD-0047) — an observation, never a state change. */
  | 'Check'
  /** The caller refined the task mid-flight (CARD-0062) — what the delegate was told and when. */
  | 'Refined'

export interface AgentTaskSummaryDto {
  id: string
  rootTaskId: string
  parentTaskId: string | null
  depth: number
  title: string
  kind: AgentTaskKind
  role: AgentTaskRole
  /** WHICH AGENT PROGRAM ran (or will run) it — ClaudeCode unless the caller chose Grok. */
  agentKind: AgentKind
  modelLevel: AgentModelLevel
  /** Set when the task was bumped up a tier — the chip shows the ladder, not just the destination. */
  escalatedFrom: AgentModelLevel | null
  status: AgentTaskStatus
  workspace: WorkspaceMode
  workingDirectory: string
  repoPath: string | null
  /** Where a Worktree task actually runs — the throwaway checkout, branch included. */
  worktreePath: string | null
  worktreeBranch: string | null
  scope: string | null
  /** Areas the task actually touched, filled at settlement (CARD-0063 S4). */
  observedScope?: string | null
  agentId: string | null
  agentName: string | null
  agentSessionId: string | null
  attempt: number
  createdAt: string
  dispatchedAt: string | null
  completedAt: string | null
  readAt?: string | null
  deliverablePath?: string | null
  deliverableRef?: string | null
  /** Non-null when recovery settled an unbound session without observing completion. */
  recoveredAt: string | null
  /** UNCACHED input only — add the two cache counters for a human "tokens in". */
  tokensIn: number
  /** Cached prefix re-read per turn, priced at ~0.1x input. Dominates an agentic session. */
  cacheReadTokens: number
  cacheCreationTokens: number
  tokensOut: number
  costUsd: number
  /**
   * 0 means the figure predates the CARD-0023 pricing fix (cache reads billed as fresh input, stale
   * rates) and is roughly 10x high — shown as a legacy estimate rather than passed off as current.
   */
  costPricingVersion: number
  /** This task plus everything under it — what a collapsed sub-orchestrator row reports. */
  subtreeCostUsd: number
  childCount: number
  /**
   * The caller's declared duration hint, in minutes (CARD-0047). NEVER a deadline — a task past it
   * is not late, it has only reached the point where the first check-in was scheduled.
   */
  expectedDurationMinutes: number
  /** When the next scheduled check-in is due; null means this task is never checked. */
  nextCheckAt: string | null
  checkCount: number
  /** The card this task's work is against (CARD-0040); null when nothing bound. */
  cardId?: string | null
  /** The bound card's identifier, denormalised at read time. */
  cardIdentifier?: string | null
  /** How the stored report was classified at settlement (CARD-0159). */
  reportEvidence?: AgentTaskReportEvidence
  /** CARD-0090. Set when kind/level was chosen by a complexity chain. */
  complexity?: 'Hard' | 'Medium' | 'Easy' | null
}

export interface AgentTaskEventDto {
  type: AgentTaskEventType
  modelLevel: AgentModelLevel | null
  detail: string
  at: string
}

export interface AgentTaskDetailDto {
  summary: AgentTaskSummaryDto
  goal: string
  /** The delegate's final message, untouched — forwarding may excerpt it, this never does. */
  result: string | null
  resultFilePath: string | null
  deliverablePath?: string | null
  deliverableRef?: string | null
  failureReason: string | null
  mergeTargetRef: string | null
  events: AgentTaskEventDto[]
  /** CARD-0256. Machine-readable class of failureReason when one was assigned. */
  failureCode?: string | null
}

/** Fleet-wide header counters; unlike the board list, these never use its history window. */
export interface AgentTaskListSummaryDto {
  active: number
  blocked: number
  runs: number
  totalCostUsd: number
  byStatus: Partial<Record<AgentTaskStatus, number>>
}

/** Why a queued pipeline row has not dispatched (CARD-0304 / CARD-0031). */
export type AgentTaskPipelineQueueReason =
  | 'sharedCheckoutLease'
  | 'concurrencyCap'
  | 'routingPinNotBefore'
  | 'awaitingDispatch'

export type RoutingPinProvenance = 'Auto' | 'Human'
export type RoutingPinStrength = 'Preferred' | 'Required'

export interface RoutingPinRefDto {
  id: string
  cardId: string | null
  cardIdentifier: string | null
  role: AgentTaskRole
  provenance: RoutingPinProvenance
  strength: RoutingPinStrength
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  notBefore: string | null
  reason: string
}

export interface AgentTaskPipelineCardRefDto {
  id: string
  identifier: string
  title: string
}

export interface AgentTaskPipelineHolderDto {
  taskId: string
  shortId: string
  title: string
}

export interface AgentTaskPipelineInFlightDto {
  taskId: string
  shortId: string
  title: string
  status: AgentTaskStatus
  card: AgentTaskPipelineCardRefDto | null
  agentName: string | null
  dispatchedAt: string | null
  lastActivityAt: string
}

export interface AgentTaskPipelineQueuedDto {
  taskId: string
  shortId: string
  title: string
  card: AgentTaskPipelineCardRefDto | null
  createdAt: string
  queueReason: AgentTaskPipelineQueueReason
  heldBy: AgentTaskPipelineHolderDto[]
}

export interface AgentTaskPipelineBlockedDto {
  taskId: string
  shortId: string
  title: string
  card: AgentTaskPipelineCardRefDto | null
  createdAt: string
  /** CARD-0090: this Blocked row is routing-exhausted, not a question. */
  routingExhausted?: boolean
}

export interface AgentTaskPipelineReadyDto {
  card: AgentTaskPipelineCardRefDto
  sourcePlanTaskId: string
  sourcePlanShortId: string
  readySince: string
  deliverablePath: string
  deliverableRef: string | null
  routingPin?: RoutingPinRefDto | null
}

export interface AgentTaskPipelineStageDto {
  role: AgentTaskRole
  recommendedInFlight: number | null
  inFlightCount: number
  atOrAboveRecommendation: boolean
  inFlight: AgentTaskPipelineInFlightDto[]
  queued: AgentTaskPipelineQueuedDto[]
  blocked: AgentTaskPipelineBlockedDto[]
  ready: AgentTaskPipelineReadyDto[]
  routingPin?: RoutingPinRefDto | null
}

export interface AgentTaskPipelineDto {
  asOf: string
  recommendationsAreAdvisory: boolean
  maxConcurrentTasks: number
  inFlightAgainstCap: number
  stages: AgentTaskPipelineStageDto[]
}

export interface CreateAgentTaskRequest {
  goal: string
  title?: string | null
  kind?: AgentTaskKind
  role?: AgentTaskRole
  /** Null takes the role policy's tier — which is the whole point of picking a role. */
  modelLevel?: AgentModelLevel | null
  /**
   * Null lets the server decide: workers run Shared; an orchestrator gets its own worktree unless
   * it already has its own location. An explicit value is honoured — with a warning when it puts
   * an orchestrator in its caller's directory.
   */
  workspace?: WorkspaceMode | null
  workingDirectory?: string | null
  scope?: string | null
  /**
   * Arm the PreToolUse deny hook in an orchestrator's worktree (blocks direct Edit/Write —
   * "delegate this instead"). Null follows the server's config default.
   */
  denyDirectEdits?: boolean | null
  /**
   * Bypass the subscription-quota launch gate. A 409 `subscription_quota_low` is the
   * refusal; re-send with this true to queue anyway.
   */
  ignoreSubscriptionQuota?: boolean
  /**
   * Bypass the model-availability create 409. Queues the task; dispatch still waits
   * for the hold to clear. Start never honours this flag.
   */
  ignoreModelDisabled?: boolean
}

export interface AgentTaskCreatedDto {
  id: string
  shortId: string
  status: AgentTaskStatus
  modelLevel: AgentModelLevel
  /** Set when the creation was legal but risky — surface it, the caller can still reconsider. */
  warning: string | null
  /**
   * True when the report will not be routed anywhere — `ReplyTo == None`, i.e. the caller had no
   * token and so no parent task or session to report back to. The result only lands on the board
   * (CARD-0020 S1). Optional: a server that predates the field simply omits it.
   */
  noReplyRouting?: boolean
}

/** Role → tier, mirroring the server's default RolePolicy. Shown next to each role in the picker. */
export const AGENT_TASK_ROLES: Array<{
  value: AgentTaskRole
  label: string
  use: string
  level: AgentModelLevel
}> = [
  { value: 'Plan', label: 'Plan', use: 'decompose, design, choose an approach', level: 'Frontier' },
  { value: 'Code', label: 'Code', use: 'write or change code', level: 'Frontier' },
  { value: 'Review', label: 'Review', use: 'judge whether logic is correct', level: 'Frontier' },
  { value: 'Debug', label: 'Debug', use: 'find out why something is broken', level: 'High' },
  { value: 'Coverage', label: 'Coverage', use: 'check what a change missed', level: 'High' },
  { value: 'Merge', label: 'Merge', use: 'resolve a conflict left by a worktree task', level: 'High' },
  { value: 'Docs', label: 'Docs', use: 'prose, markdown, comments', level: 'Medium' },
  { value: 'Commit', label: 'Commit', use: 'git add/commit/push/branch, PRs', level: 'Medium' },
  { value: 'Test', label: 'Test', use: 'run a suite or build and report what failed', level: 'Low' },
  { value: 'Deploy', label: 'Deploy', use: 'run a script, restart a service, check health', level: 'Low' },
  { value: 'Custom', label: 'Custom', use: 'anything else', level: 'High' },
]

export const agentTaskKeys = {
  list: (includeChecks = false, options: AgentTaskListOptions = {}) =>
    ['agentTasks', 'list', includeChecks, options.since ?? null, options.status?.join(',') ?? null] as const,
  summary: () => ['agentTasks', 'summary'] as const,
  detail: (id: string) => ['agentTasks', 'detail', id] as const,
  pipeline: () => ['agentTasks', 'pipeline'] as const,
}

/** Seven days is the shipped Delegation:DefaultWindowDays setting. */
export const DELEGATIONS_DEFAULT_WINDOW_DAYS = 7

/** Settled tasks this recent still sit on the active board (the *Just settled* lane). */
export const DELEGATIONS_ACTIVE_GRACE_MINUTES = 60

export interface AgentTaskListOptions {
  /** `default` / `active` resolve when each request runs, keeping the window rolling without cache-key churn. */
  since?: string | 'default' | 'active'
  status?: AgentTaskStatus[]
}

function queryForAgentTasks(includeChecks: boolean, options: AgentTaskListOptions): string {
  const query = new URLSearchParams()
  if (includeChecks) query.set('includeChecks', 'true')
  if (options.since) {
    const since =
      options.since === 'default'
        ? new Date(Date.now() - DELEGATIONS_DEFAULT_WINDOW_DAYS * 24 * 60 * 60 * 1000).toISOString()
        : options.since === 'active'
          ? new Date(Date.now() - DELEGATIONS_ACTIVE_GRACE_MINUTES * 60 * 1000).toISOString()
          : options.since
    query.set('since', since)
  }
  if (options.status?.length) query.set('status', options.status.join(','))
  const suffix = query.toString()
  return suffix ? `/agent-tasks?${suffix}` : '/agent-tasks'
}

/**
 * The delegations board. `includeChecks` surfaces the per-check interpretation tasks (CARD-0047),
 * which the server hides by default — one exists per interpreted check-in and none of them is
 * anybody's delegated work, so the board would otherwise drown in them on a busy fleet.
 */
export function useAgentTasks(includeChecks = false, options: AgentTaskListOptions = {}) {
  return useQuery({
    queryKey: agentTaskKeys.list(includeChecks, options),
    queryFn: () => apiGet<AgentTaskSummaryDto[]>(queryForAgentTasks(includeChecks, options)),
    // SignalR invalidates on every task change; this only covers a dropped connection.
    refetchInterval: 15_000,
    staleTime: 5_000,
  })
}

export function useAgentTaskListSummary() {
  return useQuery({
    queryKey: agentTaskKeys.summary(),
    queryFn: () => apiGet<AgentTaskListSummaryDto>('/agent-tasks/summary'),
    refetchInterval: 15_000,
    staleTime: 5_000,
  })
}

/**
 * Fleet-wide pipeline projection (CARD-0304 / CARD-0031). SignalR already invalidates this key
 * (`AgentTaskChanged`, `AgentQueueChanged`, `SessionFinished`); the interval covers a dropped
 * connection. Nobody on Home should block on this — a failed fetch is "no enrichment".
 */
export function usePipeline(enabled = true) {
  return useQuery({
    queryKey: agentTaskKeys.pipeline(),
    queryFn: () => apiGet<AgentTaskPipelineDto>('/agent-tasks/pipeline'),
    refetchInterval: 15_000,
    staleTime: 5_000,
    enabled,
  })
}

export function useAgentTask(id: string | null) {
  return useQuery({
    queryKey: agentTaskKeys.detail(id ?? ''),
    queryFn: () => apiGet<AgentTaskDetailDto>(`/agent-tasks/${id}`),
    enabled: !!id,
  })
}

/** Invalidate the board and the open drawer together — an action changes both. */
function useTaskMutation<TVariables, TResult>(
  mutationFn: (variables: TVariables) => Promise<TResult>,
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => {
      // The PREFIX, not agentTaskKeys.list() — that would invalidate only the default board and
      // leave an open includeChecks view stale.
      queryClient.invalidateQueries({ queryKey: ['agentTasks', 'list'] })
      queryClient.invalidateQueries({ queryKey: agentTaskKeys.summary() })
      queryClient.invalidateQueries({ queryKey: ['agentTasks', 'detail'] })
    },
  })
}

export function useCreateAgentTask() {
  return useTaskMutation((request: CreateAgentTaskRequest) =>
    apiPost<AgentTaskCreatedDto>('/agent-tasks', request),
  )
}

export function useCancelAgentTask() {
  return useTaskMutation((id: string) => apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/cancel`, {}))
}

export function useRetryAgentTask() {
  return useTaskMutation((id: string) => apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/retry`, {}))
}

export function useEscalateAgentTask() {
  return useTaskMutation(({ id, modelLevel }: { id: string; modelLevel?: AgentModelLevel }) =>
    apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/escalate`, { modelLevel: modelLevel ?? null }),
  )
}

/** First read wins, so a subsequent open never rewrites the operator-visible timestamp. */
export function useMarkAgentTaskRead() {
  return useTaskMutation((id: string) => apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/read`, {}))
}

/** Answer a Blocked delegate's question. Taking the work back is the failure mode this prevents. */
export function useReplyToAgentTask() {
  return useTaskMutation(({ id, message }: { id: string; message: string }) =>
    apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/reply`, { message }),
  )
}

/** CARD-0090: explicit kind/level that ends chain governance. */
export function useRerouteAgentTask() {
  return useTaskMutation(
    ({ id, agentKind, modelLevel }: { id: string; agentKind: AgentKind; modelLevel: AgentModelLevel }) =>
      apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/reroute`, { agentKind, modelLevel }),
  )
}
