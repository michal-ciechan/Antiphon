import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPut } from './client'

export interface KeepSixOrchestratorDto {
  agentId: string
  name: string
  enabled: boolean
  target: number
  revision: number
  reason: string | null
  provenance: string | null
  updatedAt: string | null
  enabledAt: string | null
  holdReason: string | null
  holdUntil: string | null
  lastReminderEnqueuedAt: string | null
  outstandingMessageId: string | null
  lastAcknowledgedAt: string | null
  countedOpen: number | null
  effectiveTarget: number | null
  dispatchableBacklog: number | null
  shortfall: number | null
  topCards: string[] | null
  lastDispatchAt: string | null
  lastCompletionAt: string | null
  badge: string | null
}

export interface PutKeepSixRequest {
  expectedRevision: number
  enabled: boolean
  target: number
  reason: string
  provenance: 'Human'
}

export const keepSixKeys = {
  list: ['orchestrators', 'keep-six'] as const,
}

export const KEEP_SIX_OPERATOR_REASON = 'operator toggle'

export function useKeepSix() {
  return useQuery({
    queryKey: keepSixKeys.list,
    queryFn: () => apiGet<KeepSixOrchestratorDto[]>('/orchestrators/keep-six'),
    refetchInterval: 5_000,
  })
}

export function usePutKeepSix() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: string; body: PutKeepSixRequest }) =>
      apiPut<KeepSixOrchestratorDto>(`/orchestrators/${agentId}/keep-six`, body),
    onSettled: () => queryClient.invalidateQueries({ queryKey: keepSixKeys.list }),
  })
}
