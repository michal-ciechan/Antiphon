import type { AgentSummaryDto } from '../../api/agents'
import type { AgentTaskSummaryDto, AgentTaskStatus } from '../../api/agentTasks'
import type { WorkspaceGitInfo, WorkspaceWorktrees } from '../../api/filesystem'

/**
 * A "project" on the home page is a repo — every directory that git says belongs to the same
 * main checkout (the checkout itself, its subdirectories, its linked worktrees) folds into one
 * group. Directories git knows nothing about stay their own project, which is also the exact
 * pre-git-info behaviour, so grouping degrades gracefully while the workspace lookup loads.
 */
export interface ProjectGroup {
  /** Normalised directory — the identity used for selection persistence and task matching. */
  key: string
  /** Display path in first-seen casing. */
  path: string
  /** Trailing path segment, widened to two segments when two projects would collide. */
  label: string
  /** Branch checked out in the main directory, when known. */
  branch: string | null
  /** Every agent across the project's workspaces. */
  agents: AgentSummaryDto[]
  /** Queued/Dispatched/Working/Blocked delegations anywhere in this project. */
  activeTaskCount: number
  /** The switchable directories: main first, then subdirectories, then worktrees. */
  workspaces: WorkspaceEntry[]
}

export type WorkspaceKind = 'main' | 'subdir' | 'worktree'

