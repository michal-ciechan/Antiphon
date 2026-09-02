import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentModelLevel } from './agents'
import type { AgentKind, CardWorkflowRunStatus } from './boards'
import type { AgentTaskRole, AgentTaskStatus } from './agentTasks'

/**
 * The home-rail projection (CARD-0002): one list of board Cards and unbound delegations.
 * Bound tasks nest as a card's worker line and are never their own item. There is no
 * question field — the rail reads that from `GET /api/attention`.
 */

export type HomeTaskSource = 'Card' | 'Delegation'

/** Numeric order on the server is display order. */
export type HomeTaskGroup = 'NeedsHuman' | 'Running' | 'Review' | 'Next' | 'Done'

export type HomeTaskHumanReason = 'Decision' | 'Question' | 'Gate' | 'Review'

export interface HomeTaskWorkerDto {
  taskId: string
  shortId: string
  role: AgentTaskRole
  status: AgentTaskStatus
  agentKind: AgentKind
  modelLevel: AgentModelLevel
  agentId: string | null
  /** Denormalised snapshot — survives ephemeral-agent deletion. */
  agentName: string | null
  agentSessionId: string | null
  costUsd: number
  dispatchedAt: string | null
  completedAt: string | null
}

export interface HomeTaskItemDto {
  /** `card:{id:N}` or `task:{id:N}` — stable React key. */
  key: string
  source: HomeTaskSource
  id: string
  /** CARD-nnnn or the 8-char task short id. */
  identifier: string
  title: string
  /** Card close verdict. Null for delegations and open cards. */
  terminalReason: string | null
  group: HomeTaskGroup
  /** Native status name verbatim, never remapped. */
  state: string
  humanReason: HomeTaskHumanReason | null
  /**
   * Workflow stage name, else the newest bound task's role (cards); the task's own role
   * (delegations). Null only for a card that has never had a bound task.
   */
  stage: string | null
  workflowRunStatus: CardWorkflowRunStatus | null
  priority: number | null
  boardId: string | null
  worker: HomeTaskWorkerDto | null
  ownerAgentId: string | null
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  escalatedFrom: AgentModelLevel | null
  role: AgentTaskRole | null
  costUsd: number | null
  agentId: string | null
  agentName: string | null
  agentSessionId: string | null
  readAt: string | null
  deliverablePath: string | null
  deliverableRef: string | null
  workingDirectory: string | null
  repoPath: string | null
  worktreePath: string | null
  createdAt: string
  startedAt: string | null
  updatedAt: string
  completedAt: string | null
}

export interface HomeTasksDto {
  generatedAt: string
  items: HomeTaskItemDto[]
}

export const homeTaskKeys = {
  all: ['homeTasks'] as const,
  list: ['homeTasks'] as const,
}

/**
 * The 15s poll matches `useAgentTasks` / `useAttention`: SignalR already invalidates this key
 * (`CardChanged`, `BoardChanged`, `AgentTaskChanged`, `RunAttemptChanged`, `SessionFinished`,
 * `AgentChanged`, `AgentQueueChanged`), so the interval only covers a dropped connection.
 */
export function useHomeTasks(enabled = true) {
  return useQuery({
    queryKey: homeTaskKeys.list,
    queryFn: () => apiGet<HomeTasksDto>('/home/tasks'),
    refetchInterval: 15_000,
    enabled,
  })
}
