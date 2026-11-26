import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import { userEvent, within } from 'storybook/test'
import { FAKE_DEFAULT_VIEWER_LOGIN, useFakeViewerStore } from '../fake/fakeViewer'
import { fakeReviewApi } from '../reviewApi'
import { ReviewStoryShell } from '../ReviewStoryShell'
import { EMPTY_RUNS_FILTERS, useReviewUiStore } from '../useReviewUiStore'
import { NewReviewModal } from './NewReviewModal'

/** The modal starts open over the runs page. Closing it leaves the page, whose own "New review" button opens it again. */
function OpenModal() {
  const [opened, setOpened] = useState(true)
  return <NewReviewModal opened={opened} onClose={() => setOpened(false)} />
}

/**
 * The "New review" intake form on its own, against the in-memory fake API. "Start review" creates
 * a run in the fake and opens its page, the same as on the real site.
 */
const meta: Meta = {
  title: 'Review/New review',
  parameters: { layout: 'fullscreen' },
  beforeEach: () => {
    fakeReviewApi.reset()
    useFakeViewerStore.setState({ login: FAKE_DEFAULT_VIEWER_LOGIN })
    useReviewUiStore.setState({ runsView: 'table', runsFilters: EMPTY_RUNS_FILTERS, demoSeedUnavailable: false })
  },
  render: () => (
    <ReviewStoryShell initialPath="/reviews">
      <OpenModal />
    </ReviewStoryShell>
  ),
}
export default meta

type Story = StoryObj

/** The modal renders in a portal, so the form is found from the document body, not the story canvas. */
async function form() {
  const body = within(document.body)
  await body.findByTestId('review-intake-form')
  return body
}

/** As it opens: GitHub PR, nothing filled in, "Start review" disabled. */
export const GitHubPr: Story = { name: 'GitHub PR · empty' }

/** A repository and a PR number are enough to start. */
export const GitHubPrFilled: Story = {
  name: 'GitHub PR · ready to start',
  play: async () => {
    const body = await form()
    await userEvent.type(body.getByTestId('review-intake-repo'), 'codeperf/antiphon-demo')
    await userEvent.type(body.getByLabelText(/PR number/), '612')
  },
}

/** Local git: a base ref is required and the head ref defaults to HEAD. */
export const LocalGit: Story = {
  name: 'Local git · empty',
  play: async () => {
    const body = await form()
    await userEvent.click(body.getByText('Local git'))
  },
}

/** Local git with a repository, a base ref and a title. */
export const LocalGitFilled: Story = {
  name: 'Local git · ready to start',
  play: async () => {
    const body = await form()
    await userEvent.click(body.getByText('Local git'))
    await userEvent.type(body.getByTestId('review-intake-repo'), 'antiphon')
    await userEvent.type(body.getByTestId('review-intake-base'), 'master')
    await userEvent.type(body.getByLabelText(/Title/), 'Retry the poll after a 502')
  },
}
