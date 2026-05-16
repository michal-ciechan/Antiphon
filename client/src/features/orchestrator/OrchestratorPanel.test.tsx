import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { OrchestratorPanel } from './OrchestratorPanel'

function stateResponse(paused = false) {
  return {
    paused,
    enabled: true,
    generatedAt: '2026-05-16T00:00:00Z',
    runningSessions: 1,
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
        cardId: 'card-1',
        cardIdentifier: 'CARD-0001',
        cardTitle: 'Investigate queue',
        boardId: 'board-1',
        boardName: 'Ops Board',
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
})
