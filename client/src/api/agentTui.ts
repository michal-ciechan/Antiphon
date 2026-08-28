import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPatch, apiPost, apiPut } from './client'

export type AgentTuiAuthenticationMode = 'WrapperManaged' | 'ManagedEnvironment'
export type AgentTuiModelSource = 'Curated' | 'Discovered' | 'Operator'
export type AgentTuiModelAvailability = 'Unverified' | 'Verified' | 'Stale' | 'Unavailable'
export type AgentTuiCapabilityState = 'Supported' | 'Unsupported' | 'Degraded' | 'Unknown'
export type AgentTuiValidationStatus =
  | 'NeverRun'
  | 'Running'
  | 'Succeeded'
  | 'Partial'
  | 'Failed'
  | 'TimedOut'
export type AgentKind = 'Raw' | 'ClaudeCode' | 'Codex' | 'OpenCode' | 'Grok'

export interface AgentTuiModelDto {
  identifier: string
  displayName: string
  family: string | null
  source: AgentTuiModelSource
  availability: AgentTuiModelAvailability
  discoveredAt: string | null
  runnerVersion: string | null
  isSuggestedDefault: boolean
}

export interface AgentTuiCapabilityDto {
  name: string
  state: AgentTuiCapabilityState
  reason: string
}

export interface AgentTuiRunnerTypeDto {
  kind: AgentKind
  displayName: string
  description: string
  defaultModelArgumentName: string | null
  authenticationModes: AgentTuiAuthenticationMode[]
  curatedModels: AgentTuiModelDto[]
  capabilities: AgentTuiCapabilityDto[]
  guidance: string
}

export interface AgentTuiSecretMetadataDto {
  name: string
  configured: boolean
  updatedAt: string | null
}

export interface AgentTuiCommandPreviewDto {
  executable: string
  arguments: string[]
  workingDirectory: string | null
}

export interface AgentTuiValidationSummaryDto {
  status: AgentTuiValidationStatus
  profileRevisionId: string | null
  isCurrentRevision: boolean
  runnerVersion: string | null
  probedAt: string | null
}

export interface AgentTuiProfileDto {
  id: string
  displayName: string
  kind: AgentKind
  isEnabled: boolean
  isDefault: boolean
  source: string
  sourceDefinitionName: string | null
  revisionId: string
  revision: number
  revisionDetails: {
    id: string
    revision: number
    executable: string
    arguments: string[]
    discoveryArguments: string[]
    versionArguments: string[]
    workingDirectory: string | null
    authenticationMode: AgentTuiAuthenticationMode
    nonSecretEnvironment: Record<string, string>
    secretEnvironmentNames: string[]
    modelArgumentName: string | null
    guidance: string
    createdAt: string
  }
  commandPreview: AgentTuiCommandPreviewDto
  secretEnvironment: AgentTuiSecretMetadataDto[]
  models: AgentTuiModelDto[]
  capabilities: AgentTuiCapabilityDto[]
  validationSummary: AgentTuiValidationSummaryDto
  createdAt: string
  updatedAt: string
}

export interface AgentTuiProfileWriteRequest {
  displayName: string
  kind: AgentKind
  isEnabled: boolean
  isDefault: boolean
  executable: string
  arguments: string[]
  discoveryArguments: string[]
  versionArguments: string[]
  workingDirectory: string | null
  authenticationMode: AgentTuiAuthenticationMode
  nonSecretEnvironment: Record<string, string>
  secretEnvironmentNames: string[]
  modelArgumentName: string | null
  guidance: string
  models: Array<{ identifier: string; displayName: string; family?: string | null; isSuggestedDefault?: boolean }>
  expectedRevision?: number | null
}

export interface AgentTuiCapabilitySnapshotDto {
  capabilities: AgentTuiCapabilityDto[]
  runnerVersion: string | null
  probedAt: string | null
}

export interface AgentTuiValidationStageDto {
  name: string
  status: 'Passed' | 'Failed' | 'Skipped' | 'Degraded'
  message: string
}

export interface AgentTuiValidationRunDto {
  id: string
  profileId: string
  profileRevisionId: string
  operation: string
  status: AgentTuiValidationStatus
  stages: AgentTuiValidationStageDto[]
  capabilities: AgentTuiCapabilityDto[]
  runnerVersion: string | null
  summary: string
  suitability: {
    interactive: boolean
    queued: boolean
    delegated: boolean
    resumable: boolean
  }
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  models?: AgentTuiModelDto[]
  cachedResultsRetained?: boolean
}

export const REMOTE_CONTROL_CAPABILITY = 'remoteControl'

