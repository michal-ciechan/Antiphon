import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import type { HostBudgetDto } from '../../api/hosts'
import { HostsPanel } from './HostsPanel'

const local: HostBudgetDto = {
  hostId: 'local', kind: 'local', configuredMaxInFlight: null, declaredCapacity: null,
  effectiveLimit: 4, inFlight: 2,
  occupiedBreakdown: { sessions: 0, pendingLaunch: 0, inFlightMirrors: 0 },
  available: true, dispatchEligible: true, source: 'config', reason: null,
  updatedAt: null, revision: 0,
}
const remote: HostBudgetDto = {
  hostId: 'server2', kind: 'runner', configuredMaxInFlight: 3, declaredCapacity: 10,
  effectiveLimit: 3, inFlight: 3,
  occupiedBreakdown: { sessions: 3, pendingLaunch: 0, inFlightMirrors: 0 },
  available: true, dispatchEligible: true, source: 'budget', reason: 'reserve',
  updatedAt: '2026-09-28T00:00:00Z', revision: 1,
}

describe('HostsPanel', () => {
  it('shows both hosts, occupancy, limits, and availability', async () => {
    server.use(http.get('/api/hosts', () => HttpResponse.json([local, remote])))
    renderWithProviders(<HostsPanel />)
    expect(await screen.findByText('server2')).toBeInTheDocument()
    expect(screen.getByText('local')).toBeInTheDocument()
    expect(screen.getByText('3 / 3')).toBeInTheDocument()
    expect(screen.getAllByText('Eligible').length).toBe(2)
  })

  it('submits a budget with its reason', async () => {
    const writes: unknown[] = []
    server.use(
      http.get('/api/hosts', () => HttpResponse.json([local, remote])),
      http.put('/api/hosts/local/budget', async ({ request }) => {
        writes.push(await request.json())
        return HttpResponse.json({ ...local, configuredMaxInFlight: 1, effectiveLimit: 1 })
      }),
    )
    renderWithProviders(<HostsPanel />)
    await screen.findByText('server2')
    await userEvent.clear(screen.getByLabelText('Budget for local'))
    await userEvent.type(screen.getByLabelText('Budget for local'), '1')
    await userEvent.type(screen.getByLabelText('Reason for local'), 'reserve')
    await userEvent.click(screen.getByRole('button', { name: 'Save budget for local' }))
    await waitFor(() => expect(writes).toEqual([{ maxInFlight: 1, reason: 'reserve' }]))
  })

  it('shows the problem code when a capacity push is refused', async () => {
    server.use(
      http.get('/api/hosts', () => HttpResponse.json([local, remote])),
      http.put('/api/session-runners/server2/capacity', () =>
        HttpResponse.json({ code: 'phone_home_unsupported_operation', detail: 'old runner' }, { status: 409 })),
    )
    renderWithProviders(<HostsPanel />)
    await screen.findByText('server2')
    await userEvent.clear(screen.getByLabelText('Capacity for server2'))
    await userEvent.type(screen.getByLabelText('Capacity for server2'), '6')
    await userEvent.type(screen.getByLabelText('Reason for server2 capacity'), 'scale')
    await userEvent.click(screen.getByRole('button', { name: 'Save capacity for server2' }))
    expect(await screen.findByText('phone_home_unsupported_operation')).toBeInTheDocument()
  })

  it('marks a host whose occupancy exceeds the lowered limit', async () => {
    server.use(http.get('/api/hosts', () => HttpResponse.json([
      local, { ...remote, inFlight: 5 },
    ])))
    renderWithProviders(<HostsPanel />)
    expect(await screen.findByText('Over budget')).toBeInTheDocument()
  })
})
