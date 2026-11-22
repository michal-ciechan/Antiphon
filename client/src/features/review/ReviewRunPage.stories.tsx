import type { Meta, StoryObj } from '@storybook/react'
import { runIdFor } from './fake/seed'
import { fakeReviewApi } from './reviewApi'
import { ReviewStoryShell } from './ReviewStoryShell'
import { useReviewUiStore } from './useReviewUiStore'
import { DEFAULT_REVIEW_VIEWER, FAKE_VIEWERS, useReviewViewerStore } from './useReviewViewerStore'

/** The real /reviews/:runId page against the in-memory fake API. Use the banner's "Viewing as" to switch people. */
const meta: Meta = {
  title: 'Review/Review run',
  parameters: { layout: 'fullscreen' },
  beforeEach: () => {
    fakeReviewApi.reset()
    useReviewViewerStore.setState({ viewer: DEFAULT_REVIEW_VIEWER })
    useReviewUiStore.setState({ sectionCollapse: {} })
  },
}
export default meta

type Story = StoryObj

const run = (n: number, tab?: string) => `/reviews/${runIdFor(n)}${tab ? `?tab=${tab}` : ''}`
const asHugo = () => {
  useReviewViewerStore.setState({ viewer: FAKE_VIEWERS.find((v) => v.login === 'hmartin')! })
}

/** payments-api#612 at the human-review gate, seen by its author: replies allowed, gate decision disabled. */
export const FindingsAsAuthor: Story = { render: () => <ReviewStoryShell initialPath={run(612)} /> }

/** The same run seen by the assigned gate reviewer: approve / another round with a steer / close. */
export const FindingsAsGateReviewer: Story = { beforeEach: asHugo, render: () => <ReviewStoryShell initialPath={run(612)} /> }

/** payments-api#618 mid fan-out (iteration 3, reviewers 3/4) on the workflow graph. */
export const WorkflowLive: Story = { render: () => <ReviewStoryShell initialPath={run(618, 'workflow')} /> }

/** A failed run (a reviewer lost its seat) on the workflow graph. */
export const WorkflowFailed: Story = { render: () => <ReviewStoryShell initialPath={run(1851, 'workflow')} /> }

/** payments-api#618's three iterations: compute on one scale, parks and the gate's steer. */
export const Timeline: Story = { render: () => <ReviewStoryShell initialPath={run(618, 'timeline')} /> }

/** An unknown run id: the 404 state. */
export const NotFound: Story = { render: () => <ReviewStoryShell initialPath="/reviews/00000000-0000-4000-8000-000000000000" /> }
