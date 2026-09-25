import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import { agentTaskEnvelope } from '../test/agentTaskEnvelope'
import {
  agentTaskKeys,
  useAgentTaskListSummary,
  useAgentTasks,
  useCancelAgentTask,
  useCreateAgentTask,
  type AgentTaskListSummaryDto,
  type AgentTaskSummaryDto,
} from './agentTasks'

const X = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1'
const Y = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2'
const B1 = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1'
const B2 = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2'

function item(id: string, title: string): AgentTaskSummaryDto {
  return {
    id,
    rootTaskId: id,
    parentTaskId: null,
    depth: 0,
    title,
    kind: 'Worker',
    role: 'Code',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Shared',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    worktreePath: null,
    worktreeBranch: null,
    scope: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-09-13T12:00:00Z',
    dispatchedAt: null,
    completedAt: null,
    recoveredAt: null,
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
    projectId: X,
    projectName: 'Antiphon',
    scopeSource: 'Task',
  }
}

const xItem = item('11111111-1111-1111-1111-111111111111', 'X task')
const yItem = item('22222222-2222-2222-2222-222222222221', 'Y task')
yItem.projectId = Y
yItem.projectName = 'gym-stat'

const emptySummary: AgentTaskListSummaryDto = {
  active: 0,
  blocked: 0,
  runs: 0,
  totalCostUsd: 0,
  byStatus: {},
}

describe('agentTasks create', () => {
  it('posts an explicit platform and runner and keeps an omitted platform off the body', async () => {
    const bodies: Array<Record<string, unknown>> = []
    server.use(http.post('/api/agent-tasks', async ({ request }) => {
      bodies.push((await request.json()) as Record<string, unknown>)
      return HttpResponse.json({ id: 'task-1', shortId: 'task-1', status: 'Queued', modelLevel: 'High' })
    }))
    const explicit = renderHookWithProviders(() => useCreateAgentTask())
    explicit.result.current.mutate({ goal: 'linux work', requiredPlatform: 'Linux', runnerId: 'server2' })
    await waitFor(() => expect(bodies[0]).toMatchObject({ goal: 'linux work', requiredPlatform: 'Linux', runnerId: 'server2' }))

    const omitted = renderHookWithProviders(() => useCreateAgentTask())
    omitted.result.current.mutate({ goal: 'inherit the card' })
    await waitFor(() => expect(bodies).toHaveLength(2))
    expect(bodies[1]).not.toHaveProperty('requiredPlatform')
    expect(bodies[1]).not.toHaveProperty('runnerId')
  })
})

describe('agentTasks scope', () => {
  it('list requests preserve every scope parameter', async () => {
    const captured: URLSearchParams[] = []
    server.use(
      http.get('/api/agent-tasks', ({ request }) => {
        captured.push(new URL(request.url).searchParams)
        return HttpResponse.json(agentTaskEnvelope([xItem]))
      }),
    )
    const { result } = renderHookWithProviders(() =>
      useAgentTasks(false, { projectId: X, boardId: B1, unscoped: 'include', since: '2026-09-01T00:00:00Z', status: ['Working'] }),
    )
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    const params = captured[0]
    expect(params.get('projectId')).toBe(X)
    expect(params.get('boardId')).toBe(B1)
    expect(params.get('unscoped')).toBe('include')
    expect(params.get('since')).toBe('2026-09-01T00:00:00Z')
    expect(params.get('status')).toBe('Working')
  })

  it('summary requests preserve every scope parameter', async () => {
    const captured: URLSearchParams[] = []
    server.use(
      http.get('/api/agent-tasks/summary', ({ request }) => {
        captured.push(new URL(request.url).searchParams)
        return HttpResponse.json(emptySummary)
      }),
    )
    const { result } = renderHookWithProviders(() =>
      useAgentTaskListSummary({ projectId: X, boardId: B1, unscoped: 'only' }),
    )
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(captured[0].get('projectId')).toBe(X)
    expect(captured[0].get('boardId')).toBe(B1)
    expect(captured[0].get('unscoped')).toBe('only')
  })

  it('list caches cannot cross scopes', async () => {
    server.use(
      http.get('/api/agent-tasks', ({ request }) => {
        const projectId = new URL(request.url).searchParams.get('projectId')
        return HttpResponse.json(agentTaskEnvelope(projectId === Y ? [yItem] : [xItem]))
      }),
    )
    const { result: x } = renderHookWithProviders(() => useAgentTasks(false, { projectId: X }))
    await waitFor(() => expect(x.current.data?.items.map((row) => row.id)).toEqual([xItem.id]))
    const { result: y } = renderHookWithProviders(() => useAgentTasks(false, { projectId: Y }))
    await waitFor(() => expect(y.current.data?.items.map((row) => row.id)).toEqual([yItem.id]))
    expect(x.current.data?.items.map((row) => row.id)).toEqual([xItem.id])
  })

  it('summary caches cannot cross scopes', async () => {
    server.use(
      http.get('/api/agent-tasks/summary', ({ request }) => {
        const projectId = new URL(request.url).searchParams.get('projectId')
        return HttpResponse.json({
          ...emptySummary,
          active: projectId === Y ? 3 : 1,
        })
      }),
    )
    const { result: x } = renderHookWithProviders(() => useAgentTaskListSummary({ projectId: X }))
    await waitFor(() => expect(x.current.data?.active).toBe(1))
    const { result: y } = renderHookWithProviders(() => useAgentTaskListSummary({ projectId: Y }))
    await waitFor(() => expect(y.current.data?.active).toBe(3))
    expect(x.current.data?.active).toBe(1)
  })

  it('task mutations invalidate every scoped summary', async () => {
    server.use(
      http.post('/api/agent-tasks/:id/cancel', () => HttpResponse.json(xItem)),
    )
    const { result, queryClient } = renderHookWithProviders(() => useCancelAgentTask())
    queryClient.setQueryData(agentTaskKeys.summary({ projectId: X }), { ...emptySummary, active: 1 })
    queryClient.setQueryData(agentTaskKeys.summary({ boardId: B1 }), { ...emptySummary, active: 2 })
    await result.current.mutateAsync(xItem.id)
    expect(queryClient.getQueryState(agentTaskKeys.summary({ projectId: X }))?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(agentTaskKeys.summary({ boardId: B1 }))?.isInvalidated).toBe(true)
  })

  it('omitted and exclude unscoped share a cache key', () => {
    expect(agentTaskKeys.list(false, { projectId: X })).toEqual(
      agentTaskKeys.list(false, { projectId: X, unscoped: 'exclude' }),
    )
    expect(agentTaskKeys.summary({ boardId: B2 })).toEqual(
      agentTaskKeys.summary({ boardId: B2, unscoped: 'exclude' }),
    )
  })
})
