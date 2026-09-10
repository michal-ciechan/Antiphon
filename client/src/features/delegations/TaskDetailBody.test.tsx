import { HttpResponse, http } from 'msw'
import { expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { TaskDetailBody } from './TaskDetailBody'
vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
const FLY_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
function summary(overrides: Partial<AgentTaskSummaryDto> = {}): AgentTaskSummaryDto {
  return {
    id: FLY_ID,
    rootTaskId: FLY_ID,
    parentTaskId: null,
    depth: 0,
    title: 'Phone-friendly pipeline-stage view',
    kind: 'Worker',
    role: 'Plan',
    agentKind: 'Grok',
    modelLevel: 'Frontier',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Worktree',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    worktreePath: null,
    worktreeBranch: null,
    scope: null,
    agentId: 'agent-1',
    agentName: 'task-fly',
    agentSessionId: 'session-1',
    attempt: 1,
    createdAt: '2026-02-03T09:00:00Z',
    dispatchedAt: '2026-02-03T09:10:00Z',
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
    expectedDurationMinutes: 30,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
}

function detail(): AgentTaskDetailDto {
  return {
    summary: summary(),
    goal: 'Build the phone stage glance.',
    result: null,
    resultFilePath: null,
    failureReason: null,
    mergeTargetRef: null,
    events: [{ type: 'Created', modelLevel: 'Frontier', detail: 'Created.', at: '2026-02-03T09:00:00Z' }],
  }
}


it('C470 renders canonical mutation handoff', async () => {
  const task = { ...detail(), nextStage: 'Mutation', nextHandoff: 'PCs pending; original owner ce744e22',
    deliverablePath: 'docs/superpowers/plans/2026-09-09-card-0470-code-mutation-split-plan.md' }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  expect(await screen.findByText('next: mutation')).toBeInTheDocument()
  expect(screen.getByTestId('task-next-handoff')).toHaveTextContent('PCs pending; original owner ce744e22')
  expect(screen.getByText(task.deliverablePath)).toBeInTheDocument()
})
