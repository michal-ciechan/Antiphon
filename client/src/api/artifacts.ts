import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiDelete } from './client'
import type { ArtifactDto } from '../features/workflow/types'

export interface SectionReviewDto {
	id: string
	stageExecutionId: string
	sectionPath: string
	contentHash: string
	reviewedAt: string
}

// --- Artifact query hooks ---

export function useWorkflowArtifacts(workflowId: string | undefined) {
  return useQuery({
    queryKey: ['workflow', workflowId, 'artifacts'],
    queryFn: () => apiGet<ArtifactDto[]>(`/workflows/${workflowId}/artifacts`),
    enabled: !!workflowId,
  })
}

export function useStageArtifact(
  workflowId: string | undefined,
  stageId: string | undefined,
  version?: number,
) {
  const versionParam = version != null ? `?version=${version}` : ''
  return useQuery({
    queryKey: ['workflow', workflowId, 'artifacts', stageId, version],
    queryFn: () => apiGet<ArtifactDto>(`/workflows/${workflowId}/artifacts/${stageId}${versionParam}`),
    enabled: !!workflowId && !!stageId,
  })
}

export function useSectionReviews(workflowId: string | undefined, stageExecutionId: string | undefined) {
  return useQuery({
    queryKey: ['workflow', workflowId, 'artifacts', stageExecutionId, 'section-reviews'],
    queryFn: () => apiGet<SectionReviewDto[]>(`/workflows/${workflowId}/artifacts/${stageExecutionId}/section-reviews`),
    enabled: !!workflowId && !!stageExecutionId,
  })
}

export function useMarkSectionReviewed(workflowId: string, stageExecutionId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: { sectionPath: string; contentHash: string }) =>
      apiPost<SectionReviewDto>(`/workflows/${workflowId}/artifacts/${stageExecutionId}/section-reviews`, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow', workflowId, 'artifacts', stageExecutionId, 'section-reviews'] })
    },
  })
}

export function useUnmarkSectionReviewed(workflowId: string, stageExecutionId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (sectionPath: string) =>
      apiDelete(`/workflows/${workflowId}/artifacts/${stageExecutionId}/section-reviews/${sectionPath}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow', workflowId, 'artifacts', stageExecutionId, 'section-reviews'] })
    },
  })
}
