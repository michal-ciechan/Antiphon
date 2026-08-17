import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { TIER_VISUALS, formatCost } from '../delegations/taskVisuals'

/**
 * The pure half of `WorkLine` (spec §D3 band 2): one live task as the orchestrator loop's own
 * status line — `#56 launch leak · opus` over `check 13:02 · $1.12`. The format IS the feature —
 * it is the line an orchestrator already reports in, so the phone reads the same language as the
 * loop. Separate from the component so it is testable without a render, per the repo's
 * `taskVisuals`/`attentionVisuals` convention.
 */

/** `CARD-0056` anywhere in a task title — the identifier-as-citation convention (spec §0). */
const TITLE_CITATION = /card-0*(\d+)/i

export interface WorkLineParts {
  line: string
  sub: string
}

/** Local wall-clock `13:02` — the format the orchestrator's own status lines use. */
export function formatClockTime(iso: string): string {
  const date = new Date(iso)
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}

/**
 * The citation pulled to the front as the `#56` display form, the rest of the title kept
 * verbatim. A title with no citation renders untouched — foreign schemes stay themselves.
 */
export function citationHead(title: string): string {
  const match = TITLE_CITATION.exec(title)
  if (!match) return title
  const rest = `${title.slice(0, match.index).trimEnd()} ${title.slice(match.index + match[0].length).trimStart()}`
    .trim()
    .replace(/^[-–—:·]\s*/, '')
    .trim()
  return rest ? `#${Number(match[1])} ${rest}` : `#${Number(match[1])}`
}

/**
 * The two lines. Pure — `formatTime` is injectable so tests pin the shape without inheriting the
 * machine's timezone. The sub line leads with the one fact that says whether anyone is watching:
 * the next check time, or `checks spent` when the budget ran out with the task still open
 * (`nextCheckAt` null with checks already taken — a task never checked shows its lane instead).
 */
export function taskWorkLine(
  task: AgentTaskSummaryDto,
  { formatTime = formatClockTime }: { formatTime?: (iso: string) => string } = {},
): WorkLineParts {
  const line = `${citationHead(task.title)} · ${TIER_VISUALS[task.modelLevel].alias}`
  const watch =
    task.status === 'Blocked'
      ? 'blocked — answer it'
      : task.status === 'Queued'
        ? 'queued'
        : task.nextCheckAt
          ? `check ${formatTime(task.nextCheckAt)}`
          : task.checkCount > 0
            ? 'checks spent'
            : 'working'
  return { line, sub: `${watch} · ${formatCost(task.subtreeCostUsd)}` }
}

/** Where a tap goes: the task's drawer — the surface that explains the line. */
export function workLineTarget(task: AgentTaskSummaryDto): string {
  return `/orchestrator?tab=delegations&task=${task.id}`
}
