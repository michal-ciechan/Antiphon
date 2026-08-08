import { Badge, Loader } from '@mantine/core'
import type { AgentSummaryDto } from '../../api/agents'

/**
 * The one agent status badge — used by the agents page (cards + detail header) and the files
 * review page, so "Working" means the same thing everywhere it appears.
 *
 * It reads `agent.working` (transcript-derived: mid-turn RIGHT NOW), never `agent.status`, which
 * only says the agent was started. Before this was shared, the files page rendered raw
 * `agent.status` through its own colour map — a map that still had a `Working` key after the enum
 * was renamed to `Running`, so the same live agent read "Working" (yellow) on one page and
 * "Running" (grey, unmapped fallback) on the other.
 *
 * Quiet states show NOTHING by default: on the agents page liveness is the terminal icon's
 * colour, and a permanent "Idle" badge on every card is noise. Pass `showIdle` in surfaces with no
 * terminal icon (the files page header) where the badge is the only status indicator.
 */
export function AgentActivityBadge({ agent, showIdle = false }: { agent: AgentSummaryDto; showIdle?: boolean }) {
  if (agent.working) {
    return (
      <Badge
        color="yellow"
        variant="light"
        leftSection={<Loader size={10} color="yellow" type="dots" />}
        data-testid={`agent-working-${agent.id}`}
      >
        Working
      </Badge>
    )
  }
  if (agent.status === 'WaitingForHumanReview') {
    return (
      <Badge color="orange" variant="light">
        Review
      </Badge>
    )
  }
  if (agent.status === 'Failed' || agent.status === 'Disconnected') {
    return (
      <Badge color="red" variant="light">
        {agent.status}
      </Badge>
    )
  }
  if (!showIdle) return null

  // Green mirrors the terminal icon: a live session that simply is not mid-turn. Anything else
  // (Stopped, Idle, Ready — no process) stays grey and says so.
  const live = agent.liveSession?.status === 'Running'
  return (
    <Badge color={live ? 'green' : 'gray'} variant="light" data-testid={`agent-activity-${agent.id}`}>
      {live ? 'Idle' : agent.status}
    </Badge>
  )
}
