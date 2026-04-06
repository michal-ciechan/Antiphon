import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { ArtifactDto } from '../features/workflow/types'

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
