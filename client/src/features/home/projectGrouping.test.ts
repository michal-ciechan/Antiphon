import { describe, expect, it } from 'vitest'
import type { AgentSummaryDto } from '../../api/agents'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { buildProjects, normalizeDir, pickAgent, taskProjectDir } from './projectGrouping'

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
    scopeGlob: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-08T00:00:00Z',
    dispatchedAt: null,
    completedAt: null,
    tokensIn: 0,
    tokensOut: 0,
    costUsd: 0,
    subtreeCostUsd: 0,
    childCount: 0,
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
