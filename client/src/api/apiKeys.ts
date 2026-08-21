import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPut } from './client'

/** Metadata only — stored API-key values are deliberately never returned to the browser. */
export interface ApiKeyDto {
  id: string
  name: string
  projectId: string | null
  projectName: string | null
  createdAt: string
  updatedAt: string
}

export interface PutApiKeyRequest {
  value: string
}

export const apiKeyKeys = {
  all: ['api-keys'] as const,
  scope: (projectId?: string) => [...apiKeyKeys.all, projectId ?? 'global'] as const,
}

function scopePath(projectId?: string): string {
  return projectId ? `/projects/${projectId}/api-keys` : '/api-keys/global'
}

function writePath(name: string, projectId?: string): string {
  const encodedName = encodeURIComponent(name)
  return projectId ? `/projects/${projectId}/api-keys/${encodedName}` : `/api-keys/${encodedName}`
}

export function useApiKeys(projectId?: string) {
  return useQuery({
    queryKey: apiKeyKeys.scope(projectId),
    queryFn: () => apiGet<ApiKeyDto[]>(scopePath(projectId)),
  })
}

export function usePutApiKey(projectId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      apiPut<ApiKeyDto>(writePath(name, projectId), { value } satisfies PutApiKeyRequest),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: apiKeyKeys.scope(projectId) }),
  })
}

export function useDeleteApiKey(projectId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/api-keys/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: apiKeyKeys.scope(projectId) }),
  })
}
