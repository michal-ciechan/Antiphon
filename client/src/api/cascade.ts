import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'

// --- Cascade types ---

export type CascadeAction = 'UpdateFromDiff' | 'Regenerate' | 'KeepAsIs'

export interface AffectedStageDto {
  stageId: string
  stageName: string
  stageOrder: number
  currentVersion: number
  reason: string
  defaultAction: CascadeAction
}

export interface GoBackRequest {
  targetStageId: string
}

export interface GoBackResponse {
  affectedStages: AffectedStageDto[]
}

export interface CascadeDecision {
  stageId: string
  action: CascadeAction
}

export interface CascadeRequest {
  decisions: CascadeDecision[]
}

// --- Cascade hooks ---

export function useGoBack(workflowId: string | undefined) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (targetStageId: string) =>
      apiPost<GoBackResponse>(`/workflows/${workflowId}/gates/go-back`, {
        targetStageId,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['workflows'] })
    },
  })
}

export function useAffectedStages(workflowId: string | undefined, enabled = false) {
  return useQuery({
    queryKey: ['workflow', workflowId, 'cascade', 'affected'],
    queryFn: () =>
      apiGet<AffectedStageDto[]>(`/workflows/${workflowId}/cascade/affected`),
    enabled: !!workflowId && enabled,
  })
}

export function useSubmitCascade(workflowId: string | undefined) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (decisions: CascadeDecision[]) =>
      apiPost<void>(`/workflows/${workflowId}/cascade`, { decisions }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['workflows'] })
      queryClient.invalidateQueries({
        queryKey: ['workflow', workflowId, 'cascade'],
      })
    },
  })
}
