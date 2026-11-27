import { notifications } from '@mantine/notifications'
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ApiError } from '../../api/client'
import {
  reviewEngineKeys,
  reviewRunChangedKeys,
  type CreateReviewRunRequest,
  type FindingResponseRequest,
  type GateDecisionRequest,
  type QueueScope,
  type ReviewGateDto,
  type ReviewRoundRequest,
  type ReviewRunDetailDto,
  type ReviewRunFilter,
  type RunFindingResponse,
  type WorkflowGraphDto,
} from '../../api/reviewEngine'
import { reviewProblem, notifyReviewError } from './reviewErrors'
import { currentReviewApiMode, reviewApi } from './reviewApi'
import { IN_REVIEW_STATUSES } from './runs/runFilters'
import { useReviewUiStore } from './useReviewUiStore'

/**
 * Query and mutation hooks for the review engine (contract: docs/features/012-review-engine/api.md).
 * In HTTP mode `ReviewRunChanged` keeps these fresh; refetch on focus and a poll cover a dropped
 * connection (30s, or 10s for a run that is queued or running). In fake mode `useReviewLiveUpdates`
 * does the same job, so there is no poll. Every mutation reports its failure as a notification
 * with the problem `detail` and `code` (`notifyReviewError`).
 */
const POLL_MS = 30_000
const IN_FLIGHT_POLL_MS = 10_000

const isHttp = () => currentReviewApiMode() === 'http'
const live = () => ({ refetchInterval: isHttp() ? POLL_MS : (false as const), refetchOnWindowFocus: true })

function invalidateRun(queryClient: QueryClient, runId: string | undefined) {
  for (const queryKey of reviewRunChangedKeys(runId)) void queryClient.invalidateQueries({ queryKey })
}

export function useReviewRuns(filter: ReviewRunFilter = {}) {
  return useQuery({
    queryKey: reviewEngineKeys.runList(filter),
    queryFn: () => reviewApi().listRuns(filter),
    placeholderData: keepPreviousData,
    ...live(),
  })
}

export function useReviewKpis() {
  return useQuery({ queryKey: reviewEngineKeys.kpis, queryFn: () => reviewApi().getKpis(), ...live() })
}

export function useReviewRun(runId: string | undefined) {
  const http = isHttp()
  return useQuery({
    queryKey: reviewEngineKeys.run(runId ?? ''),
    queryFn: () => reviewApi().getRun(runId!),
    enabled: !!runId,
    refetchOnWindowFocus: true,
    refetchInterval: (query) => {
      if (!http) return false
      const status = (query.state.data as ReviewRunDetailDto | undefined)?.status
      return status && IN_REVIEW_STATUSES.includes(status) ? IN_FLIGHT_POLL_MS : POLL_MS
    },
  })
}

export function useReviewFindings(runId: string | undefined) {
  return useQuery({
    queryKey: reviewEngineKeys.findings(runId ?? ''),
    queryFn: () => reviewApi().listFindings(runId!),
    enabled: !!runId,
    ...live(),
  })
}

export function useReviewQueue(scope: QueueScope, login: string | undefined) {
  return useQuery({
    queryKey: reviewEngineKeys.queueScope(scope, login),
    queryFn: () => reviewApi().getQueue(scope, login),
    placeholderData: keepPreviousData,
    ...live(),
  })
}

/**
 * The workflow graph a run used: its snapshotted revision (immutable, so cached for good), or the
 * default workflow when the run has no revision id.
 */
export function useReviewWorkflow(revisionId: string | null) {
  return useQuery({
    queryKey: revisionId ? reviewEngineKeys.workflowRevision(revisionId) : reviewEngineKeys.defaultWorkflow,
    queryFn: () => (revisionId ? reviewApi().getWorkflowRevision(revisionId) : reviewApi().getDefaultWorkflow()),
    staleTime: revisionId ? Infinity : 5 * 60_000,
  })
}

export function useReviewWorkflowPolicy(runId: string, revisionId: string | null) {
  return useQuery({
    queryKey: reviewEngineKeys.workflowPolicy(runId, revisionId),
    queryFn: () => reviewApi().getWorkflowPolicy(runId, revisionId),
  })
}

/**
 * Who the review UI acts as (`GET /api/review-me`). In HTTP mode the server resolves it (identity,
 * or `Review:DefaultViewerLogin` until identity lands); in fake mode it is the banner's "Viewing
 * as" person. Gate permissions come from each gate's `viewerCanDecide`, not from this.
 */
export function useReviewMe() {
  return useQuery({ queryKey: reviewEngineKeys.me, queryFn: () => reviewApi().getMe(), staleTime: isHttp() ? 5 * 60_000 : 0 })
}

