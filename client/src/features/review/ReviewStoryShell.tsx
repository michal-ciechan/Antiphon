import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { forceReviewApiMode } from './reviewApi'
import { ReviewQueuePage } from './ReviewQueuePage'
import { ReviewRunPage } from './ReviewRunPage'
import { ReviewRunsPage } from './ReviewRunsPage'

// Stories always run on the in-memory fake, whatever VITE_REVIEW_API or localStorage say.
forceReviewApiMode('fake')

/**
 * Storybook harness: a fresh QueryClient and an in-memory router with the three review routes, so
 * a story can start on any page and click through to the others against the fake API.
 */
export function ReviewStoryShell({ initialPath, children }: { initialPath: string; children?: ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: false } } }))
  return (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialPath]}>
        {children}
        <Routes>
          <Route path="/reviews" element={<ReviewRunsPage />} />
          <Route path="/reviews/:runId" element={<ReviewRunPage />} />
          <Route path="/review-queue" element={<ReviewQueuePage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}
