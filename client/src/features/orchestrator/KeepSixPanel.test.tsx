import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, settleQueries, userEvent, waitFor, within } from '../../test/utils'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
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
  holdUntil: '2099-01-01T00:00:00.000Z',
  lastReminderEnqueuedAt: '2026-10-17T09:20:00.000Z',
  lastDispatchAt: '2026-10-17T08:20:00.000Z',
})

describe('KeepSixPanel', () => {
  it('renders off, healthy, and under-target badges with the counts and an active hold', async () => {
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
    expect(within(short).getByTestId('keep-six-hold')).toHaveTextContent('2099-01-01T00:00:00.000Z')
    expect(within(short).getByTestId('keep-six-reminder')).toHaveTextContent('2026-10-17T09:20:00.000Z')
    expect(within(short).getByTestId('keep-six-dispatch')).toHaveTextContent('2026-10-17T08:20:00.000Z')
    expect(screen.getByRole('switch', { name: 'Auto-processing Off seat' })).not.toBeChecked()
    expect(screen.getByRole('switch', { name: 'Auto-processing Healthy seat' })).toBeChecked()
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

  it('a stale revision 409 refetches, notifies, and keeps the server row', async () => {
    const { notifications } = await import('@mantine/notifications')
    vi.mocked(notifications.show).mockClear()
    let reads = 0
    const initial = row({
      agentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
      name: 'Off seat',
      enabled: false,
      revision: 1,
      countedOpen: 2,
      effectiveTarget: 6,
      badge: 'off',
    })
    const refreshed = { ...initial, revision: 4, countedOpen: 3 }
    server.use(
      http.get('/api/orchestrators/keep-six', () => {
        reads += 1
        return HttpResponse.json([reads === 1 ? initial : refreshed])
      }),
      http.put('/api/orchestrators/:agentId/keep-six', () => HttpResponse.json(
        { title: 'keep_six_revision', detail: 'Keep-six revision changed during the write.' },
        { status: 409 },
      )),
    )

    const { queryClient } = renderWithProviders(<KeepSixPanel />)
    const toggle = await screen.findByRole('switch', { name: 'Auto-processing Off seat' })
    const readsAfterLoad = reads
    expect(within(screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1')).getByTestId('keep-six-count')).toHaveTextContent('2 of 6')
    expect(toggle).not.toBeChecked()

    await userEvent.click(toggle)
    await settleQueries(queryClient)

    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({
      color: 'red',
      message: 'Keep-six revision changed during the write.',
    }))
    expect(reads).toBeGreaterThan(readsAfterLoad)
    expect(screen.getByRole('switch', { name: 'Auto-processing Off seat' })).not.toBeChecked()
    expect(within(screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1')).getByTestId('keep-six-count')).toHaveTextContent('3 of 6')
  })

  it('renders an em dash when lastCompletionAt is null and the timestamp when it is set', async () => {
    const absent = row({
      agentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa4',
      name: 'No completion',
      lastCompletionAt: null,
      lastDispatchAt: '2026-10-17T08:00:00.000Z',
    })
    const present = row({
      agentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa5',
      name: 'Completed seat',
      lastCompletionAt: '2026-10-17T11:00:00.000Z',
    })
    server.use(http.get('/api/orchestrators/keep-six', () => HttpResponse.json([absent, present])))

    renderWithProviders(<KeepSixPanel />)

    const missing = await screen.findByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa4')
    const filled = screen.getByTestId('keep-six-row-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa5')
    expect(within(missing).getByTestId('keep-six-completion')).toHaveTextContent('—')
    expect(within(missing).getByTestId('keep-six-completion')).not.toHaveTextContent('null')
    expect(within(filled).getByTestId('keep-six-completion')).toHaveTextContent('2026-10-17T11:00:00.000Z')
  })
})
