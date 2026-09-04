import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { AgentTaskDetailDto, AgentTaskListSummaryDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { agentTaskKeys } from '../../api/agentTasks'
import { DelegationsHistory } from './DelegationsHistory'
import { SETTLED_STATUSES, isSettled } from './taskVisuals'
import agentTasksFixture from '../../test/fixtures/contract/agent-tasks.json'
import agentTaskDetailFixture from '../../test/fixtures/contract/agent-task-detail.json'

const tasks = agentTasksFixture as AgentTaskSummaryDto[]
const detail = agentTaskDetailFixture as unknown as AgentTaskDetailDto
const settled = tasks.filter((task) => isSettled(task.status))
const listSummary: AgentTaskListSummaryDto = {
  active: tasks.filter((task) => task.status === 'Dispatched' || task.status === 'Working').length,
  blocked: tasks.filter((task) => task.status === 'Blocked').length,
  runs: new Set(tasks.map((task) => task.rootTaskId)).size,
  totalCostUsd: tasks.reduce((sum, task) => sum + task.costUsd, 0),
  byStatus: {},
}

const NOW = Date.parse('2026-02-03T09:14:00Z')
Date.now = () => NOW

function withHistoryData(Story: () => React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Infinity, gcTime: Infinity, refetchInterval: false },
    },
  })
  client.setQueryData(
    agentTaskKeys.list(false, { since: 'default', status: SETTLED_STATUSES }),
    settled,
  )
  client.setQueryData(agentTaskKeys.summary(), listSummary)
  client.setQueryData(agentTaskKeys.detail(detail.summary.id), detail)
  return (
    <QueryClientProvider client={client}>
      <Story />
    </QueryClientProvider>
  )
}

const meta: Meta<typeof DelegationsHistory> = {
  title: 'Delegations/History',
  component: DelegationsHistory,
  parameters: { layout: 'fullscreen' },
  decorators: [withHistoryData],
}
export default meta

type Story = StoryObj<typeof DelegationsHistory>

/** Newest-settled first, one row per task. */
export const History: Story = {}
