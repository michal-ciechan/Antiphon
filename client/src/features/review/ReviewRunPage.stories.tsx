import type { Meta, StoryObj } from '@storybook/react'
import { FAKE_DEFAULT_VIEWER_LOGIN, useFakeViewerStore } from './fake/fakeViewer'
import { runIdFor } from './fake/seed'
import { fakeReviewApi } from './reviewApi'
import { ReviewStoryShell } from './ReviewStoryShell'
import { useReviewUiStore } from './useReviewUiStore'

/** The real /reviews/:runId page against the in-memory fake API. Use the banner's "Viewing as" to switch people. */
const meta: Meta = {
  title: 'Review/Review run',
  parameters: { layout: 'fullscreen' },
  beforeEach: () => {
    fakeReviewApi.reset()
    useFakeViewerStore.setState({ login: FAKE_DEFAULT_VIEWER_LOGIN })
    useReviewUiStore.setState({ sectionCollapse: {} })
  },
}
export default meta

type Story = StoryObj

const run = (n: number, tab?: string) => `/reviews/${runIdFor(n)}${tab ? `?tab=${tab}` : ''}`
const asHugo = () => {
  useFakeViewerStore.setState({ login: 'hmartin' })
}

/** payments-api#612 at the human-review gate, seen by its author: replies allowed, gate decision disabled. */
export const FindingsAsAuthor: Story = { render: () => <ReviewStoryShell initialPath={run(612)} /> }

/** The same run seen by the assigned gate reviewer: approve / another round with a steer / close. */
export const FindingsAsGateReviewer: Story = { beforeEach: asHugo, render: () => <ReviewStoryShell initialPath={run(612)} /> }

/** payments-api#618 mid fan-out (iteration 3, reviewers 3/4) on the workflow graph. */
export const WorkflowLive: Story = { render: () => <ReviewStoryShell initialPath={run(618, 'workflow')} /> }

/** The same run with the graph full screen (`?graph=fullscreen`); click a step for the floating details window. */
export const WorkflowFullscreen: Story = { render: () => <ReviewStoryShell initialPath={`${run(618, 'workflow')}&graph=fullscreen`} /> }

/** A failed run (a reviewer lost its seat) on the workflow graph; the header shows the failed step's summary. Refresh rechecks it. */
export const WorkflowFailed: Story = { render: () => <ReviewStoryShell initialPath={run(1851, 'workflow')} /> }

/** payments-api#617, the hotfix gate: approve or close only (`allowedDecisions`), opened by a service account. */
export const HotfixGateAsAssignee: Story = { render: () => <ReviewStoryShell initialPath={run(617)} /> }

/** payments-api#618's three iterations from `iterations[]`: round, trigger, base..head, compute on one scale, parks and the gate's steer. */
export const Timeline: Story = { render: () => <ReviewStoryShell initialPath={run(618, 'timeline')} /> }

/** An unknown run id: the 404 state. */
export const NotFound: Story = { render: () => <ReviewStoryShell initialPath="/reviews/00000000-0000-4000-8000-000000000000" /> }
