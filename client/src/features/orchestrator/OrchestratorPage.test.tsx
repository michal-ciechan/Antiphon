import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
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
  server.use(
    http.get('/api/attention', () =>
      HttpResponse.json<AttentionDto>({
        generatedAt: '2026-08-17T10:00:00Z',
        runnerConsulted: true,
        items,
      }),
    ),
    http.get('/api/agent-tasks', () => HttpResponse.json([])),
    // The Cards tab renders eagerly alongside the others, so its own endpoint has to answer with a
    // real shape — an empty object throws inside OrchestratorPanel and takes the page down with it.
    http.get('/api/orchestrator/state', () =>
      HttpResponse.json({
        paused: false,
        enabled: true,
        generatedAt: '2026-08-17T10:00:00Z',
        runningSessions: 0,
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
      }),
    ),
  )
}

describe('OrchestratorPage', () => {
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
})
