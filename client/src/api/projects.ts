import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiPut, apiDelete } from './client'

// --- Project types ---

export interface ProjectDto {
  id: string
  name: string
  gitRepositoryUrl: string
  localRepositoryPath?: string
  baseBranch: string
  constitutionPath: string
  gitHubIntegrationEnabled: boolean
  notificationsEnabled: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateProjectRequest {
  name: string
  gitRepositoryUrl: string
  localRepositoryPath?: string
  baseBranch?: string
  constitutionPath?: string
  gitHubIntegrationEnabled: boolean
  notificationsEnabled: boolean
}

export interface UpdateProjectRequest {
  name: string
  gitRepositoryUrl: string
  localRepositoryPath?: string
  baseBranch?: string
  constitutionPath?: string
  gitHubIntegrationEnabled: boolean
  notificationsEnabled: boolean
}

export interface TestGitConnectivityResult {
  success: boolean
  message: string
}

/** What deleting a project would destroy or detach — read before the confirm dialog unlocks. */
export interface ProjectDeletionImpactDto {
  projectId: string
  projectName: string
  boardCount: number
  cardCount: number
  /** Cards that are neither Done nor Canceled — the "outstanding items" warning. */
  openCardCount: number
  runningSessionCount: number
  /** Agents unpinned from the board/card. Never deleted. */
  detachedAgentCount: number
  workflowCount: number
  /** Non-empty means the delete is impossible, force or not. */
  blockers: string[]
  requiresConfirmation: boolean
  canDelete: boolean
}

// --- Project hooks ---

const PROJECTS_KEY = ['projects'] as const

export function useProjects() {
  return useQuery({
    queryKey: PROJECTS_KEY,
    queryFn: () => apiGet<ProjectDto[]>('/projects'),
  })
}

export function useProject(id: string | undefined) {
  return useQuery({
    queryKey: [...PROJECTS_KEY, id],
    queryFn: () => apiGet<ProjectDto>(`/projects/${id}`),
    enabled: !!id,
  })
}

export function useCreateProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateProjectRequest) =>
      apiPost<ProjectDto>('/projects', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROJECTS_KEY })
    },
  })
}

export function useUpdateProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateProjectRequest }) =>
      apiPut<ProjectDto>(`/projects/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROJECTS_KEY })
    },
  })
}

/**
 * Fetched when the delete dialog opens (never cached — the counts have to be current at the
 * moment someone confirms them).
 */
export function useProjectDeletionImpact(id: string | null) {
  return useQuery({
    queryKey: [...PROJECTS_KEY, id, 'deletion-impact'],
    queryFn: () => apiGet<ProjectDeletionImpactDto>(`/projects/${id}/deletion-impact`),
    enabled: !!id,
    staleTime: 0,
    gcTime: 0,
  })
}

/**
 * `force` is the caller confirming the impact report. Without it the server refuses (409) rather
 * than destroying attached boards and cards.
 */
export function useDeleteProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, force = false }: { id: string; force?: boolean }) =>
      apiDelete(`/projects/${id}${force ? '?force=true' : ''}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROJECTS_KEY })
      queryClient.invalidateQueries({ queryKey: ['boards'] })
    },
  })
}

export function useTestGitConnectivity() {
  return useMutation({
    mutationFn: (gitRepositoryUrl: string) =>
      apiPost<TestGitConnectivityResult>('/projects/test-connectivity', { gitRepositoryUrl }),
  })
}

// --- GitHub status ---

export interface GitHubStatusDto {
  enabled: boolean
  baseUrl: string
  connected: boolean
  authenticatedAs: string | null
  error: string | null
  repoCache: {
    count: number
    lastRefreshed: string | null
    isStale: boolean
    ttlMinutes: number
  }
}

export function useGitHubStatus() {
  return useQuery({
    queryKey: ['github', 'status'],
    queryFn: () => apiGet<GitHubStatusDto>('/github/status'),
    refetchInterval: 30_000,
  })
}

// --- GitHub repo cache ---

export interface GitHubRepoDto {
  fullName: string
  cloneUrl: string
  htmlUrl: string
  isPrivate: boolean
}

const GITHUB_REPOS_KEY = ['github', 'repos'] as const

export function useGitHubRepos() {
  return useQuery({
    queryKey: GITHUB_REPOS_KEY,
    queryFn: () => apiGet<GitHubRepoDto[]>('/github/repos'),
    staleTime: 15 * 60 * 1000, // 15 min — matches server TTL
  })
}

export function useRefreshGitHubRepos() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<GitHubRepoDto[]>('/github/repos/refresh', {}),
    onSuccess: (data) => {
      queryClient.setQueryData(GITHUB_REPOS_KEY, data)
    },
  })
}

// --- Branch diff ---

export interface BranchDiffFileDto {
  filename: string
  additions: number
  deletions: number
  patch: string
}

export interface BranchDiffDto {
  baseBranch: string
  headBranch: string
  files: BranchDiffFileDto[]
  prNumber?: number
  prUrl?: string
  prTitle?: string
  prState?: string
}

export function useBranchDiff(workflowId: string | undefined) {
  return useQuery({
    queryKey: ['workflows', workflowId, 'branch-diff'],
    queryFn: () => apiGet<BranchDiffDto>(`/workflows/${workflowId}/branch-diff`),
    enabled: !!workflowId,
    retry: 1,
    staleTime: 30_000,
  })
}

export function useWorkflowFileContent(workflowId: string | undefined, filePath: string | null) {
  return useQuery({
    queryKey: ['workflows', workflowId, 'file-content', filePath],
    queryFn: () => apiGet<{ path: string; content: string }>(`/workflows/${workflowId}/file-content?path=${encodeURIComponent(filePath!)}`),
    enabled: !!workflowId && !!filePath,
    staleTime: 60_000,
  })
}

// --- GitHub branch listing ---

export interface GitHubBranchDto {
  name: string
  sha: string
  isProtected: boolean
}

export function useRepoBranches(owner: string | null, repo: string | null) {
  return useQuery({
    queryKey: ['github', 'repos', owner, repo, 'branches'],
    queryFn: () => apiGet<GitHubBranchDto[]>(`/github/repos/${owner}/${repo}/branches`),
    enabled: !!owner && !!repo,
    staleTime: 5 * 60 * 1000,
  })
}
