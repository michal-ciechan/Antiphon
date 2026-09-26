import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { Layout } from '../../shared/Layout'
import { host } from './hostsFixtures'
import { HostsPage } from './HostsPage'

describe('Hosts page', () => {
  it('renders one card per host', async () => {
    server.use(http.get('/api/hosts/stats', () => HttpResponse.json([host(), host({ hostId: 'server2', displayName: 'Server 2' })])))
    renderWithProviders(<HostsPage live={false} />)
    expect(await screen.findByRole('region', { name: 'Desktop' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Server 2' })).toBeInTheDocument()
  })

  it('keeps stale last values under a Stale badge', async () => {
    server.use(http.get('/api/hosts/stats', () => HttpResponse.json([host({ state: 'stale' })])))
    renderWithProviders(<HostsPage live={false} />)
    const card = await screen.findByRole('region', { name: 'Desktop' })
    expect(within(card).getByText('Stale')).toBeInTheDocument()
    expect(within(card).getByText('42 %')).toBeInTheDocument()
  })

  it('shows No data for an offline host and never fabricates zero', async () => {
    server.use(http.get('/api/hosts/stats', () => HttpResponse.json([host({ state: 'offline', current: null, rollups: null })])))
    renderWithProviders(<HostsPage live={false} />)
    const card = await screen.findByRole('region', { name: 'Desktop' })
    expect(within(card).getByText('No data')).toBeInTheDocument()
    expect(within(card).queryByText(/\b0 ?%/)).not.toBeInTheDocument()
  })

  it('explains when polling is disabled', async () => {
    server.use(http.get('/api/hosts/stats', () => HttpResponse.json([host({ state: 'offline', reason: 'disabled', current: null, rollups: null })])))
    renderWithProviders(<HostsPage live={false} />)
    const card = await screen.findByRole('region', { name: 'Desktop' })
    expect(within(card).getByText('disabled')).toBeInTheDocument()
  })

  it('labels Windows commit charge and Linux swap separately', async () => {
    server.use(http.get('/api/hosts/stats', () => HttpResponse.json([
      host(), host({ hostId: 'server2', displayName: 'Server 2', platform: 'linux' }),
    ])))
    renderWithProviders(<HostsPage live={false} />)
    const desktop = await screen.findByRole('region', { name: 'Desktop' })
    const server2 = screen.getByRole('region', { name: 'Server 2' })
    expect(within(desktop).getByText('Commit charge / limit')).toBeInTheDocument()
    expect(within(server2).getByText('Swap used / total')).toBeInTheDocument()
  })

  it('links the Hosts nav item to /hosts', async () => {
    renderWithProviders(<Layout />)
    await waitFor(() => expect(screen.getAllByRole('link', { name: 'Hosts' }).some(link => link.getAttribute('href') === '/hosts')).toBe(true))
  })
})
