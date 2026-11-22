import type { Meta, StoryObj } from '@storybook/react'
import { fakeReviewApi } from './reviewApi'
import { ReviewStoryShell } from './ReviewStoryShell'
import { useReviewUiStore } from './useReviewUiStore'
import { DEFAULT_REVIEW_VIEWER, useReviewViewerStore } from './useReviewViewerStore'

/** The real /review-queue page against the in-memory fake API. */
const meta: Meta = {
  title: 'Review/Review queue',
  parameters: { layout: 'fullscreen' },
  beforeEach: () => {
    fakeReviewApi.reset()
    useReviewViewerStore.setState({ viewer: DEFAULT_REVIEW_VIEWER })
    useReviewUiStore.setState({ queueScope: 'Mine' })
  },
}
export default meta

type Story = StoryObj

/** Alice's queue: the hotfix assigned to her plus unassigned payments-core items. */
export const Mine: Story = { render: () => <ReviewStoryShell initialPath="/review-queue" /> }

/** Every open gate across teams. */
export const All: Story = {
  beforeEach: () => {
    useReviewUiStore.setState({ queueScope: 'All' })
  },
  render: () => <ReviewStoryShell initialPath="/review-queue" />,
}
