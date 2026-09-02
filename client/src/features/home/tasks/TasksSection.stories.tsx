import { Paper } from '@mantine/core'
import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { agentKeys } from '../../../api/agents'
import { agentTaskKeys, type AgentTaskPipelineDto } from '../../../api/agentTasks'
import { attentionKeys, type AttentionDto } from '../../../api/attention'
import { homeTaskKeys, type HomeTasksDto } from '../../../api/homeTasks'
import { normalizeDir } from '../projectGrouping'
import { TasksSection } from './TasksSection'
// CONTRACT fixtures — captured (and drift-guarded) by tests/Antiphon.E2E/ContractSnapshotTests
// against the REAL backend. Stories must seed from these files ONLY: hand-written mock shapes can
// silently diverge from what the server actually returns; these cannot.
import homeTasksFixture from '../../../test/fixtures/contract/home-tasks.json'
import pipelineFixture from '../../../test/fixtures/contract/pipeline.json'

const homeTasks = homeTasksFixture as HomeTasksDto
const pipeline = pipelineFixture as AgentTaskPipelineDto
const DIR_KEY = normalizeDir('C:\\src\\antiphon')

/**
 * The section mounts with a pre-seeded QueryClient (the repo's no-MSW Storybook convention): every
 * query resolves from cache, so rendering is deterministic and network-free — which is what the
 * Playwright screenshot suite needs.
 *
 * S2 captured `home-tasks.json`; S3 captured `pipeline.json`. `useAgentList` / `useAttention` still
 * fire from this tree, so those keys are seeded with the empty shapes the endpoints return
 * (generatedAt taken from the home-tasks fixture) rather than invented agent or attention rows.
 *
 * No verdict badge in this screenshot: ContractSnapshotTests does not capture `/api/attention`
 * (no attention.json), so there is no contract PastExpectedIdle / Overdue / ProgressStalled row to
 * seed. Forcing one would mean changing the E2E scenario (ExpectedDurationMinutes = 0 + test clock)
 * and capturing a new fixture — out of this slice. Elapsed still renders from the home-tasks
 * timestamps against the pinned clock. Queue / ready / active lines do not join: pipeline.json
 * keeps the S3 scenario's `dddddddd-…` ids, home-tasks.json is GUID-scrubbed to `aaaaaaaa-…` /
 * `cccccccc-…`.
 */
// The fixture's instants are fixed (they have to be — a snapshot cannot move), and elapsed time is
// measured against the clock, so without pinning "now" every screenshot would show months of drift
// instead of the minutes the rail is designed to display. Story-local and never restored: the
// screenshot suite loads one story per page.
const NOW = Date.parse('2026-02-03T09:14:00Z')
Date.now = () => NOW

function withContractData(Story: () => React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
    },
  })
  client.setQueryData(homeTaskKeys.list, homeTasks)
  client.setQueryData(agentTaskKeys.pipeline(), pipeline)
  client.setQueryData(agentKeys.all, [])
  client.setQueryData(attentionKeys.all, {
    generatedAt: homeTasks.generatedAt,
    runnerConsulted: true,
    items: [],
  } satisfies AttentionDto)
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <Story />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const meta: Meta<typeof TasksSection> = {
  title: 'Home/TasksSection',
  component: TasksSection,
  parameters: { layout: 'padded' },
  decorators: [withContractData],
  args: { dirKeys: [DIR_KEY] },
}
export default meta

type Story = StoryObj<typeof TasksSection>

/** The 300px home-rail Tasks section: Needs you, Running, To review, Up next from the S2 fixture. */
export const Rail: Story = {
  render: (args) => (
    <Paper
      withBorder
      p="xs"
      w={300}
      style={{ height: 840, display: 'flex', flexDirection: 'column', minHeight: 0 }}
    >
      <TasksSection {...args} />
    </Paper>
  ),
}
