import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { OrchestratorPanel } from './OrchestratorPanel'

function delegateTask(overrides: {
  taskId: string
  shortId: string
  title: string
  parentTaskId?: string | null
  status?: 'Dispatched' | 'Working'
  role?: 'Code' | 'Plan'
  agentName?: string | null
}) {
  return {
    taskId: overrides.taskId,
    shortId: overrides.shortId,
    title: overrides.title,
    role: overrides.role ?? 'Code',
    status: overrides.status ?? 'Working',
    kind: 'Worker' as const,
    rootTaskId: overrides.parentTaskId ?? overrides.taskId,
    parentTaskId: overrides.parentTaskId ?? null,
    agentName: overrides.agentName ?? 'task-code',
  }
}

function stateResponse(paused = false) {
  return {
    paused,
    enabled: true,
    generatedAt: '2026-05-16T00:00:00Z',
    runningSessions: 4,
    runningCardSessions: 1,
    runningDelegateSessions: 3,
    retryQueueLength: 1,
    totals: {
      tokensIn: 123,
      tokensOut: 45,
      costUsd: 0.12,
      activeRuntimeSeconds: 95,
    },
    limits: {
      pollIntervalSeconds: 30,
      maxDispatchesPerTick: 25,
      failureBackoffBaseMs: 10000,
      failureBackoffMaxMs: 300000,
      startingSessionGraceSeconds: 300,
    },
    running: [
      {
        sessionId: 'session-1',
        source: 'Card',
        depth: 0,
        cardId: 'card-1',
        cardIdentifier: 'CARD-0001',
        cardTitle: 'Investigate queue',
        boardId: 'board-1',
        boardName: 'Ops Board',
        task: null,
        definitionName: 'e13-raw',
        agentKind: 'Raw',
        status: 'Running',
        runAttemptId: 'attempt-1',
        turnCount: 2,
        attemptNumber: 2,
        phase: 'StreamingTurn',
        startedAt: '2026-05-16T00:00:00Z',
        lastSeenAt: '2026-05-16T00:01:00Z',
        lastEventAt: '2026-05-16T00:01:00Z',
        runtimeSeconds: 95,
        tokensIn: 123,
        tokensOut: 45,
        costUsd: 0.12,
        live: true,
        lastSequence: 7,
      },
      {
        sessionId: 'session-bound',
        source: 'Delegation',
        depth: 0,
        cardId: 'card-92',
        cardIdentifier: 'CARD-0092',
        cardTitle: 'Delegate card',
        boardId: 'board-1',
        boardName: 'Ops Board',
        task: delegateTask({
          taskId: 'task-bound',
          shortId: 'abcd1234',
          title: 'Bound delegate work',
        }),
        definitionName: 'claude',
        agentKind: 'ClaudeCode',
        status: 'Running',
        runAttemptId: null,
        turnCount: 0,
        attemptNumber: null,
        phase: null,
        startedAt: '2026-05-16T00:00:00Z',
        lastSeenAt: '2026-05-16T00:01:00Z',
        lastEventAt: null,
        runtimeSeconds: 40,
        tokensIn: 10,
        tokensOut: 4,
        costUsd: 0.01,
        live: true,
        lastSequence: 3,
      },
      {
        sessionId: 'session-unbound',
        source: 'Delegation',
        depth: 0,
        cardId: null,
        cardIdentifier: null,
        cardTitle: null,
        boardId: null,
        boardName: null,
        task: delegateTask({
          taskId: 'task-unbound',
          shortId: 'deadbeef',
          title: 'Unbound fan-out',
        }),
        definitionName: 'claude',
        agentKind: 'ClaudeCode',
        status: 'Running',
        runAttemptId: null,
        turnCount: 0,
        attemptNumber: null,
        phase: null,
        startedAt: '2026-05-16T00:00:00Z',
        lastSeenAt: '2026-05-16T00:01:00Z',
        lastEventAt: null,
        runtimeSeconds: 12,
        tokensIn: 2,
        tokensOut: 1,
        costUsd: 0,
        live: false,
        lastSequence: 0,
      },
      {
        sessionId: 'session-child',
        source: 'Delegation',
        depth: 1,
        cardId: 'card-92',
        cardIdentifier: 'CARD-0092',
        cardTitle: 'Delegate card',
        boardId: 'board-1',
        boardName: 'Ops Board',
        task: delegateTask({
          taskId: 'task-child',
          shortId: 'child001',
          title: 'Child worker',
          parentTaskId: 'task-bound',
        }),
        definitionName: 'claude',
        agentKind: 'ClaudeCode',
        status: 'Running',
        runAttemptId: null,
        turnCount: 0,
        attemptNumber: null,
        phase: null,
        startedAt: '2026-05-16T00:00:10Z',
        lastSeenAt: '2026-05-16T00:01:00Z',
        lastEventAt: null,
        runtimeSeconds: 8,
        tokensIn: 1,
        tokensOut: 1,
        costUsd: 0,
        live: true,
        lastSequence: 1,
      },
    ],
    retryQueue: [
      {
        cardId: 'card-2',
        cardIdentifier: 'CARD-0002',
        cardTitle: 'Retry failed card',
        boardId: 'board-1',
        boardName: 'Ops Board',
        attemptCount: 1,
        maxAttempts: 3,
        nextRetryAt: '2026-05-16T00:02:00Z',
        lastAttemptAt: '2026-05-16T00:01:00Z',
        lastError: 'temporary failure',
      },
    ],
  }
}

