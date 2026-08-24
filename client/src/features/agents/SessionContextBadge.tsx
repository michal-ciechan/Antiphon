import { Badge, Tooltip } from '@mantine/core'
import type { ContextFullnessState } from '../../api/boards'

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

function contextBadgeTone(fullness: number): ContextBadgeTone {
  if (fullness >= CONTEXT_DANGER_FULLNESS) return 'danger'
  if (fullness >= CONTEXT_WARNING_FULLNESS) return 'warning'
  return 'normal'
}

function formatPercent(fullness: number): string {
  return `${Math.round(fullness * 100)}%`
}

function percentCopy(fullness: number): { label: string; hint: string; tone: ContextBadgeTone } {
  return {
    label: formatPercent(fullness),
    hint: `Context ${formatPercent(fullness)} full`,
    tone: contextBadgeTone(fullness),
  }
}

const UNKNOWN_COPY = {
  label: 'unknown',
  hint: 'Context unknown',
  tone: 'awaiting' as const,
}

/**
 * Live context-window fullness for a Claude session. Same Badge / variant="light" / green-
 * orange-red-gray palette as SessionWorkingBadge and AgentActivityBadge.
 *
 * `state` names why fullness is a number or null (CARD-0178). Absent state (older server)
 * with null fullness is "unknown", not compacted — four reasons used to share one copy.
 */
export function SessionContextBadge({
  fullness,
  state,
  size = 'sm',
}: {
  fullness: number | null | undefined
  state?: ContextFullnessState | null
  size?: 'xs' | 'sm' | 'md'
}) {
  if (state === 'Suppressed') return null

  const copy = resolveCopy(fullness, state)
  const dataState = state ?? 'absent'

  return (
    <Tooltip label={copy.hint} withArrow>
      <Badge
        size={size}
        color={TONE_COLOR[copy.tone]}
        variant="light"
        data-testid="session-context-badge"
        data-tone={copy.tone}
        data-state={dataState}
        aria-label={copy.hint}
      >
        {copy.label}
      </Badge>
    </Tooltip>
  )
}

function resolveCopy(
  fullness: number | null | undefined,
  state: ContextFullnessState | null | undefined,
): { label: string; hint: string; tone: ContextBadgeTone } {
  switch (state) {
    case 'NoUsageYet':
      return {
        label: 'no turns yet',
        hint: 'No turns yet — context unknown',
        tone: 'awaiting',
      }
    case 'Compacted':
      return {
        label: 'awaiting next turn',
        hint: 'Compacted — awaiting next turn',
        tone: 'awaiting',
      }
    case 'Cleared':
      return {
        label: 'cleared',
        hint: 'Conversation cleared — awaiting next turn',
        tone: 'awaiting',
      }
    case 'Known':
      return fullness == null ? UNKNOWN_COPY : percentCopy(fullness)
    default:
      return fullness == null ? UNKNOWN_COPY : percentCopy(fullness)
  }
}
