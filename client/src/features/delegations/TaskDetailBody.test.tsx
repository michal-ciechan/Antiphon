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


it.each([null, 'feat/card-task-ce744e22'])('C470 renders canonical mutation handoff with ref %s', async (deliverableRef) => {
  const task = { ...detail(), nextStage: 'Mutation', nextHandoff: 'PCs pending; original owner ce744e22',
    deliverablePath: 'docs/superpowers/plans/2026-09-09-card-0470-code-mutation-split-plan.md', deliverableRef }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  expect(await screen.findByText('next: mutation')).toBeInTheDocument()
  expect(screen.getByTestId('task-next-handoff')).toHaveTextContent('PCs pending; original owner ce744e22')
  const link = screen.getByRole('link', { name: task.deliverablePath })
  const target = new URL(link.getAttribute('href')!, 'http://localhost')
  expect(target.pathname).toBe('/plans')
  expect(target.searchParams.get('file')).toBe(task.deliverablePath)
  expect(target.searchParams.get('ref')).toBe(deliverableRef)
  expect(target.searchParams.get('task')).toBe(FLY_ID)
})

it('shows exact approval, source resolution, verification and legacy evidence', async () => {
  const shaA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
  const shaB = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
  const shaP = 'cccccccccccccccccccccccccccccccccccccccc'
  const shaR = 'dddddddddddddddddddddddddddddddddddddddd'
  const task = {
    ...detail(),
    reviewEvidence: {
      id: 'bbbbbbbb-0000-0000-0000-000000000002',
      subjectTaskId: FLY_ID,
      reviewedSourceSha: shaB,
      reviewedSourceRef: 'refs/heads/feat/card-task-aaaaaaaa000000000000000000000001',
      outcome: 'Clean',
    },
    landRequest: {
      id: 'cccccccc-0000-0000-0000-000000000003',
      state: 'Queued',
      requestedAt: '2026-09-11T12:00:00Z',
      startedAt: null,
      lastEvaluatedAt: '2026-09-11T12:00:00Z',
      lastProgressAt: '2026-09-11T12:00:00Z',
      ageSeconds: 12,
      noProgressSeconds: 12,
      attempt: 0,
      holdReasonCode: null,
      holdDetail: null,
      holdingTaskId: null,
      holdingTaskStatus: null,
      heldSince: null,
      holdEpisode: 0,
      reconciliationError: null,
      expectedSourceSha: shaB,
      localBeforeSha: shaA,
      remoteSourceSha: shaR,
      resolvedSourceSha: shaB,
      notifications: [],
    },
    landing: {
      operationId: 'dddddddd-0000-0000-0000-000000000004',
      phase: 'Verified',
      mode: 'Fresh' as const,
      publication: 'Unconfirmed' as const,
      cleanup: 'NotStarted' as const,
      sourceSha: shaB,
      reviewedSha: shaB,
      verifiedSha: shaP,
      sourceRemoteSha: shaR,
      remoteSha: null,
      remoteConfirmedAt: null,
      destinationRef: 'refs/heads/master',
      reason: null,
    },
  }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  expect(await screen.findByText(/Approved original/)).toBeInTheDocument()
  expect(screen.getByText(shaB, { exact: false })).toBeInTheDocument()
  expect(screen.getByText(shaP)).toBeInTheDocument()
  expect(screen.getByText(/Remote source/)).toBeInTheDocument()
  expect(screen.getByText(/Reviewed SHA/)).toBeInTheDocument()
})
