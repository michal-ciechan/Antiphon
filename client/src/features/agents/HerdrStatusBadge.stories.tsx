import type { Meta, StoryObj } from '@storybook/react'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { HerdrStatusBadge } from './HerdrStatusBadge'

const base: AgentSessionSummaryDto = {
  id: '00000000-0000-0000-0000-000000000163', definitionName: 'herdr pane', agentKind: 'ClaudeCode',
  status: 'Running', cwd: 'C:\\Antiphon', createdAt: '', startedAt: '', lastSeenAt: '', endedAt: null,
  exitCode: null, failureReason: null, herdrAgentStatusSinceUtc: '2026-08-26T12:34:00Z',
}

const meta: Meta<typeof HerdrStatusBadge> = { title: 'Agents/HerdrStatusBadge', component: HerdrStatusBadge }
export default meta
type Story = StoryObj<typeof HerdrStatusBadge>

export const States: Story = {
  render: () => <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
    {(['idle', 'working', 'blocked', 'done', 'unknown'] as const).map(status =>
      <HerdrStatusBadge key={status} session={{ ...base, herdrAgentStatus: status }} working={status === 'working'} />)}
  </div>,
}

export const Disagreement: Story = {
  args: { session: { ...base, herdrAgentStatus: 'blocked' }, working: false },
}
