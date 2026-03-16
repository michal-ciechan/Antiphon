import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiPut, apiDelete } from './client'

// --- Project types ---

export interface ProjectDto {
  id: string
  name: string
  gitRepositoryUrl: string
  constitutionPath: string
  gitHubIntegrationEnabled: boolean
  notificationsEnabled: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateProjectRequest {
  name: string
  gitRepositoryUrl: string
  constitutionPath?: string
  gitHubIntegrationEnabled: boolean
  notificationsEnabled: boolean
}

export interface UpdateProjectRequest {
  name: string
  gitRepositoryUrl: string
  constitutionPath?: string
  gitHubIntegrationEnabled: boolean
  notificationsEnabled: boolean
}

export interface TestGitConnectivityResult {
  success: boolean
  message: string
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

export function useDeleteProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/projects/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROJECTS_KEY })
    },
  })
}

export function useTestGitConnectivity() {
  return useMutation({
    mutationFn: (gitRepositoryUrl: string) =>
      apiPost<TestGitConnectivityResult>('/projects/test-connectivity', { gitRepositoryUrl }),
  })
}
