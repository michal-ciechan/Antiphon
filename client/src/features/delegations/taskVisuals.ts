import type { AgentModelLevel } from '../../api/agents'
import type { AgentTaskStatus, AgentTaskSummaryDto, WorkspaceMode } from '../../api/agentTasks'

/**
 * Tier gets its OWN visual axis. It is a ladder, not a health signal, so it must never reuse
 * green/orange/red — a violet at four intensities (solid → tinted → outline → grey) reads as rank
 * and cannot be mistaken for "this task is fine / in trouble".
 */
export const TIER_VISUALS: Record<
  AgentModelLevel,
  { alias: string; variant: 'filled' | 'light' | 'outline'; color: string; rank: number }
> = {
  Frontier: { alias: 'fable', variant: 'filled', color: 'violet', rank: 0 },
  High: { alias: 'opus', variant: 'light', color: 'violet', rank: 1 },
  Medium: { alias: 'sonnet', variant: 'outline', color: 'violet', rank: 2 },
  Low: { alias: 'haiku', variant: 'outline', color: 'gray', rank: 3 },
}

/** Health, on the app's semantic palette — deliberately disjoint from the tier axis above. */
export const STATUS_COLOR: Record<AgentTaskStatus, string> = {
  Queued: 'gray',
  Dispatched: 'active',
  Working: 'active',
  Blocked: 'warning',
  Succeeded: 'success',
  Failed: 'danger',
  Canceled: 'gray',
}

export type LaneKey = 'queued' | 'working' | 'blocked' | 'done'

export const LANES: Array<{ key: LaneKey; label: string; statuses: AgentTaskStatus[]; hint: string }> = [
  { key: 'queued', label: 'Queued', statuses: ['Queued'], hint: 'waiting for a dispatch slot' },
  { key: 'working', label: 'Working', statuses: ['Dispatched', 'Working'], hint: 'a delegate is on it' },
  { key: 'blocked', label: 'Blocked', statuses: ['Blocked'], hint: 'asked a question — answer it' },
  { key: 'done', label: 'Done', statuses: ['Succeeded', 'Failed', 'Canceled'], hint: 'settled' },
]

export function laneOf(status: AgentTaskStatus): LaneKey {
  return LANES.find((lane) => lane.statuses.includes(status))?.key ?? 'done'
}

export const WORKSPACE_LABEL: Record<WorkspaceMode, string> = {
  Shared: 'shared',
  Worktree: 'worktree',
  ReadOnly: 'read-only',
}

/**
 * How long the task has been the thing it currently is: running tasks count up from dispatch,
 * settled ones show what they took, and a queued task shows how long it has been waiting — which
 * is the number that tells you the concurrency cap is the bottleneck.
 */
export function elapsedSeconds(task: AgentTaskSummaryDto, now: number = Date.now()): number {
  const start = task.dispatchedAt ?? task.createdAt
  const end = task.completedAt ? Date.parse(task.completedAt) : now
  return Math.max(0, (end - Date.parse(start)) / 1000)
}

export function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)
  // A task that has been "working" for weeks is a stuck task, and "4450h49m" hides that behind
  // arithmetic nobody does in their head.
  if (days > 0) return `${days}d${hours % 24}h`
  if (hours > 0) return `${hours}h${String(minutes % 60).padStart(2, '0')}m`
  if (minutes > 0) return `${minutes}m${String(seconds % 60).padStart(2, '0')}`
  return `${seconds}s`
}

/** Sub-cent spend is normal for a haiku task; two decimals would render every one of them as $0.00. */
export function formatCost(costUsd: number): string {
  if (costUsd === 0) return '$0'
  return costUsd < 0.01 ? `$${costUsd.toFixed(4)}` : `$${costUsd.toFixed(2)}`
}

export function shortId(id: string): string {
  return id.replace(/-/g, '').slice(0, 8)
}

export interface TaskNode {
  task: AgentTaskSummaryDto
  children: TaskNode[]
}

/**
 * The fan-out, as a tree. Rows arrive flat (one query for the whole run), and a task whose parent
 * is missing from the set is treated as a root so a filtered view can never silently drop work.
 */
export function buildTaskForest(tasks: AgentTaskSummaryDto[]): TaskNode[] {
  const nodes = new Map<string, TaskNode>()
  for (const task of tasks) nodes.set(task.id, { task, children: [] })

  const roots: TaskNode[] = []
  for (const node of nodes.values()) {
    const parent = node.task.parentTaskId ? nodes.get(node.task.parentTaskId) : undefined
    if (parent) parent.children.push(node)
    else roots.push(node)
  }

  const byCreated = (a: TaskNode, b: TaskNode) => Date.parse(a.task.createdAt) - Date.parse(b.task.createdAt)
  const sortDeep = (list: TaskNode[]) => {
    list.sort(byCreated)
    for (const node of list) sortDeep(node.children)
  }
  sortDeep(roots)
  // Newest run first: a board is read from the top, and the run you started last is the live one.
  roots.reverse()
  return roots
}

/** Every task id under a node, including its own — what "only this run" filters against. */
export function subtreeIds(node: TaskNode): Set<string> {
  const ids = new Set<string>()
  const walk = (current: TaskNode) => {
    ids.add(current.task.id)
    for (const child of current.children) walk(child)
  }
  walk(node)
  return ids
}

export function countSubtree(node: TaskNode): number {
  return subtreeIds(node).size
}
