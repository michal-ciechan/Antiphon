import { Badge, Loader, Tooltip } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { getSessionQueue } from '../../api/sessions'

/**
 * The SERVER's working/idle read of a session (IsWorkingAsync over the persisted transcript) —
 * the same signal that gates WhenIdle queue delivery. Shown next to the terminal precisely so a
 * human can cross-check it against what the terminal output claims: when these two disagree
 * (terminal idle, badge stuck on Working…), a working/idle detection gap is live — the
 * interrupt-marker and local-command misses both looked exactly like that.
 */
export function SessionWorkingBadge({ sessionId }: { sessionId: string }) {
  const queue = useQuery({
    queryKey: ['session', sessionId, 'working-badge'],
    queryFn: () => getSessionQueue(sessionId),
    refetchInterval: 3000,
  })

  const working = queue.data?.working
  return (
    <Tooltip label="Server-side working/idle (from the session transcript) — cross-check it against the terminal">
      <Badge
        color={working === undefined ? 'gray' : working ? 'yellow' : 'green'}
        variant="light"
        leftSection={working ? <Loader size={10} color="yellow" type="dots" /> : undefined}
        data-testid="session-working-badge"
      >
        {working === undefined ? '…' : working ? 'Working…' : 'Idle'}
      </Badge>
    </Tooltip>
  )
}
