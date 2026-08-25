import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPatch, apiPost } from './client'
import { boardKeys, type AgentKind, type AgentSessionSummaryDto } from './boards'

export type { ContextFullnessState } from './boards'

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

/**
 * How the agent writes (CARD-0060). `Normal` composes to NOTHING at launch — it is the default and
 * the migration backfill, so choosing it changes an agent's launch arguments by exactly zero bytes.
 */
export type AgentReplyStyle = 'Normal' | 'Terse' | 'Caveman' | 'Explanatory'

/**
 * Which lane hosts the interactive child (CARD-0160). `PtyHost` is the default — Herdr is opt-in
 * and Claude-only until CARD-0187.
 */
export type SessionBackend = 'PtyHost' | 'Herdr'

export const SESSION_BACKEND_OPTIONS: Array<{
  value: SessionBackend
  label: string
  description: string
}> = [
  {
    value: 'PtyHost',
    label: 'Pty host',
    description: 'Detached pty-host process — the existing default. Survives runner restarts.',
  },
  {
    value: 'Herdr',
    label: 'Herdr',
    description:
      "Session runs in a pane of the operator's herdr instance — visible and natively attachable, but it does not survive a herdr restart; an always-on agent is resumed into a new pane by supervision.",
  },
]

/** Picker options, least to most words. Normal is the default and is deliberately first. */
export const AGENT_REPLY_STYLE_OPTIONS: Array<{
  value: AgentReplyStyle
  label: string
  description: string
}> = [
  {
    value: 'Normal',
    label: 'Normal',
    description: 'No style instruction at all — the model writes the way it does by default.',
  },
  {
    value: 'Terse',
    label: 'Terse',
    description: 'Answer first, one line where one line will do. No preamble, no sign-off.',
  },
  {
    value: 'Caveman',
    label: 'Caveman',
    description: 'Short word. Drop small word. Paths, flags and code still written exactly.',
  },
  {
    value: 'Explanatory',
    label: 'Explanatory',
    description: 'Answer first, then the reasoning: alternatives, what it depends on, where it was read.',
  },
]
/**
 * Generic model capability LEVEL — each agent kind maps it to its provider's ladder at launch
 * (Claude today: Frontier→Fable, High→Opus, Medium→Sonnet, Low→Haiku; a future GPT kind would
 * map e.g. Sol=Frontier, Terra=High, Luna=Medium). Never a pinned model version.
 */
export type AgentModelLevel = 'Frontier' | 'High' | 'Medium' | 'Low'

/** Picker options in capability order; High (the Opus tier) is the default. */
export const AGENT_MODEL_LEVEL_OPTIONS: Array<{
  value: AgentModelLevel
  label: string
  description: string
}> = [
  {
    value: 'Frontier',
    label: 'Frontier',
    description:
      'The most capable model — the very hardest tasks, highest level of reasoning. Highest cost and token usage. Claude: Fable.',
  },
  {
    value: 'High',
    label: 'High (default)',
    description:
      'Frontier-grade daily driver — hard tasks, deep reasoning. Higher cost and token usage. Claude: Opus.',
  },
  {
    value: 'Medium',
    label: 'Medium',
    description: 'Balanced — strong everyday reasoning at moderate cost and token usage. Claude: Sonnet.',
  },
  {
    value: 'Low',
    label: 'Low',
    description: 'Fast and light — simple tasks and quick replies. Lowest cost and token usage. Claude: Haiku.',
  },
]
export type AgentStatus = 'Idle' | 'Ready' | 'Running' | 'WaitingForHumanReview' | 'Stopped' | 'Disconnected' | 'Failed'
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
  /** Generic model capability level for the agent's sessions. High (the Opus tier) is the default. */
  modelLevel: AgentModelLevel
  /**
   * Transcript-derived "mid-turn right now" for the live session. Distinct from status=Running,
   * which only means the agent was started — this is what deserves a spinner.
   */
  working: boolean
  tuiProfileId?: string | null
  modelId?: string | null
  configuredSelection?: {
    tuiProfileId: string | null
    modelId: string | null
    profileDisplayName: string | null
    profileRevision: number | null
  } | null
  liveSessionSelection?: {
    tuiProfileRevisionId: string | null
    effectiveModelId: string | null
    pendingRestart: boolean
  } | null
  /** How the agent writes. Absent on an older server response — treat as 'Normal'. */
  replyStyle?: AgentReplyStyle
  /**
   * Which lane hosts the interactive child (CARD-0160). Absent on an older server — treat as
   * 'PtyHost'.
   */
  sessionBackend?: SessionBackend
  /**
   * The live session was launched with instruction bundles the repo has since moved on from — an
   * edited bundle file, an attachment added or removed, a changed reply style (CARD-0058).
   * Informational only: the agent picks the new ones up at its next launch and nothing forces that.
   */
  bundlesOutOfDate?: boolean
  /**
   * Per-agent auto-compact overrides (CARD-0082). Null / omitted = use the installation
   * ContextCompactionSettings value.
   */
  autoCompactEnabled?: boolean | null
  autoCompactIdleMinutes?: number | null
  autoCompactContextPercent?: number | null
  /** Per-agent launch environment. Values can refer to stored API keys as {{key:NAME}}. */
  launchEnv?: Record<string, string> | null
  /**
   * WHICH AGENT PROGRAM this row is (CARD-0139). With a tuiProfileId attached this equals that
   * profile's kind; without one it is the row's own truth. Absent on an older server response.
   */
  kind?: AgentKind
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
  | 'DeliveryVerificationFailed'
  | 'ContextCompacted'
  | 'DeliveryUnverified'

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
  /**
   * The instruction bundles this agent's NEXT launch will carry, as `"style-caveman v1a2b3c4d"`.
   * Read-only and recomputed server-side per request: nothing composed is stored anywhere, so this
   * list cannot drift from what the repo's bundle files currently say.
   */
  composedBundles?: string[] | null
  /**
   * The bundle KEYS attached to this agent, in composition order — what the settings modal's picker
   * round-trips. Distinct from `composedBundles`, which is the whole composition (attachments AND
   * the reply-style block) stamped with versions.
   */
  attachedBundleKeys?: string[] | null
}

