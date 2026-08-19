import { Badge, Tooltip } from '@mantine/core'

type ContextBadgeTone = 'awaiting' | 'normal' | 'warning' | 'danger'

/**
 * Claude's own auto-compact fires around ~92% (`SessionContextUsage.AutoCompactHeadroomThreshold`
 * is 0.80 on the server). Warn from 80%, danger from 90%. Below that is the healthy band.
 */
const CONTEXT_WARNING_FULLNESS = 0.8
const CONTEXT_DANGER_FULLNESS = 0.9

const TONE_COLOR: Record<ContextBadgeTone, string> = {
  awaiting: 'gray',
  normal: 'green',
  warning: 'orange',
  danger: 'red',
}

function contextBadgeTone(fullness: number | null | undefined): ContextBadgeTone {
  if (fullness == null) return 'awaiting'
  if (fullness >= CONTEXT_DANGER_FULLNESS) return 'danger'
  if (fullness >= CONTEXT_WARNING_FULLNESS) return 'warning'
  return 'normal'
}

function formatPercent(fullness: number): string {
  return `${Math.round(fullness * 100)}%`
}

/**
 * Live context-window fullness for a Claude session. Same Badge / variant="light" / green-
 * orange-red-gray palette as SessionWorkingBadge and AgentActivityBadge.
 *
 * Null is the expected post-compaction state (and the pre-first-turn state) — not an error and
 * not 0%. The badge stays visible so a just-compacted session is not mistaken for empty.
 */
export function SessionContextBadge({
  fullness,
  size = 'sm',
}: {
  fullness: number | null | undefined
  size?: 'xs' | 'sm' | 'md'
}) {
  const tone = contextBadgeTone(fullness)
  const label = fullness == null ? 'awaiting next turn' : formatPercent(fullness)
  const hint =
    fullness == null
      ? 'Compacted — awaiting next turn'
      : `Context ${formatPercent(fullness)} full`

  return (
    <Tooltip label={hint} withArrow>
      <Badge
        size={size}
        color={TONE_COLOR[tone]}
        variant="light"
        data-testid="session-context-badge"
        data-tone={tone}
        aria-label={hint}
      >
        {label}
      </Badge>
    </Tooltip>
  )
}
