import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPut } from './client'
import { attentionKeys } from './attention'

export type ModelAvailabilitySource = 'AutoDetected' | 'Manual'

export interface ModelAvailabilityHoldDto {
  id: string
  kind: string
  modelAlias: string
  source: ModelAvailabilitySource
  disabledUntil: string | null
  hitAt: string
  reason: string
  rawText: string | null
  sourceSessionId: string | null
  sourceTaskId: string | null
}

export interface ModelAvailabilityDto {
  holds: ModelAvailabilityHoldDto[]
  available: string[]
}

export interface PutModelAvailabilityRequest {
  disabledUntil?: string | null
  reason?: string | null
}

export const modelAvailabilityKeys = {
  all: ['model-availability'] as const,
}

function holdPath(kind: string, alias: string): string {
  return `/model-availability/${encodeURIComponent(kind)}/${encodeURIComponent(alias)}`
}

export function useModelAvailability() {
  return useQuery({
    queryKey: modelAvailabilityKeys.all,
    queryFn: () => apiGet<ModelAvailabilityDto>('/model-availability'),
    refetchInterval: 15_000,
  })
}

export function usePutModelAvailabilityHold() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      kind,
      alias,
      body,
    }: {
      kind: string
      alias: string
      body: PutModelAvailabilityRequest
    }) => apiPut<ModelAvailabilityHoldDto>(holdPath(kind, alias), body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: modelAvailabilityKeys.all })
      void queryClient.invalidateQueries({ queryKey: attentionKeys.all })
    },
  })
}

export function useClearModelAvailabilityHold() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ kind, alias }: { kind: string; alias: string }) =>
      clearModelAvailabilityHold(kind, alias),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: modelAvailabilityKeys.all })
      void queryClient.invalidateQueries({ queryKey: attentionKeys.all })
    },
  })
}

export function clearModelAvailabilityHold(kind: string, alias: string): Promise<void> {
  return apiDelete(holdPath(kind, alias))
}
