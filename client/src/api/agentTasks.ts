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
  scopeGlob: string | null
  agentId: string | null
  agentName: string | null
  agentSessionId: string | null
  attempt: number
  createdAt: string
  dispatchedAt: string | null
  completedAt: string | null
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
  failureReason: string | null
  mergeTargetRef: string | null
  events: AgentTaskEventDto[]
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
  scopeGlob?: string | null
  /**
   * Arm the PreToolUse deny hook in an orchestrator's worktree (blocks direct Edit/Write —
   * "delegate this instead"). Null follows the server's config default.
   */
  denyDirectEdits?: boolean | null
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
  list: (includeChecks = false) => ['agentTasks', 'list', includeChecks] as const,
  detail: (id: string) => ['agentTasks', 'detail', id] as const,
}

/**
 * The delegations board. `includeChecks` surfaces the per-check interpretation tasks (CARD-0047),
 * which the server hides by default — one exists per interpreted check-in and none of them is
 * anybody's delegated work, so the board would otherwise drown in them on a busy fleet.
 */
export function useAgentTasks(includeChecks = false) {
  return useQuery({
    queryKey: agentTaskKeys.list(includeChecks),
    queryFn: () =>
      apiGet<AgentTaskSummaryDto[]>(
        includeChecks ? '/agent-tasks?includeChecks=true' : '/agent-tasks',
      ),
    // SignalR invalidates on every task change; this only covers a dropped connection.
    refetchInterval: 15_000,
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

/** Answer a Blocked delegate's question. Taking the work back is the failure mode this prevents. */
export function useReplyToAgentTask() {
  return useTaskMutation(({ id, message }: { id: string; message: string }) =>
    apiPost<AgentTaskSummaryDto>(`/agent-tasks/${id}/reply`, { message }),
  )
}
