import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'

export interface DirectoryBrowseResponse {
  normalizedPath: string
  exists: boolean
  isDrivesListing: boolean
  suggestions: string[]
}

/**
 * Git identity of one directory. `repoRoot` is the MAIN checkout's root — for a linked
 * worktree (or a subdirectory of either) it points back at the primary repo, which is how
 * the home screen nests worktree/subdirectory agents under the project they belong to.
 */
export interface WorkspaceGitInfo {
  path: string
  isGitRepository: boolean
  repoRoot: string | null
  branch: string | null
  isWorktree: boolean
}

export interface WorktreeEntry {
  path: string
  /** Null when detached — the UI shows the path and no branch badge. */
  branch: string | null
  isMain: boolean
  isLocked: boolean
  isDetached: boolean
}

export interface WorkspaceWorktrees {
  path: string
  isGitRepository: boolean
  repoRoot: string | null
  worktrees: WorktreeEntry[]
}

export const filesystemKeys = {
  browse: (path: string) => ['filesystem', 'browse', path] as const,
  workspaces: (paths: string[]) => ['filesystem', 'workspaces', ...paths] as const,
  worktrees: (path: string) => ['filesystem', 'worktrees', path] as const,
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

/**
 * Batch git identity for every distinct directory on screen. Callers must pass a STABLE
 * array (sorted, deduped) — it is the query key. Polls so a `git switch` or a new worktree
 * shows up without a reload; the backend caches ~20s, so polling stays cheap.
 */
export function useWorkspaceGitInfos(paths: string[]) {
  return useQuery({
    queryKey: filesystemKeys.workspaces(paths),
    queryFn: () =>
      apiGet<WorkspaceGitInfo[]>(
        `/filesystem/workspaces?${paths.map((p) => `path=${encodeURIComponent(p)}`).join('&')}`,
      ),
    enabled: paths.length > 0,
    staleTime: 20_000,
    refetchInterval: 30_000,
  })
}

/** All worktrees of the repo containing `path` — the switcher's rows for the selected project. */
export function useWorkspaceWorktrees(path: string | null) {
  return useQuery({
    queryKey: filesystemKeys.worktrees(path ?? ''),
    queryFn: () => apiGet<WorkspaceWorktrees>(`/filesystem/worktrees?path=${encodeURIComponent(path!)}`),
    enabled: path != null && path.length > 0,
    staleTime: 20_000,
    refetchInterval: 30_000,
  })
}
