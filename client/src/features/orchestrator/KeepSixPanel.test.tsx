import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { KeepSixPanel } from './KeepSixPanel'

interface KeepSixOrchestratorDto {
  agentId: string
  name: string
  enabled: boolean
  target: number
  revision: number
  reason: string | null
  provenance: string | null
  updatedAt: string | null
  enabledAt: string | null
  holdReason: string | null
  holdUntil: string | null
  lastReminderEnqueuedAt: string | null
  outstandingMessageId: string | null
  lastAcknowledgedAt: string | null
  countedOpen: number | null
  effectiveTarget: number | null
  dispatchableBacklog: number | null
  shortfall: number | null
  topCards: string[] | null
  lastDispatchAt: string | null
  lastCompletionAt: string | null
  badge: string | null
}

function row(overrides: Partial<KeepSixOrchestratorDto> & Pick<KeepSixOrchestratorDto, 'agentId' | 'name'>): KeepSixOrchestratorDto {
  return {
    enabled: false,
    target: 6,
    revision: 0,
    reason: null,
    provenance: null,
    updatedAt: null,
    enabledAt: null,
    holdReason: null,
    holdUntil: null,
    lastReminderEnqueuedAt: null,
    outstandingMessageId: null,
    lastAcknowledgedAt: null,
    countedOpen: null,
    effectiveTarget: null,
    dispatchableBacklog: null,
    shortfall: null,
    topCards: null,
    lastDispatchAt: null,
    lastCompletionAt: null,
    badge: 'off',
    ...overrides,
  }
}

const off = row({
  agentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
  name: 'Off seat',
  enabled: false,
  badge: 'off',
  countedOpen: 2,
  effectiveTarget: 6,
  dispatchableBacklog: 4,
  shortfall: 4,
  lastReminderEnqueuedAt: '2026-10-17T09:00:00.000Z',
  lastDispatchAt: '2026-10-17T08:00:00.000Z',
})

const healthy = row({
  agentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
  name: 'Healthy seat',
  enabled: true,
  badge: 'healthy',
  countedOpen: 6,
  effectiveTarget: 6,
  dispatchableBacklog: 1,
  shortfall: 0,
  revision: 3,
  lastReminderEnqueuedAt: '2026-10-17T09:10:00.000Z',
  lastDispatchAt: '2026-10-17T08:10:00.000Z',
})

const under = row({
  agentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
  name: 'Short seat',
  enabled: true,
  badge: 'under-target',
  countedOpen: 1,
  effectiveTarget: 6,
  dispatchableBacklog: 9,
  shortfall: 5,
  revision: 4,
  holdReason: 'model quota',
  holdUntil: '2026-10-18T12:00:00.000Z',
  lastReminderEnqueuedAt: '2026-10-17T09:20:00.000Z',
  lastDispatchAt: '2026-10-17T08:20:00.000Z',
})

describe('KeepSixPanel', () => {
  it('renders off, healthy, and under-target badges with the counts and an active hold', async () => {
    // holdUntil is 2026-10-18T12:00:00.000Z. The panel clocks expiry from query dataUpdatedAt
    // (Date.now()). Only Date is faked so findBy keeps real timers.
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-10-17T12:00:00.000Z') })
    try {
      server.use(http.get('/api/orchestrators/keep-six', () => HttpResponse.json([off, healthy, under])))

      renderWithProviders(<KeepSixPanel />)

      const badges = await screen.findAllByTestId('keep-six-badge')
      expect(badges.map((badge) => badge.textContent)).toEqual(['off', 'healthy', 'under-target'])

      const short = screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa3')
      expect(within(screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1')).getByTestId('keep-six-count')).toHaveTextContent('2 of 6')
      expect(within(screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2')).getByTestId('keep-six-count')).toHaveTextContent('6 of 6')
      expect(within(short).getByTestId('keep-six-count')).toHaveTextContent('1 of 6')
      expect(within(screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1')).getByTestId('keep-six-dispatchable')).toHaveTextContent('4')
      expect(within(screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2')).getByTestId('keep-six-dispatchable')).toHaveTextContent('1')
      expect(within(short).getByTestId('keep-six-dispatchable')).toHaveTextContent('9')
      expect(within(short).getByTestId('keep-six-hold')).toHaveTextContent('model quota')
      expect(within(short).getByTestId('keep-six-hold')).toHaveTextContent('2026-10-18T12:00:00.000Z')
      expect(within(short).getByTestId('keep-six-reminder')).toHaveTextContent('2026-10-17T09:20:00.000Z')
      expect(within(short).getByTestId('keep-six-dispatch')).toHaveTextContent('2026-10-17T08:20:00.000Z')
      expect(screen.getByRole('switch', { name: 'Auto-processing Off seat' })).not.toBeChecked()
      expect(screen.getByRole('switch', { name: 'Auto-processing Healthy seat' })).toBeChecked()
    } finally {
      vi.useRealTimers()
    }
  })

  it('the switch puts Human provenance and does not call tick', async () => {
    let putBody: unknown = null
    let putUrl = ''
    let orchestratorPosts = 0
    server.use(
      http.get('/api/orchestrators/keep-six', () => HttpResponse.json([off])),
      http.put('/api/orchestrators/:agentId/keep-six', async ({ request }) => {
        putUrl = request.url
        putBody = await request.json()
        return HttpResponse.json({ ...off, enabled: true, revision: 1 })
      }),
      http.post('/api/orchestrator/:action', () => {
        orchestratorPosts += 1
        return HttpResponse.json({})
      }),
    )

    renderWithProviders(<KeepSixPanel />)

    await userEvent.click(await screen.findByRole('switch', { name: 'Auto-processing Off seat' }))

    await waitFor(() => expect(putBody).not.toBeNull())
    expect(putUrl).toContain('/api/orchestrators/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1/keep-six')
    expect(putBody).toEqual({
      expectedRevision: 0,
      enabled: true,
      target: 6,
      reason: 'operator toggle',
      provenance: 'Human',
    })
    expect(orchestratorPosts).toBe(0)
  })
})
