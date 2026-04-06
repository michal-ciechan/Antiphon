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
  workflowTemplateId: string | null
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

export function useModelRoutingsByTemplate(templateId: string | undefined) {
  return useQuery({
    queryKey: [...ROUTING_KEY, templateId],
    queryFn: () => apiGet<ModelRoutingDto[]>(`/settings/templates/${templateId}/model-routing`),
    enabled: !!templateId,
  })
}

export function useCreateModelRouting(templateId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateModelRoutingRequest) =>
      apiPost<ModelRoutingDto>(`/settings/templates/${templateId}/model-routing`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...ROUTING_KEY, templateId] })
    },
  })
}

export function useUpdateModelRouting(templateId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateModelRoutingRequest }) =>
      apiPut<ModelRoutingDto>(`/settings/templates/model-routing/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...ROUTING_KEY, templateId] })
    },
  })
}

export function useDeleteModelRouting(templateId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/settings/templates/model-routing/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...ROUTING_KEY, templateId] })
    },
  })
}

// --- Template Group types ---

export interface TemplateGroupDto {
  id: string
  name: string
  description: string
  isBuiltIn: boolean
  templateCount: number
  createdAt: string
  updatedAt: string
}

export interface CreateTemplateGroupRequest {
  name: string
  description: string
}

export interface UpdateTemplateGroupRequest {
  name: string
  description: string
}

// --- Stage Definition types ---

export interface StageDefinitionDto {
  name: string
  executorType: string
  modelName: string
  gateRequired: boolean
  systemPrompt: string
}

// --- Template Group hooks ---

const TEMPLATE_GROUPS_KEY = ['template-groups'] as const

export function useTemplateGroups() {
  return useQuery({
    queryKey: TEMPLATE_GROUPS_KEY,
    queryFn: () => apiGet<TemplateGroupDto[]>('/settings/template-groups'),
  })
}

export function useTemplateGroup(id: string | undefined) {
  return useQuery({
    queryKey: [...TEMPLATE_GROUPS_KEY, id],
    queryFn: () => apiGet<TemplateGroupDto>(`/settings/template-groups/${id}`),
    enabled: !!id,
  })
}

export function useCreateTemplateGroup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateTemplateGroupRequest) =>
      apiPost<TemplateGroupDto>('/settings/template-groups', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TEMPLATE_GROUPS_KEY })
    },
  })
}

export function useUpdateTemplateGroup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateTemplateGroupRequest }) =>
      apiPut<TemplateGroupDto>(`/settings/template-groups/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TEMPLATE_GROUPS_KEY })
    },
  })
}

export function useDeleteTemplateGroup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/settings/template-groups/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TEMPLATE_GROUPS_KEY })
    },
  })
}

// --- Template Stage hooks ---

export function useTemplateStages(templateId: string | null) {
  return useQuery({
    queryKey: ['template', templateId, 'stages'],
    queryFn: () => apiGet<StageDefinitionDto[]>(`/settings/templates/${templateId}/stages`),
    enabled: templateId !== null,
  })
}

// --- Workflow Template types ---

export interface WorkflowTemplateDto {
  id: string
  name: string
  description: string
  yamlDefinition: string
  isBuiltIn: boolean
  templateGroupId: string | null
  templateGroupName: string | null
  selectableStages: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateWorkflowTemplateRequest {
  name: string
  description: string
  yamlDefinition: string
  templateGroupId?: string | null
}

export interface UpdateWorkflowTemplateRequest {
  name: string
  description: string
  yamlDefinition: string
  templateGroupId?: string | null
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
