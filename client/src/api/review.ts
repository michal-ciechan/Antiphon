import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost } from './client'

// ---- Files review surface (git ∪ agent activity + viewed/reviewed marks) ----

export type GitFileStatus = 'None' | 'Modified' | 'Added' | 'Deleted' | 'Renamed' | 'Untracked'
export type FileReviewLevel = 'Viewed' | 'Reviewed'

export interface AgentFileDto {
  path: string
  gitStatus: GitFileStatus
  external: boolean
  agentEdits: number
  lastAgentEditAt: string | null
  contentHash: string | null
  reviewLevel: FileReviewLevel | null
  reviewStale: boolean
  sizeBytes: number | null
  isMarkdown: boolean
  /**
   * Client-side only: an unchanged workspace file merged in by the "All files" toggle for
   * browsing context — not part of the review set (no unviewed dot, no marks).
   */
  contextOnly?: boolean
}

/**
 * What the git half of the listing was diffed against: 'head' = working tree vs HEAD, 'checkpoint'
 * = the latest "work completed" checkpoint (timestampFallback when its commit left history and the
 * baseline resolved by time instead), 'commit' = an explicitly selected sha.
 */
export interface FilesBaselineDto {
  kind: 'head' | 'checkpoint' | 'commit'
  commitSha: string | null
  checkpointReason: string | null
  checkpointAt: string | null
  timestampFallback: boolean
}

export interface AgentFilesDto {
  agentId: string
  workspaceRoot: string
  isGitRepository: boolean
  files: AgentFileDto[]
  baseline: FilesBaselineDto
}

export interface WorkspaceCommitDto {
  sha: string
  shortSha: string
  author: string
  date: string
  subject: string
  isCheckpoint: boolean
}

export interface WorkspaceTreeDto {
  paths: string[]
  truncated: boolean
}

export interface AgentFileContentDto {
  path: string
  rev: 'work' | 'head'
  text: string | null
  truncated: boolean
  missing: boolean
  isBinary: boolean
}

export const reviewKeys = {
  filesRoot: (agentId: string) => ['agents', agentId, 'files'] as const,
  files: (agentId: string, since: string) => ['agents', agentId, 'files', 'list', since] as const,
  commits: (agentId: string) => ['agents', agentId, 'files', 'commits'] as const,
  tree: (agentId: string) => ['agents', agentId, 'files', 'tree'] as const,
  content: (agentId: string, path: string, rev: string, since: string) =>
    ['agents', agentId, 'files', 'content', path, rev, since] as const,
  threads: (agentId: string) => ['agents', agentId, 'review-threads'] as const,
}

/**
 * @param since baseline the listing diffs against: 'checkpoint' (default — the server falls back
 * to HEAD when no checkpoint exists), 'head', or a commit sha.
 */
export function useAgentFiles(agentId: string | null, since: string = 'checkpoint') {
  return useQuery({
    queryKey: reviewKeys.files(agentId ?? '', since),
    queryFn: () =>
      apiGet<AgentFilesDto>(`/agents/${agentId}/files?since=${encodeURIComponent(since)}`),
    enabled: agentId !== null,
    refetchInterval: 15_000,
  })
}

export function useAgentCommits(agentId: string | null) {
  return useQuery({
    queryKey: reviewKeys.commits(agentId ?? ''),
    queryFn: () =>
      apiGet<{ commits: WorkspaceCommitDto[] }>(`/agents/${agentId}/files/commits?limit=100`),
    enabled: agentId !== null,
    refetchInterval: 60_000,
  })
}

export function useAgentFilesTree(agentId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: reviewKeys.tree(agentId ?? ''),
    queryFn: () => apiGet<WorkspaceTreeDto>(`/agents/${agentId}/files/tree`),
    enabled: agentId !== null && enabled,
    refetchInterval: 60_000,
  })
}

export function useAgentFileContent(
  agentId: string | null,
  path: string | null,
  rev: 'work' | 'head',
  since: string = 'checkpoint',
) {
  return useQuery({
    queryKey: reviewKeys.content(agentId ?? '', path ?? '', rev, since),
    queryFn: () =>
      apiGet<AgentFileContentDto>(
        `/agents/${agentId}/files/content?path=${encodeURIComponent(path!)}&rev=${rev}&since=${encodeURIComponent(since)}`,
      ),
    enabled: agentId !== null && path !== null,
  })
}

export function useMarkFilesReview(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: {
      paths?: string[]
      prefix?: string
      level: FileReviewLevel | null
      since?: string
    }) =>
      apiPost(`/agents/${agentId}/files/review`, {
        paths: request.paths ?? null,
        prefix: request.prefix ?? null,
        level: request.level,
        since: request.since ?? null,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reviewKeys.filesRoot(agentId) }),
  })
}

/**
 * Record a manual "work completed up to here" baseline — at the current HEAD, or at an explicit
 * (historic) commit when commitSha is given.
 */
export function useSetReviewCheckpoint(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request?: { commitSha?: string; reason?: string }) =>
      apiPost(`/agents/${agentId}/review/checkpoint`, {
        commitSha: request?.commitSha ?? null,
        reason: request?.reason ?? null,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reviewKeys.filesRoot(agentId) }),
  })
}

// ---- Inline review threads ----

export type ReviewThreadStatus = 'Open' | 'AwaitingAgent' | 'AwaitingHuman' | 'Resolved'

export interface ReviewCommentDto {
  id: string
  author: 'Human' | 'Agent' | 'System'
  body: string
  createdAt: string
}

export interface ReviewThreadDto {
  id: string
  agentId: string
  path: string
  line: number
  snippet: string | null
  status: ReviewThreadStatus
  createdAt: string
  updatedAt: string
  comments: ReviewCommentDto[]
}

export function useReviewThreads(agentId: string | null) {
  return useQuery({
    queryKey: reviewKeys.threads(agentId ?? ''),
    queryFn: () => apiGet<ReviewThreadDto[]>(`/agents/${agentId}/review/threads`),
    enabled: agentId !== null,
    refetchInterval: 10_000,
  })
}

export function useCreateReviewThread(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: { path: string; line: number; snippet: string | null; body: string; dispatch: boolean }) =>
      apiPost<ReviewThreadDto>(`/agents/${agentId}/review/threads`, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reviewKeys.threads(agentId) }),
  })
}

export function useAddReviewComment(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ threadId, body, dispatch }: { threadId: string; body: string; dispatch: boolean }) =>
      apiPost<ReviewThreadDto>(`/review/threads/${threadId}/comments`, { body, dispatch }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reviewKeys.threads(agentId) }),
  })
}

export function useResolveReviewThread(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (threadId: string) => apiPost<ReviewThreadDto>(`/review/threads/${threadId}/resolve`, {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reviewKeys.threads(agentId) }),
  })
}
