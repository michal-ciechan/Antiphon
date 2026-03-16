import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiPut, apiDelete } from './client'

export interface WorkflowTemplateDto {
  id: string
  name: string
  description: string
  yamlDefinition: string
  isBuiltIn: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateWorkflowTemplateRequest {
  name: string
  description: string
  yamlDefinition: string
}

export interface UpdateWorkflowTemplateRequest {
  name: string
  description: string
  yamlDefinition: string
}

const TEMPLATES_KEY = ['settings', 'templates'] as const

export function useTemplates() {
  return useQuery({
    queryKey: TEMPLATES_KEY,
    queryFn: () => apiGet<WorkflowTemplateDto[]>('/settings/templates'),
  })
}

export function useTemplate(id: string | undefined) {
  return useQuery({
    queryKey: [...TEMPLATES_KEY, id],
    queryFn: () => apiGet<WorkflowTemplateDto>(`/settings/templates/${id}`),
    enabled: !!id,
  })
}

export function useCreateTemplate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateWorkflowTemplateRequest) =>
      apiPost<WorkflowTemplateDto>('/settings/templates', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY })
    },
  })
}

export function useUpdateTemplate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateWorkflowTemplateRequest }) =>
      apiPut<WorkflowTemplateDto>(`/settings/templates/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY })
    },
  })
}

export function useDeleteTemplate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/settings/templates/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY })
    },
  })
}
