import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import {
  reviewEngineKeys,
  reviewRunChangedKeys,
  type FindingResponseRequest,
  type GateDecisionRequest,
  type QueueScope,
  type ReviewGateDto,
  type ReviewRunFilter,
  type RunFindingResponse,
  type WorkflowGraphDto,
} from '../../api/reviewEngine'
import { REVIEW_API_MODE, reviewApi } from './reviewApi'

/**
 * Query and mutation hooks for the review engine (contract: docs/features/012-review-engine/api.md).
 * In HTTP mode `ReviewRunChanged` keeps these fresh; the 30s poll only covers a dropped connection.
 * In fake mode `useReviewLiveUpdates` does the same job, so there is no poll.
 */
const POLL_MS = REVIEW_API_MODE === 'http' ? 30_000 : false

function invalidateRun(queryClient: QueryClient, runId: string | undefined) {
  for (const queryKey of reviewRunChangedKeys(runId)) void queryClient.invalidateQueries({ queryKey })
}

export function useReviewRuns(filter: ReviewRunFilter = {}) {
  return useQuery({
    queryKey: reviewEngineKeys.runList(filter),
    queryFn: () => reviewApi.listRuns(filter),
    placeholderData: keepPreviousData,
    refetchInterval: POLL_MS,
  })
}

export function useReviewKpis() {
  return useQuery({ queryKey: reviewEngineKeys.kpis, queryFn: () => reviewApi.getKpis(), refetchInterval: POLL_MS })
}

export function useReviewRun(runId: string | undefined) {
  return useQuery({
    queryKey: reviewEngineKeys.run(runId ?? ''),
    queryFn: () => reviewApi.getRun(runId!),
    enabled: !!runId,
    refetchInterval: POLL_MS,
  })
}

export function useReviewFindings(runId: string | undefined) {
  return useQuery({
    queryKey: reviewEngineKeys.findings(runId ?? ''),
    queryFn: () => reviewApi.listFindings(runId!),
    enabled: !!runId,
    refetchInterval: POLL_MS,
  })
}

export function useReviewQueue(scope: QueueScope, login: string | undefined) {
  return useQuery({
    queryKey: reviewEngineKeys.queueScope(scope, login),
    queryFn: () => reviewApi.getQueue(scope, login),
    placeholderData: keepPreviousData,
    refetchInterval: POLL_MS,
  })
}

/**
 * The workflow graph a run used: its snapshotted revision (immutable, so cached for good), or the
 * default workflow when the run has no revision id.
 */
export function useReviewWorkflow(revisionId: string | null) {
  return useQuery({
    queryKey: revisionId ? reviewEngineKeys.workflowRevision(revisionId) : reviewEngineKeys.defaultWorkflow,
    queryFn: () => (revisionId ? reviewApi.getWorkflowRevision(revisionId) : reviewApi.getDefaultWorkflow()),
    staleTime: revisionId ? Infinity : 5 * 60_000,
  })
}

/**
 * Who the review UI acts as (`GET /api/review-me`). In HTTP mode the server resolves it (identity,
 * or `Review:DefaultViewerLogin` until identity lands); in fake mode it is the banner's "Viewing
 * as" person. Gate permissions come from each gate's `viewerCanDecide`, not from this.
 */
export function useReviewMe() {
  return useQuery({ queryKey: reviewEngineKeys.me, queryFn: () => reviewApi.getMe(), staleTime: REVIEW_API_MODE === 'http' ? 5 * 60_000 : 0 })
}

/** One submitted review: every drafted reply on the run, posted together (one code-host notification). */
export function useRespondToFindings(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (responses: RunFindingResponse[]) => reviewApi.respondToRun(runId, responses),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

export function useRespondToFinding(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ findingId, request }: { findingId: string; request: FindingResponseRequest }) => reviewApi.respondToFinding(findingId, request),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

export function useGateDecision(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ gateId, request }: { gateId: string; request: GateDecisionRequest }) => reviewApi.decideGate(gateId, request),
    onSuccess: (run) => queryClient.setQueryData(reviewEngineKeys.run(runId), run),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

/** `login` omitted = random pick (the server excludes the author and anyone who passed). */
export function useAssignGate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ gateId, login }: { gateId: string; login?: string }) => reviewApi.assignGate(gateId, login),
    onSettled: (gate: ReviewGateDto | undefined) => invalidateRun(queryClient, gate?.runId),
  })
}

/** "Not me": returns the item to the team queue and excludes the caller from the next pick. */
export function usePassGate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ gateId, reason }: { gateId: string; reason: string }) => reviewApi.passGate(gateId, reason),
    onSettled: (gate: ReviewGateDto | undefined) => invalidateRun(queryClient, gate?.runId),
  })
}

/** The manual Refresh: asks the code host for new events (and retries failed syncs). */
export function useRefreshReviewRun(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => reviewApi.refreshRun(runId),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

export function useValidateWorkflow() {
  return useMutation({ mutationFn: (graph: WorkflowGraphDto) => reviewApi.validateWorkflow(graph) })
}
