import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'

export interface OrchestratorStateTotalsDto {
  tokensIn: number
  tokensOut: number
  costUsd: number
  activeRuntimeSeconds: number
}

export interface OrchestratorStateLimitsDto {
  pollIntervalSeconds: number
  maxDispatchesPerTick: number
  failureBackoffBaseMs: number
  failureBackoffMaxMs: number
  startingSessionGraceSeconds: number
}

export interface OrchestratorRunningSessionDto {
  sessionId: string
  cardId: string
  cardIdentifier: string
  cardTitle: string
  boardId: string
  boardName: string
  definitionName: string
  agentKind: string
  status: string
  runAttemptId: string | null
  turnCount: number
  attemptNumber: number | null
  phase: string | null
  startedAt: string
  lastSeenAt: string
  lastEventAt: string | null
  runtimeSeconds: number
  tokensIn: number
  tokensOut: number
  costUsd: number
  live: boolean
  lastSequence: number
}

export interface OrchestratorRetryQueueItemDto {
  cardId: string
  cardIdentifier: string
  cardTitle: string
  boardId: string
  boardName: string
  attemptCount: number
  maxAttempts: number
  nextRetryAt: string | null
  lastAttemptAt: string | null
  lastError: string | null
}

export interface OrchestratorStateDto {
  paused: boolean
  enabled: boolean
  generatedAt: string
  runningSessions: number
  retryQueueLength: number
  totals: OrchestratorStateTotalsDto
  limits: OrchestratorStateLimitsDto
  running: OrchestratorRunningSessionDto[]
  retryQueue: OrchestratorRetryQueueItemDto[]
}

export interface OrchestratorPauseResult {
  paused: boolean
}

export interface OrchestratorTickResult {
  paused: boolean
  eligibleCards: number
  dispatched: number
  reconciled: number
  skippedGlobalConcurrency: number
  skippedColumnConcurrency: number
  claimedElsewhere: number
  failures: number
}

export const orchestratorKeys = {
  state: ['orchestrator', 'state'] as const,
}

export function useOrchestratorState() {
  return useQuery({
    queryKey: orchestratorKeys.state,
    queryFn: () => apiGet<OrchestratorStateDto>('/orchestrator/state'),
    refetchInterval: 5_000,
  })
}

export function usePauseOrchestrator() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<OrchestratorPauseResult>('/orchestrator/pause', {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: orchestratorKeys.state }),
  })
}

export function useResumeOrchestrator() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<OrchestratorPauseResult>('/orchestrator/resume', {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: orchestratorKeys.state }),
  })
}

export function useRunOrchestratorTick() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<OrchestratorTickResult>('/orchestrator/tick', {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: orchestratorKeys.state }),
  })
}
