import { useEffect } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { httpReviewApi, reviewApiMode, reviewRunChangedKeys, type ReviewApi } from '../../api/reviewEngine'
import { createFakeReviewApi } from './fake/fakeReviewApi'
import { useFakeViewerStore } from './fake/fakeViewer'

/**
 * The one place that picks the review API implementation (see `reviewApiMode`: localStorage
 * `antiphon.reviewApi`, then `VITE_REVIEW_API=http`, else the fake). Read once at load: switching
 * needs a page reload, which keeps one implementation behind every cached query.
 */
export const REVIEW_API_MODE = reviewApiMode()

/** The in-memory fake, acting as whoever the fake-viewer store says (the banner's "Viewing as"). */
export const fakeReviewApi = createFakeReviewApi({ actor: () => useFakeViewerStore.getState().login })

export const reviewApi: ReviewApi = REVIEW_API_MODE === 'http' ? httpReviewApi : fakeReviewApi

/**
 * Live updates in fake mode. The fake emits `ReviewRunChanged { runId }` in-process (including
 * the delayed Pending → Synced flips), and this maps it to the same invalidations the SignalR
 * handler performs in HTTP mode (`useSignalRInvalidation`).
 */
export function useReviewLiveUpdates() {
  const queryClient = useQueryClient()
  useEffect(() => {
    if (REVIEW_API_MODE !== 'fake') return
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
