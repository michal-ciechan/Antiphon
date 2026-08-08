import type { AgentSummaryDto } from '../../api/agents'
import type { AgentTaskSummaryDto, AgentTaskStatus } from '../../api/agentTasks'

/**
 * A "project" on the home page is a distinct agent working directory — nothing more is modelled.
 * The server already keys pooling, delegation, and scope on the directory string, so the client
 * derives the same grouping instead of inventing a second source of truth.
 */
export interface ProjectGroup {
  /** Normalised directory — the identity used for selection persistence and task matching. */
  key: string
  /** Display path in first-seen casing. */
  path: string
  /** Trailing path segment, widened to two segments when two projects would collide. */
  label: string
  agents: AgentSummaryDto[]
  /** Queued/Dispatched/Working/Blocked delegations running in this directory. */
  activeTaskCount: number
}

/** Case-insensitive, separator- and trailing-slash-normalised directory identity. */
export function normalizeDir(dir: string): string {
  return dir.trim().replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase()
}

const ACTIVE_STATUSES: ReadonlySet<AgentTaskStatus> = new Set([
  'Queued',
  'Dispatched',
  'Working',
  'Blocked',
])

export function isActiveTask(task: AgentTaskSummaryDto): boolean {
  return ACTIVE_STATUSES.has(task.status)
}

/**
 * The directory a task belongs to for grouping. Worktree tasks run in a throwaway checkout — their
 * repoPath points back at the repo they came from, which is the project the user thinks in.
 */
export function taskProjectDir(task: AgentTaskSummaryDto): string {
  return task.repoPath ?? task.workingDirectory
}

function segments(path: string): string[] {
  return path.split(/[\\/]/).filter(Boolean)
}

/**
 * Group agents by working directory; directories that only have delegations (no standing agent)
 * still count — a project you delegated into is a project. Sorted by label for a stable switcher.
 */
export function buildProjects(
  agents: AgentSummaryDto[],
  tasks: AgentTaskSummaryDto[] = [],
): ProjectGroup[] {
  const byKey = new Map<string, ProjectGroup>()

  const ensure = (path: string): ProjectGroup | null => {
    const trimmed = path.trim()
    if (!trimmed) return null
    const key = normalizeDir(trimmed)
    let group = byKey.get(key)
    if (!group) {
      group = { key, path: trimmed.replace(/[\\/]+$/, ''), label: '', agents: [], activeTaskCount: 0 }
      byKey.set(key, group)
    }
    return group
  }

  for (const agent of agents) {
    ensure(agent.workingDirectory)?.agents.push(agent)
  }
  for (const task of tasks) {
    if (!isActiveTask(task)) continue
    const group = ensure(taskProjectDir(task))
    if (group) group.activeTaskCount += 1
  }

  const groups = [...byKey.values()]

  // Label = last segment; widen to the last two when two projects would read the same.
  const lastSegment = (g: ProjectGroup) => segments(g.path).at(-1) ?? g.path
  const counts = new Map<string, number>()
  for (const g of groups) {
    const label = lastSegment(g).toLowerCase()
    counts.set(label, (counts.get(label) ?? 0) + 1)
  }
  for (const g of groups) {
    const last = lastSegment(g)
    if ((counts.get(last.toLowerCase()) ?? 0) > 1) {
      g.label = segments(g.path).slice(-2).join('\\')
    } else {
      g.label = last
    }
  }

  return groups.sort((a, b) => a.label.localeCompare(b.label, undefined, { sensitivity: 'base' }))
}

/**
 * The agent to preselect for a project: the remembered one when it still exists, else the first
 * with a live session, else the first agent.
 */
export function pickAgent(
  project: ProjectGroup | null,
  rememberedId: string | null,
): AgentSummaryDto | null {
  if (!project || project.agents.length === 0) return null
  return (
    project.agents.find((a) => a.id === rememberedId) ??
    project.agents.find((a) => a.liveSession != null) ??
    project.agents[0]
  )
}
