import { useEffect } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { httpReviewApi, reviewApiMode, reviewRunChangedKeys, type ReviewApi, type ReviewApiMode } from '../../api/reviewEngine'
import { createFakeReviewApi } from './fake/fakeReviewApi'
import { useFakeViewerStore } from './fake/fakeViewer'

let forcedMode: ReviewApiMode | null = null
let resolvedMode: ReviewApiMode | undefined

/**
 * The one place that picks the review API implementation. A forced mode (Storybook, Vitest) wins;
 * otherwise `reviewApiMode()` is read once, on first use: HTTP unless localStorage
 * `antiphon.reviewApi` or `VITE_REVIEW_API` asks for the fake. Switching needs a page reload, which
 * keeps one implementation behind every cached query.
 */
export function currentReviewApiMode(): ReviewApiMode {
  return forcedMode ?? (resolvedMode ??= reviewApiMode())
}

/**
 * Stories and tests call this before rendering a review page, so they never depend on the build's
 * env or the browser's storage. `null` drops the override.
 */
export function forceReviewApiMode(mode: ReviewApiMode | null) {
  forcedMode = mode
}

/** The in-memory fake, acting as whoever the fake-viewer store says (the banner's "Viewing as"). */
export const fakeReviewApi = createFakeReviewApi({ actor: () => useFakeViewerStore.getState().login })

/** The implementation for the current mode; resolved per call so a forced mode applies at once. */
export function reviewApi(): ReviewApi {
  return currentReviewApiMode() === 'fake' ? fakeReviewApi : httpReviewApi
}

/**
 * Live updates in fake mode. The fake emits `ReviewRunChanged { runId }` in-process (including
 * the delayed Pending → Synced flips), and this maps it to the same invalidations the SignalR
 * handler performs in HTTP mode (`useSignalRInvalidation`).
 */
export function useReviewLiveUpdates() {
  const queryClient = useQueryClient()
  useEffect(() => {
    if (currentReviewApiMode() !== 'fake') return
    return fakeReviewApi.subscribe(({ runId }) => {
      for (const queryKey of reviewRunChangedKeys(runId)) void queryClient.invalidateQueries({ queryKey })
    })
  }, [queryClient])
}

/**
 * Fake mode: act as someone else. Every review query is viewer-dependent (`review-me`, each gate's
 * `viewerCanDecide`, the Mine queue), so all of them are invalidated.
 */
export function switchFakeViewer(queryClient: QueryClient, login: string) {
  useFakeViewerStore.getState().setLogin(login)
  void queryClient.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('review') })
}
