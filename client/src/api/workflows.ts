import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'

// --- Workflow types ---

export type WorkflowStatus =
  | 'Created'
  | 'Running'
  | 'Paused'
  | 'GateWaiting'
  | 'Completed'
  | 'Failed'
  | 'Abandoned'

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
