import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPut } from './client'
import type { AgentKind } from './boards'

export interface RunnerKindDefaultDto {
  agentKind: AgentKind
  runnerId: string
  inheritedRunnerId: string | null
  source: string
}

export interface RunnerDefaultsDto {
  revision: number
  globalRunnerId: string | null
  kindDefaults: RunnerKindDefaultDto[]
  updatedAt: string
  lastReason: string | null
  lastProvenance: string | null
  lastCallerTaskId: string | null
  supportedKinds: string[]
  unresolvedReferences: string[]
}

export interface PutRunnerKindDefault {
  agentKind: AgentKind
  runnerId: string
}

export interface PutRunnerDefaultsRequest {
  expectedRevision: number
  globalRunnerId: string | null
  kindDefaults: PutRunnerKindDefault[]
  reason: string
  provenance: 'Human' | 'Auto'
}

export interface RunnerDefaultsRevisionDto {
  revision: number
  previousRevision: number | null
  globalRunnerId: string | null
  kindDefaults: RunnerKindDefaultDto[]
  createdAt: string
  reason: string
  provenance: string
  callerTaskId: string | null
}

export interface RunnerDefaultsRevisionPageDto {
  revisions: RunnerDefaultsRevisionDto[]
  nextBeforeRevision: number | null
}

export const runnerDefaultKeys = {
  current: ['runnerDefaults'] as const,
  revisions: ['runnerDefaultRevisions'] as const,
}

export function useRunnerDefaults() {
  return useQuery({
    queryKey: runnerDefaultKeys.current,
    queryFn: () => apiGet<RunnerDefaultsDto>('/runner-defaults'),
    refetchOnWindowFocus: true,
  })
}

export function useRunnerDefaultRevisions() {
  return useQuery({
    queryKey: runnerDefaultKeys.revisions,
    queryFn: () => apiGet<RunnerDefaultsRevisionPageDto>('/runner-defaults/revisions?limit=20'),
    refetchOnWindowFocus: true,
  })
}

export function usePutRunnerDefaults() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: PutRunnerDefaultsRequest) =>
      apiPut<RunnerDefaultsDto>('/runner-defaults', request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: runnerDefaultKeys.current })
      void queryClient.invalidateQueries({ queryKey: runnerDefaultKeys.revisions })
    },
  })
}
