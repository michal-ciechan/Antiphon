import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiDelete } from './client'

// --- Workflow types ---

export type WorkflowStatus =
  | 'Created'
  | 'Running'
  | 'Paused'
  | 'GateWaiting'
  | 'Completed'
  | 'Failed'
  | 'Abandoned'
  | 'CascadeWaiting'

export type StageStatus = 'Pending' | 'Running' | 'Completed' | 'Failed'

export interface WorkflowDto {
  id: string
  name: string
  description: string
  status: WorkflowStatus
  currentStageName: string | null
  templateId: string
  templateName: string
  projectId: string
  projectName: string
  featureName: string | null
  stageCount: number
  completedStageCount: number
  availableTransitions: WorkflowStatus[]
  createdAt: string
  updatedAt: string
}

export interface StageDto {
  id: string
  name: string
  stageOrder: number
  status: StageStatus
  gateRequired: boolean
  currentVersion: number
}

export interface WorkflowDetailDto extends WorkflowDto {
  stages: StageDto[]
}

export interface CreateWorkflowRequest {
  templateId: string
  projectId: string
  name?: string
  initialContext?: string
  stageModelOverrides?: Record<string, string>
  featureName?: string
  selectedStages?: string[]
}

export interface FeatureStatusDto {
  featureName: string
  completedStages: string[]
}

export interface WorkflowDeletePeerDto {
  id: string
  name: string
}

export interface WorkflowDeleteInfoDto {
  branchName: string
  peerWorkflows: WorkflowDeletePeerDto[]
}

// --- Workflow hooks ---

const WORKFLOWS_KEY = ['workflows'] as const

export function useWorkflows() {
  return useQuery({
    queryKey: WORKFLOWS_KEY,
    queryFn: () => apiGet<WorkflowDto[]>('/workflows'),
  })
}

export function useWorkflow(id: string | undefined) {
  return useQuery({
    queryKey: ['workflow', id],
    queryFn: () => apiGet<WorkflowDetailDto>(`/workflows/${id}`),
    enabled: !!id,
  })
}

export function useCreateWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateWorkflowRequest) =>
      apiPost<{ id: string }>('/workflows', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: WORKFLOWS_KEY })
    },
  })
}

export function usePauseWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiPost<void>(`/workflows/${id}/pause`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: WORKFLOWS_KEY })
    },
  })
}

export function useResumeWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiPost<void>(`/workflows/${id}/resume`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: WORKFLOWS_KEY })
    },
  })
}

export function useAbandonWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiPost<void>(`/workflows/${id}/abandon`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: WORKFLOWS_KEY })
    },
  })
}

export function useWorkflowDeleteInfo(id: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['workflow-delete-info', id],
    queryFn: () => apiGet<WorkflowDeleteInfoDto>(`/workflows/${id}/delete-info`),
    enabled: !!id && enabled,
  })
}

export function useDeleteWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, deleteBranch, branchName }: { id: string; deleteBranch?: boolean; branchName?: string }) => {
      const params = new URLSearchParams()
      if (deleteBranch) params.set('deleteBranch', 'true')
      if (branchName) params.set('branchName', branchName)
      const qs = params.toString()
      return apiDelete(`/workflows/${id}${qs ? `?${qs}` : ''}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: WORKFLOWS_KEY })
    },
  })
}

export function useFeatureStatus(projectId: string | null, featureName: string | null) {
  return useQuery({
    queryKey: ['feature-status', projectId, featureName],
    queryFn: () =>
      apiGet<FeatureStatusDto>(`/projects/${projectId}/feature-status/${featureName}`),
    enabled: projectId !== null && featureName !== null && featureName.length >= 2,
  })
}
