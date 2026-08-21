import type { AgentModelLevel } from '../../api/agents'
import type { AgentTaskStatus, AgentTaskSummaryDto, WorkspaceMode } from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'

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

/**
 * Grok's ladder is SHORTER than the tier axis — xAI ships two models, so Frontier and High are both
 * grok-4.6 and Medium and Low are both grok-4.5. The rungs still differ (a Medium task is cheaper
 * and gets a fresh context), which is why only the NAME collapses here and the violet intensity in
 * TIER_VISUALS is left alone.
 */
const GROK_ALIASES: Record<AgentModelLevel, string> = {
  Frontier: 'grok-4.6',
  High: 'grok-4.6',
  Medium: 'grok-4.5',
  Low: 'grok-4.5',
}

/**
 * The model-family alias for the program the task ACTUALLY runs on — the client mirror of the
 * server's `ModelLevelAliases.For(kind, level)` (CARD-0084 S4). A Grok task whose chip read `fable`
 * was naming a model nobody was paying for, on the one surface an operator scans to decide what to
 * escalate.
 *
 * Anything that is not Grok takes the Claude ladder in TIER_VISUALS, exactly as the server does, so
 * every pre-CARD-0084 chip is byte-identical. That fallback is only safe while ClaudeCode and Grok
 * are the sole delegatable kinds — a third one must add its ladder HERE and on the server together,
 * or its tasks silently read as Claude.
 */
export function tierAlias(level: AgentModelLevel, kind: AgentKind = 'ClaudeCode'): string {
  return kind === 'Grok' ? GROK_ALIASES[level] : TIER_VISUALS[level].alias
}

/**
 * What the tier badge's tooltip says. Claude's aliases are bare family words, so they need the
 * vendor in front to mean anything ("Claude fable"); Grok's already carry it, and "Grok grok-4.6"
 * is a stutter. Claude's text is unchanged, byte for byte.
 */
export function tierTooltip(level: AgentModelLevel, kind: AgentKind = 'ClaudeCode'): string {
  const alias = tierAlias(level, kind)
  return `${level} tier — ${kind === 'Grok' ? alias : `Claude ${alias}`}`
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

/** A recovery settlement has a timestamp, but it did not observe the delegate finish its work. */
export function completionObserved(task: AgentTaskSummaryDto): boolean {
  return task.recoveredAt == null
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

/**
 * Everything the model read this session. The three input counters are stored apart because they
 * are PRICED apart (CARD-0023) — but a human reading the board means their sum by "tokens in".
 */
export function totalTokens(task: AgentTaskSummaryDto): number {
  return task.tokensIn + task.cacheReadTokens + task.cacheCreationTokens + task.tokensOut
}

/**
 * A cost figure written by the pre-CARD-0023 model: cache reads billed as fresh input against a
 * stale rate table, so roughly an order of magnitude high. Those rows are labelled rather than
 * silently trusted — the per-root ceiling still sums them.
 */
export function isLegacyCostEstimate(task: AgentTaskSummaryDto): boolean {
  return task.costPricingVersion === 0 && task.costUsd > 0
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
