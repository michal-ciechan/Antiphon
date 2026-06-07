import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'

export interface DirectoryBrowseResponse {
  normalizedPath: string
  exists: boolean
  isDrivesListing: boolean
  suggestions: string[]
}

export const filesystemKeys = {
  browse: (path: string) => ['filesystem', 'browse', path] as const,
}

/**
 * Fetches directory autocomplete data for a typed path. Empty path returns the drive
 * roots. `staleTime` matches the backend listing cache (~15s) so retyping a recently
 * seen path is served from the react-query cache without a round trip; `gcTime` bounds
 * how long unused keystroke entries linger in memory.
 */
export function useDirectoryBrowse(path: string, enabled: boolean) {
  return useQuery({
    queryKey: filesystemKeys.browse(path),
    queryFn: () => apiGet<DirectoryBrowseResponse>(`/filesystem/browse?path=${encodeURIComponent(path)}`),
    enabled,
    staleTime: 15_000,
    gcTime: 60_000,
  })
}