/**
 * One attachable bundle from the catalog (CARD-0058). The catalog is CODE — markdown files under
 * `server/Bundles/`, versioned by content hash — so this is read-only: the only thing an operator
 * chooses is which agent carries which key. Reply-style bundles are deliberately absent; the reply
 * style dropdown already picks one.
 */
export interface InstructionBundleDto {
  key: string
  /** Content hash of the bundle file. Changes when the file changes; there is nothing to bump. */
  version: string
  /** `"board-api v1a2b3c4d"` — the same string that rides the composed output and the drift stamp. */
  stamp: string
  /** The bundle's opening sentence, for the picker. */
  summary: string
  chars: number
}

export interface CreateAgentRequest {
  name: string
  workingDirectory: string
  details?: string | null
  defaultWorkflowTemplateId?: string | null
  assignmentPolicy?: AgentAssignmentPolicy
  createWorkingDirectory?: boolean
  /** Omit/null = High (the default level — the Opus tier — unless picked otherwise). */
  modelLevel?: AgentModelLevel | null
  tuiProfileId?: string | null
  modelId?: string | null
  /** Omit = Normal. Create deliberately still cannot set systemPromptAppend. */
  replyStyle?: AgentReplyStyle
  /** Omit = PtyHost (CARD-0160). */
  sessionBackend?: SessionBackend
  /** Supervised from birth: auto-started at boot, auto-restarted on crash. */
  alwaysOn?: boolean
  remoteControlEnabled?: boolean
  /** Null / omit = use the installation ContextCompactionSettings value. */
  autoCompactEnabled?: boolean | null
  autoCompactIdleMinutes?: number | null
  autoCompactContextPercent?: number | null
}

export interface UpdateAgentRequest {
  name: string
  workingDirectory: string
  details?: string | null
  defaultWorkflowTemplateId?: string | null
  assignmentPolicy: AgentAssignmentPolicy
  /** Omit/null = leave unchanged. Every agent keeps a default board — it can be moved, not cleared. */
  boardId?: string | null
  /** Omit/null = leave unchanged. */
  alwaysOn?: boolean | null
  remoteControlEnabled?: boolean | null
  /** Omit/null = leave unchanged; empty string = clear. */
  systemPromptAppend?: string | null
  /** Omit/null = leave unchanged. */
  modelLevel?: AgentModelLevel | null
  /** When set, also applies modelId (null clears exact model). */
  tuiProfileId?: string | null
  modelId?: string | null
  /** Omit/null = leave unchanged, so an older client cannot reset a chosen style to Normal. */
  replyStyle?: AgentReplyStyle | null
  /** Omit/null = leave unchanged (CARD-0160), so an older client cannot reset a chosen backend. */
  sessionBackend?: SessionBackend | null
  /**
   * The bundles this agent carries on top of what its role implies (CARD-0058). Omit/null = leave
   * unchanged, same reason as replyStyle — an older client must not silently detach everything. An
   * EMPTY array is the explicit "detach all". Order is composition order.
   */
  bundleKeys?: string[] | null
  /**
   * Per-agent auto-compact overrides (CARD-0082). Always sent from the settings modal, including
   * JSON null for "use the installation default".
   */
  autoCompactEnabled?: boolean | null
  autoCompactIdleMinutes?: number | null
  autoCompactContextPercent?: number | null
  /** Null/omitted leaves existing values unchanged; an empty object clears all launch environment. */
  launchEnv?: Record<string, string> | null
  /**
   * CARD-0139. Omit/null = leave unchanged. Assert-or-set: with a tuiProfileId attached this is
   * checked against the profile's kind (agreement is a no-op, disagreement is 409), not written.
   * Written only for a non-pool agent with no profile. Pool delegates always 409.
   */
  kind?: AgentKind | null
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
  /**
   * Bypass the subscription-quota launch gate. A 409 `subscription_quota_low` is the
   * refusal; re-send with this true to launch anyway. No UI wires it in CARD-0136.
   */
  ignoreSubscriptionQuota?: boolean
}

export const agentKeys = {
  definitions: ['agents', 'definitions'] as const,
  bundles: ['agents', 'bundles'] as const,
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

/**
 * The bundles an operator may attach to an agent. The catalog only changes when the server does, so
 * this is fetched once and never polled — unlike the agent list, nothing here moves on its own.
 */
export function useInstructionBundles(enabled = true) {
  return useQuery({
    queryKey: agentKeys.bundles,
    queryFn: () => apiGet<InstructionBundleDto[]>('/agents/bundles'),
    staleTime: Infinity,
    enabled,
  })
}

export function useAgentList() {
  return useQuery({
    queryKey: agentKeys.all,
    queryFn: () => apiGet<AgentSummaryDto[]>('/agents'),
    // SignalR covers turn END (SessionFinished invalidates this key) but nothing fires on turn
    // START — poll so the cards' Working spinner appears without a manual refresh.
    refetchInterval: 5000,
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
    // Same reasoning as useAgentList — the detail header renders the Working spinner too.
    refetchInterval: 5000,
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
