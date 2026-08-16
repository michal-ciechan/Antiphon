import { describe, expect, it } from 'vitest'
import type { AgentSummaryDto } from '../../api/agents'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import type { WorkspaceGitInfo } from '../../api/filesystem'
import {
  buildProjects,
  mergeWorktrees,
  normalizeDir,
  pickAgent,
  pickWorkspace,
  taskProjectDir,
} from './projectGrouping'

function agent(overrides: Partial<AgentSummaryDto>): AgentSummaryDto {
  return {
    id: 'a1',
    name: 'agent',
    slug: 'agent',
    workingDirectory: 'C:\\src\\antiphon',
    details: '',
    defaultWorkflowTemplateId: null,
    defaultWorkflowTemplateName: null,
    assignmentPolicy: 'AutoPick',
    status: 'Idle',
    persistentSessionId: null,
    currentCardId: null,
    boardId: null,
    boardName: null,
    queueLength: 0,
    createdAt: '2026-08-08T00:00:00Z',
    updatedAt: '2026-08-08T00:00:00Z',
    liveSession: null,
    alwaysOn: false,
    remoteControlEnabled: false,
    supervision: null,
    systemPromptAppend: null,
    modelLevel: 'High',
    working: false,
    ...overrides,
  }
}

function task(overrides: Partial<AgentTaskSummaryDto>): AgentTaskSummaryDto {
  return {
    id: 't1',
    rootTaskId: 't1',
    parentTaskId: null,
    depth: 0,
    title: 'a task',
    kind: 'Worker',
    role: 'Code',
    modelLevel: 'Frontier',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Shared',
    workingDirectory: 'C:\\src\\antiphon',
    repoPath: null,
    worktreePath: null,
    worktreeBranch: null,
    scopeGlob: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-08T00:00:00Z',
    dispatchedAt: null,
    completedAt: null,
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 0,
    costPricingVersion: 2,
    subtreeCostUsd: 0,
    childCount: 0,
    expectedDurationMinutes: 10,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
}

describe('normalizeDir', () => {
  it('is case-, separator-, and trailing-slash-insensitive', () => {
    expect(normalizeDir('C:/src/Antiphon/')).toBe('c:\\src\\antiphon')
    expect(normalizeDir('C:\\src\\antiphon')).toBe('c:\\src\\antiphon')
  })
})

describe('buildProjects', () => {
  it('groups agents by working directory, one project per distinct dir', () => {
    const projects = buildProjects([
      agent({ id: 'a1', workingDirectory: 'C:\\src\\antiphon' }),
      agent({ id: 'a2', workingDirectory: 'c:/src/antiphon/' }),
      agent({ id: 'a3', workingDirectory: 'C:\\src\\am-service' }),
    ])
    expect(projects.map((p) => p.label)).toEqual(['am-service', 'antiphon'])
    expect(projects[1].agents.map((a) => a.id)).toEqual(['a1', 'a2'])
  })

  it('a directory with only delegations is still a project', () => {
    // You delegated into it — the pool may spawn an agent there any moment.
    const projects = buildProjects([], [task({ workingDirectory: 'C:\\src\\other' })])
    expect(projects).toHaveLength(1)
    expect(projects[0].agents).toHaveLength(0)
    expect(projects[0].activeTaskCount).toBe(1)
  })

  it('counts only in-flight tasks and maps worktree tasks back to their repo', () => {
    const projects = buildProjects(
      [agent({ workingDirectory: 'C:\\src\\antiphon' })],
      [
        task({ status: 'Queued' }),
        task({ id: 't2', status: 'Succeeded' }), // done — not "active"
        task({
          id: 't3',
          status: 'Working',
          workspace: 'Worktree',
          workingDirectory: 'C:\\src\\antiphon\\.worktrees\\task-abc',
          repoPath: 'C:\\src\\antiphon',
        }),
      ],
    )
    expect(projects).toHaveLength(1)
    expect(projects[0].activeTaskCount).toBe(2)
  })

  it('widens colliding labels to two segments', () => {
    const projects = buildProjects([
      agent({ id: 'a1', workingDirectory: 'C:\\clients\\alpha\\app' }),
      agent({ id: 'a2', workingDirectory: 'C:\\clients\\beta\\app' }),
    ])
    expect(projects.map((p) => p.label).sort()).toEqual(['alpha\\app', 'beta\\app'])
  })
})

function info(overrides: Partial<WorkspaceGitInfo> & { path: string }): WorkspaceGitInfo {
  return { isGitRepository: true, repoRoot: null, branch: null, isWorktree: false, ...overrides }
}

describe('buildProjects with git info', () => {
  it('nests worktree- and subdirectory-scoped agents under their repo', () => {
    const projects = buildProjects(
      [
        agent({ id: 'root', workingDirectory: 'C:\\src\\antiphon' }),
        agent({ id: 'wt', workingDirectory: 'C:\\Antiphon\\worktrees\\card-1' }),
        agent({ id: 'sub', workingDirectory: 'C:\\src\\antiphon\\client' }),
      ],
      [],
      [
        info({ path: 'C:\\src\\antiphon', repoRoot: 'C:\\src\\antiphon', branch: 'master' }),
        info({
          path: 'C:\\Antiphon\\worktrees\\card-1',
          repoRoot: 'C:\\src\\antiphon',
          branch: 'feat/card-1',
          isWorktree: true,
        }),
        info({ path: 'C:\\src\\antiphon\\client', repoRoot: 'C:\\src\\antiphon' }),
      ],
    )

    expect(projects).toHaveLength(1)
    const p = projects[0]
    expect(p.agents.map((a) => a.id).sort()).toEqual(['root', 'sub', 'wt'])
    expect(p.branch).toBe('master')
    // Main first, then subdirectories, then worktrees.
    expect(p.workspaces.map((w) => `${w.kind}:${w.label}`)).toEqual([
      'main:main',
      'subdir:client',
      'worktree:card-1',
    ])
    const worktree = p.workspaces[2]
    expect(worktree.branch).toBe('feat/card-1')
    expect(worktree.agents.map((a) => a.id)).toEqual(['wt'])
  })

  it('a worktree task lands on its checkout workspace, branch included', () => {
    const projects = buildProjects(
      [agent({})],
      [
        task({
          workspace: 'Worktree',
          workingDirectory: 'C:\\src\\antiphon',
          repoPath: 'C:\\src\\antiphon',
          worktreePath: 'C:\\wt\\task-1',
          worktreeBranch: 'task/1',
        }),
      ],
    )

    expect(projects).toHaveLength(1)
    expect(projects[0].activeTaskCount).toBe(1)
    const wt = projects[0].workspaces.find((w) => w.kind === 'worktree')
    expect(wt?.path).toBe('C:\\wt\\task-1')
    expect(wt?.branch).toBe('task/1')
    expect(wt?.activeTaskCount).toBe(1)
    // The main workspace carries no share of the worktree task.
    expect(projects[0].workspaces.find((w) => w.kind === 'main')?.activeTaskCount).toBe(0)
  })

  it('without git info every directory stays its own project (graceful degrade)', () => {
    const projects = buildProjects([
      agent({ id: 'root', workingDirectory: 'C:\\src\\antiphon' }),
      agent({ id: 'wt', workingDirectory: 'C:\\Antiphon\\worktrees\\card-1' }),
    ])
    expect(projects).toHaveLength(2)
    expect(projects.every((p) => p.workspaces.length === 1)).toBe(true)
    expect(projects.every((p) => p.workspaces[0].kind === 'main')).toBe(true)
  })
})

describe('mergeWorktrees', () => {
  const listing = (worktrees: Array<Partial<import('../../api/filesystem').WorktreeEntry> & { path: string }>) => ({
    path: 'C:\\src\\antiphon',
    isGitRepository: true,
    repoRoot: 'C:\\src\\antiphon',
    worktrees: worktrees.map((w) => ({
      branch: null,
      isMain: false,
      isLocked: false,
      isDetached: false,
      ...w,
    })),
  })

  it('adds unclaimed worktrees and refreshes the main branch from git truth', () => {
    const project = buildProjects([agent({})])[0]
    const merged = mergeWorktrees(
      project,
      listing([
        { path: 'C:\\src\\antiphon', branch: 'master', isMain: true },
        { path: 'C:\\Antiphon\\worktrees\\card-2', branch: 'feat/card-2' },
      ]),
    )

    expect(merged.branch).toBe('master')
    expect(merged.workspaces.map((w) => w.label)).toEqual(['main', 'card-2'])
    expect(merged.workspaces[1].kind).toBe('worktree')
    expect(merged.workspaces[1].branch).toBe('feat/card-2')
    expect(merged.workspaces[1].agents).toHaveLength(0)
    // Pure: the input project was not mutated.
    expect(project.workspaces).toHaveLength(1)
    expect(project.branch).toBeNull()
  })

  it('returns the project untouched for an absent or empty listing', () => {
    const project = buildProjects([agent({})])[0]
    expect(mergeWorktrees(project, undefined)).toBe(project)
    expect(mergeWorktrees(project, listing([]))).toBe(project)
  })
})

describe('pickWorkspace', () => {
  it('honours the remembered workspace, else falls back to main', () => {
    const project = buildProjects(
      [
        agent({ id: 'root', workingDirectory: 'C:\\src\\antiphon' }),
        agent({ id: 'wt', workingDirectory: 'C:\\wt\\card-1' }),
      ],
      [],
      [info({ path: 'C:\\wt\\card-1', repoRoot: 'C:\\src\\antiphon', isWorktree: true })],
    )[0]

    expect(pickWorkspace(project, 'c:\\wt\\card-1')?.kind).toBe('worktree')
    expect(pickWorkspace(project, 'c:\\gone')?.kind).toBe('main')
    expect(pickWorkspace(null, null)).toBeNull()
  })

  it('an agent-less main loses the default to the first workspace with agents', () => {
    // A repo whose agents all live in subdirectories (ClaudeBot's agents/* layout) —
    // landing on empty main would show a bare rail for no reason.
    const project = buildProjects(
      [
        agent({ id: 'care', workingDirectory: 'C:\\src\\claudebot\\agents\\az-care' }),
        agent({ id: 'fam', workingDirectory: 'C:\\src\\claudebot\\agents\\family' }),
      ],
      [],
      [
        info({ path: 'C:\\src\\claudebot\\agents\\az-care', repoRoot: 'C:\\src\\claudebot' }),
        info({ path: 'C:\\src\\claudebot\\agents\\family', repoRoot: 'C:\\src\\claudebot' }),
      ],
    )[0]

    const picked = pickWorkspace(project, null)
    expect(picked?.kind).toBe('subdir')
    expect(picked?.agents[0]?.id).toBe('care')
  })
})

describe('taskProjectDir', () => {
  it('prefers the repo a worktree task came from', () => {
    expect(
      taskProjectDir(task({ workingDirectory: 'C:\\wt\\x', repoPath: 'C:\\src\\antiphon' })),
    ).toBe('C:\\src\\antiphon')
    expect(taskProjectDir(task({ repoPath: null }))).toBe('C:\\src\\antiphon')
  })
})

describe('pickAgent', () => {
  const project = {
    key: 'c:\\src\\antiphon',
    path: 'C:\\src\\antiphon',
    label: 'antiphon',
    activeTaskCount: 0,
    agents: [
      agent({ id: 'cold' }),
      agent({
        id: 'live',
        liveSession: { id: 's1', status: 'Running' } as AgentSummaryDto['liveSession'],
      }),
    ],
  }

  it('honours the remembered agent when it still exists', () => {
    expect(pickAgent(project, 'cold')?.id).toBe('cold')
  })

  it('falls back to the first live agent, then the first agent', () => {
    expect(pickAgent(project, 'gone')?.id).toBe('live')
    expect(pickAgent({ ...project, agents: [project.agents[0]] }, null)?.id).toBe('cold')
  })

  it('returns null for an empty project', () => {
    expect(pickAgent({ ...project, agents: [] }, null)).toBeNull()
    expect(pickAgent(null, null)).toBeNull()
  })
})