/** Manual intake (`POST /api/review-runs`); the caller navigates to the new run. */
export function useCreateReviewRun() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateReviewRunRequest) => reviewApi().createRun(request),
    onSuccess: (run) => queryClient.setQueryData(reviewEngineKeys.run(run.id), run),
    onError: (error) => notifyReviewError('Review not started', error),
    onSettled: (run) => invalidateRun(queryClient, run?.id),
  })
}

/**
 * `POST /api/review-runs/demo`. A 404 means the server has demo seeding off: the button hides for
 * the rest of the session instead of reporting an error.
 */
export function useSeedReviewDemo() {
  const queryClient = useQueryClient()
  const setUnavailable = useReviewUiStore((s) => s.setDemoSeedUnavailable)
  return useMutation({
    mutationFn: () => reviewApi().seedDemo(),
    onSuccess: (result) => notifications.show({ color: 'success', message: `Loaded ${result.total} demo review${result.total === 1 ? '' : 's'}.` }),
    onError: (error) => {
      if (error instanceof ApiError && error.status === 404) {
        setUnavailable(true)
        notifications.show({ color: 'gray', title: 'Demo data is off on this server', message: reviewProblem(error, 'Review:DemoSeedEnabled is false.').detail })
        return
      }
      notifyReviewError('Demo data not loaded', error)
    },
    onSettled: () => invalidateRun(queryClient, undefined),
  })
}

/** One submitted review: every drafted reply on the run, posted together (one code-host notification). */
export function useRespondToFindings(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (responses: RunFindingResponse[]) => reviewApi().respondToRun(runId, responses),
    onError: (error) => notifyReviewError('Review not posted', error, 'The review API refused the replies.'),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

export function useRespondToFinding(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ findingId, request }: { findingId: string; request: FindingResponseRequest }) => reviewApi().respondToFinding(findingId, request),
    onError: (error) => notifyReviewError('Reply not saved', error),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

export function useGateDecision(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ gateId, request }: { gateId: string; request: GateDecisionRequest }) => reviewApi().decideGate(gateId, request),
    onSuccess: (run) => queryClient.setQueryData(reviewEngineKeys.run(runId), run),
    onError: (error) => notifyReviewError('Decision not recorded', error),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

/** `login` omitted = random pick (the server excludes the author and anyone who passed). */
export function useAssignGate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ gateId, login }: { gateId: string; login?: string }) => reviewApi().assignGate(gateId, login),
    onError: (error, { login }) => notifyReviewError(login ? 'Could not take it' : 'No one to assign', error),
    onSettled: (gate: ReviewGateDto | undefined) => invalidateRun(queryClient, gate?.runId),
  })
}

/** "Not me": returns the item to the team queue and excludes the caller from the next pick. */
export function usePassGate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ gateId, reason }: { gateId: string; reason: string }) => reviewApi().passGate(gateId, reason),
    onError: (error) => notifyReviewError('Could not pass it back', error),
    onSettled: (gate: ReviewGateDto | undefined) => invalidateRun(queryClient, gate?.runId),
  })
}

/** The manual Refresh (202, no body): re-reads the PR from the code host and retries failed syncs. */
export function useRefreshReviewRun(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => reviewApi().refreshRun(runId),
    onError: (error) => notifyReviewError('Refresh failed', error),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

/**
 * A manual review round (`POST /api/review-runs/{id}/rounds`). No notification on failure: the
 * round modal shows the refusal (a 409 at a gate, finished or busy) where it was asked.
 */
export function useRequestReviewRound(runId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ReviewRoundRequest) => reviewApi().requestRound(runId, request),
    onSuccess: (run) => queryClient.setQueryData(reviewEngineKeys.run(runId), run),
    onSettled: () => invalidateRun(queryClient, runId),
  })
}

/** Code host polling: the settings and the last pass (`GET /api/review-polling`), refetched every minute. */
export function useReviewPolling() {
  return useQuery({
    queryKey: reviewEngineKeys.polling,
    queryFn: () => reviewApi().getPolling(),
    refetchInterval: isHttp() ? 60_000 : false,
    refetchOnWindowFocus: true,
  })
}

/** "Check GitHub now" (`POST /api/review-polling/poll`): one pass, then the runs list is refetched. */
export function usePollNow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => reviewApi().pollNow(),
    onSuccess: (polling) => queryClient.setQueryData(reviewEngineKeys.polling, polling),
    onError: (error) => notifyReviewError('Could not check GitHub', error),
    onSettled: () => invalidateRun(queryClient, undefined),
  })
}

export function useValidateWorkflow() {
  return useMutation({ mutationFn: (graph: WorkflowGraphDto) => reviewApi().validateWorkflow(graph) })
}