/** One switchable directory inside a project: the main checkout, a subdirectory, or a worktree. */
export interface WorkspaceEntry {
  key: string
  path: string
  /** 'main' for the checkout root; otherwise the relative path or last segment. */
  label: string
  kind: WorkspaceKind
  branch: string | null
  agents: AgentSummaryDto[]
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

/** The directory a task actually RUNS in — a worktree task's checkout, everyone else's cwd. */
export function taskRunDir(task: AgentTaskSummaryDto): string {
  return task.worktreePath ?? task.workingDirectory
}

function segments(path: string): string[] {
  return path.split(/[\\/]/).filter(Boolean)
}

const KIND_ORDER: Record<WorkspaceKind, number> = { main: 0, subdir: 1, worktree: 2 }

/**
 * Group agents by repo (falling back to raw working directory when git info is absent);
 * directories that only have delegations still count — a project you delegated into is a
 * project. Sorted by label for a stable switcher.
 */
export function buildProjects(
  agents: AgentSummaryDto[],
  tasks: AgentTaskSummaryDto[] = [],
  gitInfos: WorkspaceGitInfo[] = [],
): ProjectGroup[] {
  const infoByDir = new Map<string, WorkspaceGitInfo>()
  for (const info of gitInfos) infoByDir.set(normalizeDir(info.path), info)

  const byKey = new Map<string, ProjectGroup>()

  const projectPathOf = (dir: string): string =>
    infoByDir.get(normalizeDir(dir))?.repoRoot ?? dir

  const ensureProject = (path: string): ProjectGroup | null => {
    const trimmed = path.trim()
    if (!trimmed) return null
    const key = normalizeDir(trimmed)
    let group = byKey.get(key)
    if (!group) {
      group = {
        key,
        path: trimmed.replace(/[\\/]+$/, ''),
        label: '',
        branch: null,
        agents: [],
        activeTaskCount: 0,
        workspaces: [],
      }
      byKey.set(key, group)
    }
    return group
  }

  const ensureWorkspace = (project: ProjectGroup, path: string): WorkspaceEntry => {
    const trimmed = path.trim().replace(/[\\/]+$/, '')
    const key = normalizeDir(trimmed)
    let ws = project.workspaces.find((w) => w.key === key)
    if (!ws) {
      const info = infoByDir.get(key)
      const kind: WorkspaceKind =
        key === project.key
          ? 'main'
          : info?.isWorktree
            ? 'worktree'
            : key.startsWith(project.key + '\\')
              ? 'subdir'
              : 'worktree'
      ws = {
        key,
        path: trimmed,
        label: '',
        kind,
        branch: info?.branch ?? null,
        agents: [],
        activeTaskCount: 0,
      }
      project.workspaces.push(ws)
    }
    return ws
  }

  for (const agent of agents) {
    const project = ensureProject(projectPathOf(agent.workingDirectory))
    if (!project) continue
    project.agents.push(agent)
    ensureWorkspace(project, agent.workingDirectory).agents.push(agent)
  }
  for (const task of tasks) {
    if (!isActiveTask(task)) continue
    const project = ensureProject(projectPathOf(taskProjectDir(task)))
    if (!project) continue
    project.activeTaskCount += 1
    const ws = ensureWorkspace(project, taskRunDir(task))
    ws.activeTaskCount += 1
    if (!ws.branch && task.worktreeBranch) ws.branch = task.worktreeBranch
  }

  const groups = [...byKey.values()]
  for (const g of groups) {
    ensureWorkspace(g, g.path) // every project can always switch back to its main directory
    finalizeWorkspaces(g)
  }

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

/** Labels and ordering for a project's workspaces; main's branch becomes the project's branch. */
function finalizeWorkspaces(project: ProjectGroup): void {
  for (const ws of project.workspaces) {
    if (ws.kind === 'main') {
      ws.label = 'main'
    } else if (ws.key.startsWith(project.key + '\\')) {
      // Same length pre/post normalisation, so slicing the display path by the key offset is safe.
      ws.label = ws.path.slice(project.path.length + 1) || (segments(ws.path).at(-1) ?? ws.path)
    } else {
      ws.label = segments(ws.path).at(-1) ?? ws.path
    }
  }
  sortWorkspaces(project.workspaces)
  project.branch = project.workspaces.find((w) => w.kind === 'main')?.branch ?? null
}

function sortWorkspaces(workspaces: WorkspaceEntry[]): void {
  workspaces.sort(
    (a, b) =>
      KIND_ORDER[a.kind] - KIND_ORDER[b.kind] ||
      a.label.localeCompare(b.label, undefined, { sensitivity: 'base' }),
  )
}

/**
 * Fold the selected project's live `git worktree list` into its workspaces: worktrees nobody has
 * an agent or task in become switchable entries, and branch names refresh from git truth. Pure —
 * returns a new group, the input is untouched.
 */
export function mergeWorktrees(
  project: ProjectGroup,
  listing: WorkspaceWorktrees | undefined,
): ProjectGroup {
  if (!listing || listing.worktrees.length === 0) return project
  const workspaces = project.workspaces.map((w) => ({ ...w }))
  for (const wt of listing.worktrees) {
    const key = normalizeDir(wt.path)
    const existing = workspaces.find((w) => w.key === key)
    if (existing) {
      if (wt.branch) existing.branch = wt.branch
    } else if (!wt.isMain) {
      // A non-matching MAIN entry means the project was grouped without git info (its own dir
      // is a worktree of some other checkout) — adding the real root here would double it up.
      workspaces.push({
        key,
        path: wt.path,
        label: segments(wt.path).at(-1) ?? wt.path,
        kind: 'worktree',
        branch: wt.branch,
        agents: [],
        activeTaskCount: 0,
      })
    }
  }
  sortWorkspaces(workspaces)
  const branch = workspaces.find((w) => w.kind === 'main')?.branch ?? null
  return { ...project, branch, workspaces }
}

/** The workspace to preselect: the remembered one when it still exists, else main. */
export function pickWorkspace(
  project: ProjectGroup | null,
  rememberedKey: string | null,
): WorkspaceEntry | null {
  if (!project) return null
  return (
    project.workspaces.find((w) => w.key === rememberedKey) ??
    project.workspaces.find((w) => w.kind === 'main') ??
    project.workspaces[0] ??
    null
  )
}

/**
 * The agent to preselect for a project or workspace: the remembered one when it still exists,
 * else the first with a live session, else the first agent.
 */
export function pickAgent(
  scope: { agents: AgentSummaryDto[] } | null,
  rememberedId: string | null,
): AgentSummaryDto | null {
  if (!scope || scope.agents.length === 0) return null
  return (
    scope.agents.find((a) => a.id === rememberedId) ??
    scope.agents.find((a) => a.liveSession != null) ??
    scope.agents[0]
  )
}
