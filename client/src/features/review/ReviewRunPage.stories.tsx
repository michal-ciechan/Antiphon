import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { runIdFor } from './fake/seed'
import { ReviewRunPage } from './ReviewRunPage'

function Shell({ path }: { path: string }) {
  return (
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/reviews/:runId" element={<ReviewRunPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const meta: Meta = { title: 'Review/Review run', parameters: { layout: 'fullscreen' } }
export default meta

export const Findings: StoryObj = { render: () => <Shell path={`/reviews/${runIdFor(612)}`} /> }
export const Workflow: StoryObj = { render: () => <Shell path={`/reviews/${runIdFor(618)}?tab=workflow`} /> }
export const Timeline: StoryObj = { render: () => <Shell path={`/reviews/${runIdFor(618)}?tab=timeline`} /> }