describe('OrchestratorPanel', () => {
  it('renders running sessions and retry queue from the state endpoint', async () => {
    server.use(
      http.get('/api/orchestrator/state', () => HttpResponse.json(stateResponse())),
    )

    renderWithProviders(<OrchestratorPanel />)

    expect(await screen.findByText('CARD-0001')).toBeInTheDocument()
    expect(screen.getByText('Investigate queue')).toBeInTheDocument()
    expect(screen.getByText('StreamingTurn')).toBeInTheDocument()
    expect(screen.getAllByText('2').length).toBeGreaterThan(0)
    expect(screen.getByText('CARD-0002')).toBeInTheDocument()
    expect(screen.getByText('temporary failure')).toBeInTheDocument()
    expect(screen.getByText('25/tick')).toBeInTheDocument()
    expect(screen.getAllByText('168').length).toBeGreaterThan(0)
  })

  it('posts pause and refreshes state', async () => {
    let paused = false
    server.use(
      http.get('/api/orchestrator/state', () => HttpResponse.json(stateResponse(paused))),
      http.post('/api/orchestrator/pause', () => {
        paused = true
        return HttpResponse.json({ paused: true })
      }),
    )

    renderWithProviders(<OrchestratorPanel />)

    await userEvent.click(await screen.findByRole('button', { name: 'Pause' }))

    await waitFor(() => expect(screen.getByText('Paused')).toBeInTheDocument())
  })

  it('renders delegate rows with task chips, indent, metric subline and caption', async () => {
    server.use(
      http.get('/api/orchestrator/state', () => HttpResponse.json(stateResponse())),
    )

    renderWithProviders(<OrchestratorPanel />)

    expect(await screen.findByText('Unbound fan-out')).toBeInTheDocument()
    expect(screen.getAllByText('Task').length).toBeGreaterThanOrEqual(3)
    expect(screen.getAllByText('code · Working').length).toBeGreaterThan(0)
    expect(screen.getByText('└')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'abcd1234' })).toHaveAttribute(
      'href',
      '/orchestrator?tab=delegations&task=task-bound',
    )
    expect(screen.getByText('1 card · 3 delegate')).toBeInTheDocument()
    expect(screen.getByText(
      'Pause and Tick govern card auto-dispatch. Delegate sessions are dispatched by the task pipeline and are listed here for visibility.',
    )).toBeInTheDocument()
  })
})
