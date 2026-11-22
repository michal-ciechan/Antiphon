import type { Meta, StoryObj } from '@storybook/react'
import { fakeReviewApi } from './reviewApi'
import { ReviewStoryShell } from './ReviewStoryShell'
import { EMPTY_RUNS_FILTERS, useReviewUiStore } from './useReviewUiStore'
import { DEFAULT_REVIEW_VIEWER, useReviewViewerStore } from './useReviewViewerStore'

/** The real /reviews page against the in-memory fake API. Click a run to open its page. */
const meta: Meta = {
  title: 'Review/Review runs',
  parameters: { layout: 'fullscreen' },
  beforeEach: () => {
    fakeReviewApi.reset()
    useReviewViewerStore.setState({ viewer: DEFAULT_REVIEW_VIEWER })
    useReviewUiStore.setState({ runsView: 'table', runsFilters: EMPTY_RUNS_FILTERS })
  },
}
export default meta

type Story = StoryObj

/** The dense table with the KPI filter cards (default view). */
export const Table: Story = { render: () => <ReviewStoryShell initialPath="/reviews" /> }

/** The whose-move board. */
export const Board: Story = {
  beforeEach: () => {
    useReviewUiStore.setState({ runsView: 'board' })
  },
  render: () => <ReviewStoryShell initialPath="/reviews" />,
}

/** "Waiting on humans" toggled on from its KPI card. */
export const WaitingOnHumans: Story = {
  beforeEach: () => {
    useReviewUiStore.setState({ runsFilters: { ...EMPTY_RUNS_FILTERS, humans: true } })
  },
  render: () => <ReviewStoryShell initialPath="/reviews" />,
}
