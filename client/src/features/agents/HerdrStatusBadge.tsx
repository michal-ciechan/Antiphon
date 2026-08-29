import { Badge, Group, Loader, Tooltip } from '@mantine/core'
import type { AgentSessionSummaryDto, HerdrAgentStatus } from '../../api/boards'
import { isHerdrDisagreement } from './transcriptModel'

const colors: Record<HerdrAgentStatus, string> = {
  blocked: 'orange', working: 'yellow', idle: 'green', done: 'green', unknown: 'gray',
}

export function HerdrStatusBadge({ session, working, size }: {
  session: AgentSessionSummaryDto
  working: boolean
  size?: 'xs' | 'sm' | 'md' | 'lg'
}) {
  const status = session.herdrAgentStatus
  const attachedChip = session.herdrOrigin === 'attached' ? (
    <Badge color="blue" size={size} variant="outline" data-testid="herdr-attached-chip">
      attached
    </Badge>
  ) : null
  if (!status) return attachedChip
  const disagree = isHerdrDisagreement(status, working)
  const since = session.herdrAgentStatusSinceUtc
    ? new Date(session.herdrAgentStatusSinceUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : 'an unknown time'
  const transcript = working ? 'working' : 'idle'
  const label = disagree
    ? `herdr sees ${status} · transcript says ${transcript} — corroboration only; see the agent's incidents`
    : `herdr's screen detection for the pane, since ${since} — cross-check against Working (transcript)`
  return (
    <Group gap={4} wrap="nowrap">
      <Tooltip label={label} withArrow>
        <Badge color={colors[status]} size={size} variant="light"
          leftSection={status === 'working' ? <Loader size={10} color="yellow" type="dots" /> : undefined}
          data-testid="herdr-status-badge" data-status={status} data-disagree={disagree ? 'true' : undefined}
          style={disagree ? { outline: '1px solid var(--mantine-color-orange-6)' } : undefined}>
          herdr · {status}
        </Badge>
      </Tooltip>
      {attachedChip}
    </Group>
  )
}
