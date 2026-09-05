import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'
import type { AgentDetailDto, AgentModelLevel, AgentReplyStyle, InstructionBundleDto } from './agents'
import type { BoardSummaryDto } from './boards'
import type { ProjectDto } from './projects'

export type ReadinessLevel = 'Required' | 'Recommended' | 'Optional'
export type ReadinessStatus = 'Ok' | 'Missing' | 'Warning' | 'NotApplicable'

export interface ReadinessFixDto {
  label: string
  route?: string | null
  action?: string | null
}

export interface ReadinessCheckDto {
  key: string
  level: ReadinessLevel
  status: ReadinessStatus
  summary: string
  detail?: string | null
  fix?: ReadinessFixDto | null
  fixes?: ReadinessFixDto[] | null
}

export interface ProjectReadinessDto {
  projectId: string
  canDispatch: boolean
  checks: ReadinessCheckDto[]
}

export interface ModelLevelDto {
  key: string
  label: string
  blurb: string
  aliasesByKind: Record<string, string>
}

export interface ReplyStyleDto {
  key: string
  label: string
  description: string
}

export interface AgentTuiProfileSummaryDto {
  id: string
  displayName: string
  kind: string
  isDefault: boolean
  hasActiveRevision: boolean
}

export interface AgentPresetDto {
  key: string
  label: string
  description: string
  alwaysOn: boolean
  modelLevel: AgentModelLevel
  replyStyle: AgentReplyStyle
  bundleKeys: string[]
  systemPromptTemplate: string | null
  namePattern: string
  remoteControlEnabled: boolean
  defaultWorkflowTemplateId: string | null
}

export interface DelegationSummaryDto {
  allowedRoots: string[]
  allowedRootsIsEmpty: boolean
  maxConcurrentTasks: number
  maxCostUsdPerRoot: number
  maxDepth: number
  defaultLevel: AgentModelLevel
}

export interface ProjectSetupCatalogDto {
  modelLevels: ModelLevelDto[]
  replyStyles: ReplyStyleDto[]
  bundles: InstructionBundleDto[]
  profiles: AgentTuiProfileSummaryDto[]
  presets: AgentPresetDto[]
  delegation: DelegationSummaryDto
}

export interface ProjectSetupAgentRequest {
  preset?: string | null
  name?: string | null
  tuiProfileId?: string | null
  modelId?: string | null
  modelLevel?: AgentModelLevel | null
  replyStyle?: AgentReplyStyle | null
  alwaysOn?: boolean | null
  remoteControlEnabled?: boolean | null
  bundleKeys?: string[] | null
  systemPromptAppend?: string | null
}

export interface ProjectSetupRequest {
  directory: string
  createDirectory?: boolean
  name?: string | null
  gitRepositoryUrl?: string | null
  baseBranch?: string | null
  boardName?: string | null
  boardMaxConcurrentSessions?: number
  agent?: ProjectSetupAgentRequest | null
  startAgent?: boolean
}

export interface ProjectSetupResultDto {
  project: ProjectDto
  board: BoardSummaryDto
  agent: AgentDetailDto | null
  readiness: ProjectReadinessDto
  notes: string[]
}

export const projectSetupKeys = {
  catalog: ['projects', 'setup-catalog'] as const,
  readiness: (id: string) => ['projects', id, 'readiness'] as const,
  readinessList: (ids: string[]) => ['projects', 'readiness', ids] as const,
}

export function useProjectReadiness(id: string | undefined) {
  return useQuery({
    queryKey: id ? projectSetupKeys.readiness(id) : ['projects', 'readiness', 'missing'],
    queryFn: () => apiGet<ProjectReadinessDto>(`/projects/${id}/readiness`),
    enabled: !!id,
  })
}

export function useProjectReadinessList(ids: string[]) {
  return useQuery({
    queryKey: projectSetupKeys.readinessList(ids),
    queryFn: () => apiGet<ProjectReadinessDto[]>(`/projects/readiness?ids=${encodeURIComponent(ids.join(','))}`),
    enabled: ids.length > 0,
    retry: 1,
  })
}

export function useSetupCatalog(enabled = true) {
  return useQuery({
    queryKey: projectSetupKeys.catalog,
    queryFn: () => apiGet<ProjectSetupCatalogDto>('/projects/setup-catalog'),
    staleTime: Infinity,
    enabled,
  })
}

export function useAcknowledgeOrchestratorWorkspace() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (projectId: string) =>
      apiPost<ProjectReadinessDto>(`/projects/${projectId}/acknowledge-orchestrator-workspace`, {}),
    onSuccess: (readiness) => {
      queryClient.setQueryData(projectSetupKeys.readiness(readiness.projectId), readiness)
      queryClient.invalidateQueries({ queryKey: ['projects', 'readiness'] })
    },
  })
}

export function useSetupProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ProjectSetupRequest) =>
      apiPost<ProjectSetupResultDto>('/projects/setup', request),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['projects'] })
      queryClient.invalidateQueries({ queryKey: ['boards'] })
      queryClient.invalidateQueries({ queryKey: ['agents'] })
      queryClient.setQueryData(projectSetupKeys.readiness(result.project.id), result.readiness)
    },
  })
}

export function missingRequiredCount(readiness: ProjectReadinessDto): number {
  return readiness.checks.filter((c) => c.level === 'Required' && c.status === 'Missing').length
}

export function readinessHeader(readiness: ProjectReadinessDto): string {
  if (readiness.canDispatch) return 'Ready to dispatch'
  const n = missingRequiredCount(readiness)
  return `Cannot dispatch yet — ${n} thing${n === 1 ? '' : 's'} missing`
}
