import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPatch, apiPost } from './client'
import { boardKeys, type AgentKind, type AgentSessionSummaryDto } from './boards'

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
  boardId: string | null
  boardName: string | null
  queueLength: number
  createdAt: string
  updatedAt: string
  /** The agent's persistent session when currently live (Starting/Running/Stopping), else null. */
  liveSession: AgentSessionSummaryDto | null
  /** Supervised: auto-started at boot, auto-restarted on crash (never-give-up backoff ladder). */
  alwaysOn: boolean
  /** Remote control is part of the agent's setup: every start path arms /remote-control. */
  remoteControlEnabled: boolean
  /** Present for always-on agents with supervision history. */
  supervision: AgentSupervisionDto | null
  /**
   * Channel preamble template appended to the system prompt on every interactive launch
   * (--append-system-prompt). Null = none; also disables bootstrap/restart/recovery notes.
   */
  systemPromptAppend: string | null
}

export interface AgentSupervisionDto {
  suspended: boolean
  consecutiveFailures: number
  nextRestartAt: string | null
  lastEscalationTier: number
}

export type AgentIncidentKind =
  | 'Crash'
  | 'StartFailure'
  | 'RestartScheduled'
  | 'Recovered'
  | 'BackoffEscalated'
  | 'SuspendedByUser'
  | 'ResumedByUser'
  | 'RcDegraded'
  | 'RcReArmed'
  | 'RcRestart'
  | 'LivenessProbeFailed'

export type AlertSeverity = 'Info' | 'Warning' | 'Error' | 'Critical'

export interface AgentIncidentDto {
  id: string
  agentId: string
  sessionId: string | null
  kind: AgentIncidentKind
  severity: AlertSeverity
  message: string
  exitCode: number | null
  failureReason: string | null
  createdAt: string
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
  createWorkingDirectory?: boolean
}

export interface UpdateAgentRequest {
  name: string
  workingDirectory: string
  details?: string | null
  defaultWorkflowTemplateId?: string | null
  assignmentPolicy: AgentAssignmentPolicy
  boardId?: string | null
  /** Omit/null = leave unchanged. */
  alwaysOn?: boolean | null
  remoteControlEnabled?: boolean | null
  /** Omit/null = leave unchanged; empty string = clear. */
  systemPromptAppend?: string | null
}

export interface DraftAgentRequest {
  description: string
}

export interface DraftAgentResponse {
  name: string
  workingDirectory: string
  details: string
  assignmentPolicy: AgentAssignmentPolicy
  usedAi: boolean
}

export interface AssignAgentCardRequest {
  cardId: string
}

export interface StartAgentRequest {
  /** Omit = use the agent's persisted remoteControlEnabled setting. */
  remoteControl?: boolean | null
  /** Force a brand-new conversation. By default an interactive start resumes the agent's previous Claude session. */
  fresh?: boolean
}

export const agentKeys = {
  definitions: ['agents', 'definitions'] as const,
  all: ['agents', 'list'] as const,
  detail: (id: string) => ['agents', 'detail', id] as const,
  queue: (id: string) => ['agents', 'queue', id] as const,
  incidents: (id: string) => ['agents', 'incidents', id] as const,
}

export function useAgentIncidents(id: string | null, enabled = true) {
  return useQuery({
    queryKey: id ? agentKeys.incidents(id) : ['agents', 'incidents', 'missing'],
    queryFn: () => {
      if (!id) {
        throw new Error('Agent id is required')
      }
      return apiGet<AgentIncidentDto[]>(`/agents/${id}/incidents?take=50`)
    },
    enabled: enabled && !!id,
  })
}

export interface PreamblePresetDto {
  template: string
}

/** The channel-preamble preset template for a provider (default: telegram). */
export function fetchPreamblePreset(provider = 'telegram') {
  return apiGet<PreamblePresetDto>(`/agents/preamble-preset?provider=${encodeURIComponent(provider)}`)
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
      // Creating an agent also creates its board (and possibly a project), so refresh boards.
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useUpdateAgent(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateAgentRequest) => apiPatch<AgentDetailDto>(`/agents/${id}`, request),
    onSuccess: (agent) => {
      queryClient.setQueryData(agentKeys.detail(id), agent)
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
    },
  })
}

export function useDeleteAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/agents/${id}`),
    onSuccess: (_data, id) => {
      queryClient.removeQueries({ queryKey: agentKeys.detail(id) })
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
      // A deleted agent releases its cards, so refresh boards too.
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useStartAgent(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: StartAgentRequest = {}) => apiPost<AgentDetailDto>(`/agents/${agentId}/start`, request),
    onSuccess: (agent) => {
      queryClient.setQueryData(agentKeys.detail(agentId), agent)
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
      // Starting boots a session and may move a card into an active column.
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useStopAgent(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<AgentDetailDto>(`/agents/${agentId}/stop`, {}),
    onSuccess: (agent) => {
      queryClient.setQueryData(agentKeys.detail(agentId), agent)
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useDraftAgent() {
  return useMutation({
    mutationFn: (request: DraftAgentRequest) => apiPost<DraftAgentResponse>('/agents/draft', request),
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