export function remoteControlCapability(
  source: { capabilities: AgentTuiCapabilityDto[] } | undefined,
): AgentTuiCapabilityDto | undefined {
  return source?.capabilities.find((capability) => capability.name === REMOTE_CONTROL_CAPABILITY)
}

export const agentTuiKeys = {
  all: ['agent-tui'] as const,
  runnerTypes: () => [...agentTuiKeys.all, 'runner-types'] as const,
  profiles: () => [...agentTuiKeys.all, 'profiles'] as const,
  profile: (id: string) => [...agentTuiKeys.all, 'profile', id] as const,
  models: (id: string) => [...agentTuiKeys.all, 'models', id] as const,
  capabilities: (id: string) => [...agentTuiKeys.all, 'capabilities', id] as const,
}

export function useAgentTuiRunnerTypes() {
  return useQuery({
    queryKey: agentTuiKeys.runnerTypes(),
    queryFn: () => apiGet<AgentTuiRunnerTypeDto[]>('/agent-tui/runner-types'),
    staleTime: 60_000,
  })
}

export function useAgentTuiProfiles() {
  return useQuery({
    queryKey: agentTuiKeys.profiles(),
    queryFn: () => apiGet<AgentTuiProfileDto[]>('/agent-tui/profiles'),
    staleTime: 5_000,
  })
}

export function useAgentTuiProfile(profileId: string | null) {
  return useQuery({
    queryKey: agentTuiKeys.profile(profileId ?? ''),
    queryFn: () => apiGet<AgentTuiProfileDto>(`/agent-tui/profiles/${profileId}`),
    enabled: !!profileId,
    staleTime: 5_000,
  })
}

export function useAgentTuiModels(profileId: string | null) {
  return useQuery({
    queryKey: agentTuiKeys.models(profileId ?? ''),
    queryFn: () => apiGet<AgentTuiModelDto[]>(`/agent-tui/profiles/${profileId}/models`),
    enabled: !!profileId,
    staleTime: 5_000,
  })
}

export function useCreateAgentTuiProfile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: AgentTuiProfileWriteRequest) =>
      apiPost<AgentTuiProfileDto>('/agent-tui/profiles', body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
    },
  })
}

export function useUpdateAgentTuiProfile(profileId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: AgentTuiProfileWriteRequest) =>
      apiPatch<AgentTuiProfileDto>(`/agent-tui/profiles/${profileId}`, body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profile(profileId) })
    },
  })
}

export function useDuplicateAgentTuiProfile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ profileId, displayName }: { profileId: string; displayName: string }) =>
      apiPost<AgentTuiProfileDto>(`/agent-tui/profiles/${profileId}/duplicate`, { displayName }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
    },
  })
}

export function useDeleteAgentTuiProfile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (profileId: string) => apiDelete(`/agent-tui/profiles/${profileId}`),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
    },
  })
}

export function usePutAgentTuiSecret(profileId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      environmentName,
      value,
      expectedRevision,
    }: {
      environmentName: string
      value: string
      expectedRevision: number
    }) =>
      apiPut<{ name: string; configured: boolean; updatedAt: string; revision: number }>(
        `/agent-tui/profiles/${profileId}/secrets/${encodeURIComponent(environmentName)}`,
        { value, expectedRevision },
      ),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profile(profileId) })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
    },
  })
}

export function useClearAgentTuiSecret(profileId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      environmentName,
      expectedRevision,
    }: {
      environmentName: string
      expectedRevision: number
    }) =>
      apiDelete(
        `/agent-tui/profiles/${profileId}/secrets/${encodeURIComponent(environmentName)}`,
        { expectedRevision },
      ),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profile(profileId) })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
    },
  })
}

export function useRefreshAgentTuiModels(profileId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () =>
      apiPost<AgentTuiValidationRunDto>(`/agent-tui/profiles/${profileId}/models/refresh`, null),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.models(profileId) })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profile(profileId) })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
    },
  })
}

export function useValidateAgentTuiProfile(profileId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () =>
      apiPost<AgentTuiValidationRunDto>(`/agent-tui/profiles/${profileId}/validate`, null),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profile(profileId) })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.profiles() })
      void qc.invalidateQueries({ queryKey: agentTuiKeys.capabilities(profileId) })
    },
  })
}

export function useAgentTuiCapabilities(profileId: string | null) {
  return useQuery({
    queryKey: agentTuiKeys.capabilities(profileId ?? ''),
    queryFn: () =>
      apiGet<AgentTuiCapabilitySnapshotDto>(`/agent-tui/profiles/${profileId}/capabilities`),
    enabled: !!profileId,
    staleTime: 5_000,
  })
}
