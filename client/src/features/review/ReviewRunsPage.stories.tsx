import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { ReviewRunsPage } from './ReviewRunsPage'

const meta: Meta<typeof ReviewRunsPage> = {
  title: 'Review/Review runs',
  component: ReviewRunsPage,
  parameters: { layout: 'fullscreen' },
  decorators: [
    (Story) => (
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <Story />
        </MemoryRouter>
      </QueryClientProvider>
    ),
  ],
}
export default meta

export const Table: StoryObj<typeof ReviewRunsPage> = {}
