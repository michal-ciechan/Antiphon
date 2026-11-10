import { HttpResponse, http } from 'msw'
import { expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent } from '../../test/utils'
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

it('shows the frozen requirement separately from the observed host', async () => {
  const task = {
    ...detail(),
    summary: summary({
      status: 'Queued',
      requiredPlatform: 'Any',
      runnerId: 'server2',
      observedPlatform: 'linux',
      requirementSource: 'Default',
    }),
  }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  const badge = await screen.findByTestId('placement')
  expect(badge).toHaveTextContent('Any · server2')
  expect(badge).not.toHaveTextContent('linux')
  await userEvent.hover(badge)
  expect(await screen.findByText(/Requirement: Any/)).toBeInTheDocument()
  expect(screen.getByText(/Observed: linux/)).toBeInTheDocument()
  expect(screen.getByText(/Queued; this task has not executed/)).toBeInTheDocument()
})

it('shows session liveness under a failed task', async () => {
  const task: AgentTaskDetailDto = {
    ...detail(), summary: summary({ status: 'Failed' }), failureReason: 'API error',
    session: {
      sessionId: 'session-1', status: 'Running', working: true,
      lastSeenAt: '2026-09-14T12:00:00Z', endedAt: null, lastTranscriptAt: '2026-09-14T12:00:00Z',
    },
  }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  expect(await screen.findByText('still working')).toBeInTheDocument()
  expect(screen.getByTestId('task-session-liveness')).toHaveTextContent('Session: session-1 Running, working; last transcript 2026-09-14T12:00:00Z')
  expect(screen.getByText('API error')).toBeInTheDocument()
})

it('omits it when the detail has no session', async () => {
  const task = { ...detail(), summary: summary({ status: 'Failed' }), failureReason: 'API error' }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  expect(await screen.findByText('API error')).toBeInTheDocument()
  expect(screen.queryByTestId('task-session-liveness')).not.toBeInTheDocument()
  expect(screen.queryByText('still working')).not.toBeInTheDocument()
})


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

it('C1467 shows a memory-floor wait instead of no progress', async () => {
  const waiting = 'waiting for memory (available 4700 MB, floor 6144 MB, waited 1 min)'
  const task: AgentTaskDetailDto = {
    ...detail(),
    landRequest: {
      id: 'cccccccc-0000-0000-0000-000000000003', state: 'Running',
      requestedAt: '2026-10-22T13:49:55Z', startedAt: '2026-10-22T13:49:55Z',
      lastEvaluatedAt: '2026-10-22T13:54:55Z', lastProgressAt: '2026-10-22T13:49:55Z',
      ageSeconds: 300, noProgressSeconds: 300, attempt: 0,
      holdReasonCode: null, holdDetail: null, holdingTaskId: null, holdingTaskStatus: null,
      heldSince: null, holdEpisode: 0, reconciliationError: null, notifications: [],
      verificationWait: {
        kind: 'WaitingForMemory', availableMb: 4700, floorMb: 6144, waitedSeconds: 60,
        maxCpuCount: 0, observedAt: '2026-10-22T13:50:55Z', description: waiting,
      },
    },
  }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  expect(await screen.findByText(waiting, { exact: false })).toBeInTheDocument()
  expect(screen.queryByText(/no progress for/)).not.toBeInTheDocument()
})

it.each(['status_untrusted', 'source_dirty'])('C1715 shows the source refusal in task detail: %s', async (reason) => {
  const task: AgentTaskDetailDto = {
    ...detail(),
    landRequest: {
      id: 'cccccccc-0000-0000-0000-000000000003', state: 'Failed',
      requestedAt: '2026-09-11T12:00:00Z', startedAt: null,
      lastEvaluatedAt: '2026-09-11T12:00:00Z', lastProgressAt: '2026-09-11T12:00:00Z',
      ageSeconds: 12, noProgressSeconds: 12, attempt: 1,
      holdReasonCode: null, holdDetail: null, holdingTaskId: null, holdingTaskStatus: null,
      heldSince: null, holdEpisode: 0, reconciliationError: null, notifications: [],
      sourceRefusalReason: reason,
    },
  }
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(task)))
  renderWithProviders(<TaskDetailBody taskId={FLY_ID} onClose={() => {}} />)
  const refusal = await screen.findByTestId('source-refusal')
  expect(refusal).toHaveTextContent(`Source refusal: ${reason}`)
  if (reason === 'status_untrusted') {
    expect(refusal).toHaveTextContent('Git status wrote to stderr; worktree cleanliness could not be verified.')
  } else {
    expect(refusal).not.toHaveTextContent('Git status wrote to stderr')
  }
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
  const approved = await screen.findAllByText(/Approved original/)
  expect(approved).toHaveLength(2)
  expect(approved.every((line) => line.textContent?.includes(shaB))).toBe(true)
  const line = (label: string, sha?: string) => (_content: string, node: Element | null) => {
    if (node?.tagName !== 'P') return false
    const text = (node.textContent ?? '').replace(/\s+/g, ' ').trim()
    return text.startsWith(label) && (sha === undefined || text.includes(sha))
  }
  expect(screen.getByText(line('Resolved source:', shaB))).toBeInTheDocument()
  expect(screen.getByText(shaP)).toBeInTheDocument()
  expect(screen.getByText(line('Remote source:'))).toBeInTheDocument()
  expect(screen.getByText(line('Reviewed SHA:', shaB))).toBeInTheDocument()
})
