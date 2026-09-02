import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { AgentTaskDetailDto, AgentTaskListSummaryDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { agentTaskKeys } from '../../api/agentTasks'
import { DelegateModal } from './DelegateModal'
import { DelegationsBoard } from './DelegationsBoard'
import { DelegationsHistory } from './DelegationsHistory'
import { TaskDrawer } from './TaskDrawer'
import { SETTLED_STATUSES, isSettled } from './taskVisuals'
// CONTRACT fixtures — captured (and drift-guarded) by tests/Antiphon.E2E/ContractSnapshotTests
// against the REAL backend. Stories must seed from these files ONLY: hand-written mock shapes can
// silently diverge from what the server actually returns; these cannot.
import agentTasksFixture from '../../test/fixtures/contract/agent-tasks.json'
import agentTaskDetailFixture from '../../test/fixtures/contract/agent-task-detail.json'

const tasks = agentTasksFixture as AgentTaskSummaryDto[]
const detail = agentTaskDetailFixture as AgentTaskDetailDto
const settled = tasks.filter((task) => isSettled(task.status))
const listSummary: AgentTaskListSummaryDto = {
  active: tasks.filter((task) => task.status === 'Dispatched' || task.status === 'Working').length,
  blocked: tasks.filter((task) => task.status === 'Blocked').length,
  runs: new Set(tasks.map((task) => task.rootTaskId)).size,
  totalCostUsd: tasks.reduce((sum, task) => sum + task.costUsd, 0),
  byStatus: {},
}

/**
 * The board mounts with a pre-seeded QueryClient (the repo's no-MSW Storybook convention): every
 * query resolves from cache, so rendering is deterministic and network-free — which is what the
 * Playwright screenshot suite needs.
 *
 * The fixture is one run of the shape the design is for — orchestrator → sub-orchestrator →
 * workers — carrying all four tiers, all four lanes, an escalated task, and each workspace mode.
 */
// The fixture's instants are fixed (they have to be — a snapshot cannot move), and elapsed time is
// measured against the clock, so without pinning "now" every screenshot would show months of drift
// instead of the minutes the board is designed to display. Story-local and never restored: the
// screenshot suite loads one story per page.
const NOW = Date.parse('2026-02-03T09:14:00Z')
Date.now = () => NOW

function seedClient(): QueryClient {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
    },
  })
  client.setQueryData(agentTaskKeys.list(false, { since: 'active' }), tasks)
  client.setQueryData(
    agentTaskKeys.list(false, { since: 'default', status: SETTLED_STATUSES }),
    settled,
  )
  client.setQueryData(agentTaskKeys.summary(), listSummary)
  client.setQueryData(agentTaskKeys.detail(detail.summary.id), detail)
  return client
}

function withContractData(Story: () => React.ReactElement) {
  return (
    <QueryClientProvider client={seedClient()}>
      <Story />
    </QueryClientProvider>
  )
}

const meta: Meta<typeof DelegationsBoard> = {
  title: 'Delegations/Board',
  component: DelegationsBoard,
  parameters: { layout: 'fullscreen' },
  decorators: [withContractData],
}
export default meta

type Story = StoryObj<typeof DelegationsBoard>

/** The fan-out on the left, what needs attention on the right. */
export const Board: Story = {}

/** One task in full: brief, the delegate's untouched words, timeline, and the three actions. */
export const Drawer: StoryObj<typeof TaskDrawer> = {
  render: () => <TaskDrawer taskId={detail.summary.id} onClose={() => {}} />,
}

/** Handing work off by hand — the same two decisions the skill asks an agent to make. */
export const Delegate: StoryObj<typeof DelegateModal> = {
  render: () => (
    <DelegateModal
      opened
      onClose={() => {}}
      title="Delegate — docs/setup.md"
      prefill={{
        goal: 'In docs/setup.md: ',
        workingDirectory: 'C:\\src\\antiphon',
        scope: 'docs/setup.md',
      }}
    />
  ),
}

/** Settled work, newest first — the History tab of /orchestrator. */
export const History: StoryObj<typeof DelegationsHistory> = {
  render: () => <DelegationsHistory />,
}
