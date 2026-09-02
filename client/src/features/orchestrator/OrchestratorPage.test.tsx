import { HttpResponse, http } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AttentionDto, AttentionItemDto } from '../../api/attention'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { OrchestratorPage } from './OrchestratorPage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

function stuck(overrides: Partial<AttentionItemDto>): AttentionItemDto {
  return {
    kind: 'BlockedQuestion',
    severity: 'Critical',
    taskId: 't1',
    sessionId: null,
    agentId: null,
    messageId: null,
    title: 'Which branch should this land on?',
    headline: 'Blocked — waiting on a human answer.',
    evidence: '',
    sinceUtc: '2026-08-17T09:00:00Z',
    subtreeCostUsd: null,
    actions: ['Reply'],
    ...overrides,
  }
}

function serve(items: AttentionItemDto[]) {
  let agentTaskRequests = 0
  let orchestratorStateRequests = 0
  let cardsRequests = 0
  server.use(
    http.get('/api/attention', () =>
      HttpResponse.json<AttentionDto>({
        generatedAt: '2026-08-17T10:00:00Z',
        runnerConsulted: true,
        items,
      }),
    ),
    http.get('/api/model-availability', () =>
      HttpResponse.json({
        holds: [],
        available: ['fable', 'opus', 'sonnet', 'haiku', 'grok-4.6'],
      }),
    ),
    http.get('/api/agent-tasks', () => {
      agentTaskRequests += 1
      return HttpResponse.json([])
    }),
    http.get('/api/agent-tasks/summary', () =>
      HttpResponse.json({ active: 0, blocked: 0, runs: 0, totalCostUsd: 0, byStatus: {} }),
    ),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/cards', () => {
      cardsRequests += 1
      return HttpResponse.json({ cards: [], truncated: false })
    }),
    // The Cards tab renders eagerly alongside the others, so its own endpoint has to answer with a
    // real shape — an empty object throws inside OrchestratorPanel and takes the page down with it.
    http.get('/api/orchestrator/state', () => {
      orchestratorStateRequests += 1
      return HttpResponse.json({
        paused: false,
        enabled: true,
        generatedAt: '2026-08-17T10:00:00Z',
        runningSessions: 0,
        runningCardSessions: 0,
        runningDelegateSessions: 0,
        retryQueueLength: 0,
        totals: { tokensIn: 0, tokensOut: 0, costUsd: 0, activeRuntimeSeconds: 0 },
        limits: {
          pollIntervalSeconds: 30,
          maxDispatchesPerTick: 25,
          failureBackoffBaseMs: 10000,
          failureBackoffMaxMs: 300000,
          startingSessionGraceSeconds: 300,
        },
        running: [],
        retryQueue: [],
      })
    }),
  )

  return {
    agentTaskRequests: () => agentTaskRequests,
    orchestratorStateRequests: () => orchestratorStateRequests,
    cardsRequests: () => cardsRequests,
  }
}

describe('OrchestratorPage', () => {
  afterEach(() => {
    window.history.pushState({}, '', '/')
  })

  it('defers the delegations request until its tab is opened', async () => {
    const requests = serve([])

    renderWithProviders(<OrchestratorPage />)

    await waitFor(() => expect(requests.orchestratorStateRequests()).toBe(1))
    expect(requests.agentTaskRequests()).toBe(0)

    await userEvent.click(screen.getByRole('tab', { name: /delegations/i }))
    await waitFor(() => expect(requests.agentTaskRequests()).toBe(1))
  })

  it('carries the count of open conditions on the tab', async () => {
    // The signal has to be visible from the tab strip — a diagnostic list nobody opens on a bad day
    // is the same as no list. Settled failures are excluded so a healthy fleet reads as zero.
    serve([
      stuck({}),
      stuck({ kind: 'DeadSession', severity: 'Error', taskId: 't2', title: 'Ship it' }),
      stuck({ kind: 'RecentFailure', severity: 'Warning', taskId: 't3', title: 'A task that died' }),
    ])

    renderWithProviders(<OrchestratorPage />)

    const tab = await screen.findByRole('tab', { name: /Needs attention/ })
    await waitFor(() => expect(tab).toHaveTextContent('2'))
  })

  it('shows no badge when nothing is stuck', async () => {
    serve([])

    renderWithProviders(<OrchestratorPage />)

    const tab = await screen.findByRole('tab', { name: /Needs attention/ })
    await waitFor(() => expect(tab).not.toHaveTextContent(/\d/))
  })

  it('puts the tab in the URL so the view is linkable', async () => {
    serve([stuck({})])

    renderWithProviders(<OrchestratorPage />)
    await userEvent.click(await screen.findByRole('tab', { name: /Needs attention/ }))

    await waitFor(() =>
      expect(new URLSearchParams(window.location.search).get('tab')).toBe('attention'),
    )
    expect(await screen.findByText('Which branch should this land on?')).toBeInTheDocument()
  })

  it('the decisions tab badge counts decision cards and nothing else', async () => {
    serve([stuck({ kind: 'CardNeedsDecision', cardId: 'card-1', boardId: 'board-1' }), stuck({})])
    renderWithProviders(<OrchestratorPage />)

    const tab = await screen.findByRole('tab', { name: /Decisions/ })
    await waitFor(() => expect(tab).toHaveTextContent('1'))
  })

  it('?tab=history renders the History panel, and the Delegations panel is not mounted', async () => {
    serve([])
    window.history.pushState({}, '', '/orchestrator?tab=history')
    renderWithProviders(<OrchestratorPage />)

    expect(await screen.findByRole('heading', { name: 'History' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Delegations' })).not.toBeInTheDocument()
    expect(screen.queryByTestId('lane-working')).not.toBeInTheDocument()
  })

  it('the cards tab renders the backlog section under the retry queue', async () => {
    serve([])
    renderWithProviders(<OrchestratorPage />)

    const retry = await screen.findByRole('heading', { name: 'Retry Queue' })
    const backlog = await screen.findByRole('heading', { name: 'Backlog' })
    expect(retry.compareDocumentPosition(backlog) & Node.DOCUMENT_POSITION_FOLLOWING).toBeGreaterThan(0)
    expect(screen.getByTestId('backlog-box-DoFirst')).toBeInTheDocument()
    expect(screen.getByTestId('backlog-box-Schedule')).toBeInTheDocument()
    expect(screen.getByTestId('backlog-box-Clear')).toBeInTheDocument()
    expect(screen.getByTestId('backlog-box-Someday')).toBeInTheDocument()
  })

  it('the backlog request is deferred while another tab is open', async () => {
    const requests = serve([])
    window.history.pushState({}, '', '/orchestrator?tab=history')
    renderWithProviders(<OrchestratorPage />)

    expect(await screen.findByRole('heading', { name: 'History' })).toBeInTheDocument()
    expect(requests.cardsRequests()).toBe(0)
    expect(requests.orchestratorStateRequests()).toBe(0)

    await userEvent.click(screen.getByRole('tab', { name: /cards/i }))
    await waitFor(() => expect(requests.cardsRequests()).toBe(1))
  })
})
