import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'
import { boardKeys, type AgentKind } from './boards'

export interface AgentRegistryDto {
  defaultDefinition: string
  definitions: AgentDefinitionDto[]
}

export interface AgentDefinitionDto {
  name: string
  kind: AgentKind
  isDefault: boolean
}

export type AgentAssignmentPolicy = 'AutoPick' | 'ManualConfirm' | 'Paused'
export type AgentStatus = 'Idle' | 'Ready' | 'Working' | 'WaitingForHumanReview' | 'Stopped' | 'Disconnected' | 'Failed'
export type CardWorkflowRunStatus = 'Queued' | 'Running' | 'WaitingForHumanReview' | 'Completed' | 'Failed' | 'Canceled'

export interface AgentSummaryDto {
  id: string
  name: string
  slug: string
  workingDirectory: string
  details: string
  defaultWorkflowTemplateId: string | null
  defaultWorkflowTemplateName: string | null
  assignmentPolicy: AgentAssignmentPolicy
  status: AgentStatus
  persistentSessionId: string | null
  currentCardId: string | null
  queueLength: number
  createdAt: string
  updatedAt: string
}

export interface AgentQueueCardDto {
  cardId: string
  boardId: string
  boardName: string
  identifier: string
  title: string
  priority: number
  queuePosition: number
  activeWorkflowRunId: string | null
  workflowStatus: CardWorkflowRunStatus | null
  currentStageName: string | null
}

export interface AgentDetailDto extends AgentSummaryDto {
  queue: AgentQueueCardDto[]
}

export interface CreateAgentRequest {
  name: string
  workingDirectory: string
  details?: string | null
  defaultWorkflowTemplateId?: string | null
  assignmentPolicy?: AgentAssignmentPolicy
}

export interface AssignAgentCardRequest {
  cardId: string
}

export const agentKeys = {
  definitions: ['agents', 'definitions'] as const,
  all: ['agents', 'list'] as const,
  detail: (id: string) => ['agents', 'detail', id] as const,
  queue: (id: string) => ['agents', 'queue', id] as const,
}

export function useAgentDefinitions() {
  return useQuery({
    queryKey: agentKeys.definitions,
    queryFn: () => apiGet<AgentRegistryDto>('/agents/definitions'),
  })
}

export function useAgentList() {
  return useQuery({
    queryKey: agentKeys.all,
    queryFn: () => apiGet<AgentSummaryDto[]>('/agents'),
  })
}

export function useAgent(id: string | null) {
  return useQuery({
    queryKey: id ? agentKeys.detail(id) : ['agents', 'detail', 'missing'],
    queryFn: () => {
      if (!id) {
        throw new Error('Agent id is required')
      }
      return apiGet<AgentDetailDto>(`/agents/${id}`)
    },
    enabled: !!id,
  })
}

export function useCreateAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateAgentRequest) => apiPost<AgentDetailDto>('/agents', request),
    onSuccess: (agent) => {
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
      queryClient.setQueryData(agentKeys.detail(agent.id), agent)
    },
  })
}

export function useAssignAgentCard(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: AssignAgentCardRequest) => apiPost<AgentDetailDto>(`/agents/${agentId}/queue`, request),
    onSuccess: (agent) => {
      queryClient.setQueryData(agentKeys.detail(agentId), agent)
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
      queryClient.invalidateQueries({ queryKey: agentKeys.queue(agentId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
      for (const boardId of new Set(agent.queue.map((card) => card.boardId))) {
        queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      }
    },
  })
}
