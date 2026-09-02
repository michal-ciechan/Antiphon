import type { AgentSummaryDto } from '../../../api/agents'
import type { AgentTaskStatus } from '../../../api/agentTasks'
import type { AttentionItemDto } from '../../../api/attention'
import type { CardStatus } from '../../../api/boards'
import type { HomeTaskGroup, HomeTaskHumanReason, HomeTaskItemDto, HomeTaskSource } from '../../../api/homeTasks'
import { STATE_COLORS } from '../../board/boardVisuals'
import { STATUS_COLOR } from '../../delegations/taskVisuals'
import { normalizeDir } from '../projectGrouping'

export const SOURCE_LABEL: Record<HomeTaskSource, string> = {
  Card: 'Card',
  Delegation: 'Task',
}

export const HUMAN_REASON_LABEL: Record<HomeTaskHumanReason, string> = {
  Decision: 'Needs decision',
  Question: 'Question',
  Gate: 'Gate',
  Review: 'Review',
}

export const STATE_COLOR: Record<CardStatus | AgentTaskStatus, string> = {
  ...STATE_COLORS,
  ...STATUS_COLOR,
}

export const GROUP_ORDER: HomeTaskGroup[] = ['NeedsHuman', 'Running', 'Review', 'Next', 'Done']

export const GROUP_LABEL: Record<HomeTaskGroup, string> = {
  NeedsHuman: 'Needs you',
  Running: 'Running',
  Review: 'To review',
  Next: 'Up next',
  Done: 'Done',
}

export const GROUP_CAP: Record<HomeTaskGroup, number | null> = {
  NeedsHuman: null,
  Running: null,
  Review: 8,
  Next: 8,
  Done: 12,
}

export interface GroupedHomeTasks {
  NeedsHuman: HomeTaskItemDto[]
  Running: HomeTaskItemDto[]
  Review: HomeTaskItemDto[]
  Next: HomeTaskItemDto[]
  Done: HomeTaskItemDto[]
  hidden: { Review: number; Next: number; Done: number }
}

/**
 * Keep items whose working directory, repo path or worktree folds into the selected project's
 * dirKeys. `normalizeDir` is the same fold ProjectTasksPanel uses (mixed `C:/` and `C:\`, casing).
 */
export function filterByProject(items: HomeTaskItemDto[], dirKeys: string[]): HomeTaskItemDto[] {
  const keys = new Set(dirKeys)
  return items.filter((item) => itemDirs(item).some((dir) => keys.has(normalizeDir(dir))))
}

function itemDirs(item: HomeTaskItemDto): string[] {
  return [item.workingDirectory, item.repoPath, item.worktreePath].filter((dir): dir is string => !!dir)
}

/**
 * Split the already-ordered server list into groups. Never re-sorts. Caps To review / Up next /
 * Done and reports how many were cut.
 */
export function groupItems(items: HomeTaskItemDto[]): GroupedHomeTasks {
  const buckets: Record<HomeTaskGroup, HomeTaskItemDto[]> = {
    NeedsHuman: [],
    Running: [],
    Review: [],
    Next: [],
    Done: [],
  }
  for (const item of items) buckets[item.group].push(item)

  const take = (group: 'Review' | 'Next' | 'Done') => {
    const cap = GROUP_CAP[group]!
    const all = buckets[group]
    return { visible: all.slice(0, cap), hidden: Math.max(0, all.length - cap) }
  }

  const review = take('Review')
  const next = take('Next')
  const done = take('Done')
  return {
    NeedsHuman: buckets.NeedsHuman,
    Running: buckets.Running,
    Review: review.visible,
    Next: next.visible,
    Done: done.visible,
    hidden: { Review: review.hidden, Next: next.hidden, Done: done.hidden },
  }
}

/**
 * First non-blank line of the matching attention evidence. Cards match `CardNeedsDecision` by
 * card id, then `BlockedQuestion` on the bound worker; unbound tasks match `BlockedQuestion` by
 * their own id. Null when the feed has no row — the reason badge still shows.
 */
export function questionFor(item: HomeTaskItemDto, attentionItems: AttentionItemDto[]): string | null {
  const firstLine = (evidence: string) =>
    evidence
      .split(/\r?\n/)
      .map((line) => line.trim())
      .find((line) => line.length > 0) ?? null

  if (item.source === 'Card') {
    const decision = attentionItems.find((row) => row.kind === 'CardNeedsDecision' && row.cardId === item.id)
    if (decision) return firstLine(decision.evidence)
    if (item.worker) {
      const blocked = attentionItems.find(
        (row) => row.kind === 'BlockedQuestion' && row.taskId === item.worker!.taskId,
      )
      if (blocked) return firstLine(blocked.evidence)
    }
    return null
  }

  const blocked = attentionItems.find((row) => row.kind === 'BlockedQuestion' && row.taskId === item.id)
  return blocked ? firstLine(blocked.evidence) : null
}

export function workerAgent(item: HomeTaskItemDto, agents: AgentSummaryDto[]): AgentSummaryDto | null {
  const agentId = item.worker?.agentId
  if (!agentId) return null
  return agents.find((agent) => agent.id === agentId) ?? null
}

const OPEN_WORKER: ReadonlySet<AgentTaskStatus> = new Set(['Queued', 'Dispatched', 'Working', 'Blocked'])

export function isSpawnable(item: HomeTaskItemDto): boolean {
  if (item.source !== 'Card' || !item.boardId) return false
  if (item.ownerAgentId != null) return false
  if (item.state === 'Done' || item.state === 'Canceled') return false
  return !(item.worker && OPEN_WORKER.has(item.worker.status))
}

export function isAnswerable(item: HomeTaskItemDto): boolean {
  if (item.source === 'Delegation') return item.state === 'Blocked'
  return item.worker?.status === 'Blocked'
}
