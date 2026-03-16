import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiPut, apiDelete } from './client'

// --- LLM Provider types ---

export type ProviderType = 'Anthropic' | 'OpenAI' | 'Ollama'

export interface LlmProviderDto {
  id: string
  name: string
  providerType: number
  apiKeyMasked: string
  baseUrl: string
  isEnabled: boolean
  defaultModel: string
  createdAt: string
  updatedAt: string
}

export interface CreateLlmProviderRequest {
  name: string
  providerType: number
  apiKey: string
  baseUrl: string
  isEnabled: boolean
  defaultModel: string
}

export interface UpdateLlmProviderRequest {
  name: string
  providerType: number
  apiKey?: string
  baseUrl: string
  isEnabled: boolean
  defaultModel: string
}

export interface TestProviderResult {
  success: boolean
  message: string
}

export interface ModelRoutingDto {
  id: string
  stageName: string
  modelName: string
  providerId: string
  createdAt: string
}

export interface CreateModelRoutingRequest {
  stageName: string
  modelName: string
  providerId: string
}

export interface UpdateModelRoutingRequest {
  stageName: string
  modelName: string
  providerId: string
}

// --- LLM Provider hooks ---

const PROVIDERS_KEY = ['settings', 'providers'] as const
const ROUTING_KEY = ['settings', 'model-routing'] as const

export function useProviders() {
  return useQuery({
    queryKey: PROVIDERS_KEY,
    queryFn: () => apiGet<LlmProviderDto[]>('/settings/providers'),
  })
}

export function useProvider(id: string | undefined) {
  return useQuery({
    queryKey: [...PROVIDERS_KEY, id],
    queryFn: () => apiGet<LlmProviderDto>(`/settings/providers/${id}`),
    enabled: !!id,
  })
}

export function useCreateProvider() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateLlmProviderRequest) =>
      apiPost<LlmProviderDto>('/settings/providers', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROVIDERS_KEY })
    },
  })
}

export function useUpdateProvider() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateLlmProviderRequest }) =>
      apiPut<LlmProviderDto>(`/settings/providers/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROVIDERS_KEY })
    },
  })
}

export function useDeleteProvider() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/settings/providers/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROVIDERS_KEY })
    },
  })
}

export function useTestProvider() {
  return useMutation({
    mutationFn: (id: string) =>
      apiPost<TestProviderResult>(`/settings/providers/${id}/test`, {}),
  })
}

// --- Model Routing hooks ---

export function useModelRoutings() {
  return useQuery({
    queryKey: ROUTING_KEY,
    queryFn: () => apiGet<ModelRoutingDto[]>('/settings/model-routing'),
  })
}

export function useCreateModelRouting() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateModelRoutingRequest) =>
      apiPost<ModelRoutingDto>('/settings/model-routing', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROUTING_KEY })
    },
  })
}

export function useUpdateModelRouting() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateModelRoutingRequest }) =>
      apiPut<ModelRoutingDto>(`/settings/model-routing/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROUTING_KEY })
    },
  })
}

export function useDeleteModelRouting() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/settings/model-routing/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROUTING_KEY })
    },
  })
}

// --- Workflow Template types ---

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
