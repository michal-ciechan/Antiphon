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
}

export interface AgentFilesDto {
  agentId: string
  workspaceRoot: string
  isGitRepository: boolean
  files: AgentFileDto[]
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
  files: (agentId: string) => ['agents', agentId, 'files'] as const,
  content: (agentId: string, path: string, rev: string) =>
    ['agents', agentId, 'files', 'content', path, rev] as const,
  threads: (agentId: string) => ['agents', agentId, 'review-threads'] as const,
}

export function useAgentFiles(agentId: string | null) {
  return useQuery({
    queryKey: reviewKeys.files(agentId ?? ''),
    queryFn: () => apiGet<AgentFilesDto>(`/agents/${agentId}/files`),
    enabled: agentId !== null,
    refetchInterval: 15_000,
  })
}

export function useAgentFileContent(agentId: string | null, path: string | null, rev: 'work' | 'head') {
  return useQuery({
    queryKey: reviewKeys.content(agentId ?? '', path ?? '', rev),
    queryFn: () =>
      apiGet<AgentFileContentDto>(
        `/agents/${agentId}/files/content?path=${encodeURIComponent(path!)}&rev=${rev}`,
      ),
    enabled: agentId !== null && path !== null,
  })
}

export function useMarkFilesReview(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: { paths?: string[]; prefix?: string; level: FileReviewLevel | null }) =>
      apiPost(`/agents/${agentId}/files/review`, {
        paths: request.paths ?? null,
        prefix: request.prefix ?? null,
        level: request.level,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reviewKeys.files(agentId) }),
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
