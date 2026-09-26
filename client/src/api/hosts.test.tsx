import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import { host } from '../features/hosts/hostsFixtures'
import { useHostStats, useHostSeries } from './hosts'

describe('hosts API', () => {
  it('useHostStats reads /api/hosts/stats', async () => {
    server.use(http.get('/api/hosts/stats', () => HttpResponse.json([host(), host({ hostId: 'server2' })])))
    const view = renderHookWithProviders(() => useHostStats())
    await waitFor(() => expect(view.result.current.data).toHaveLength(2))
    expect(view.result.current.data?.[1].hostId).toBe('server2')
  })

  it('useHostSeries requests its metric and window', async () => {
    let query = ''
    server.use(http.get('/api/hosts/server2/stats/series', ({ request }) => {
      query = new URL(request.url).search
      return HttpResponse.json({ hostId: 'server2', metric: 'memory', window: '5m', intervalSeconds: 5, points: [] })
    }))
    const view = renderHookWithProviders(() => useHostSeries('server2', 'memory', '5m'))
    await waitFor(() => expect(view.result.current.data?.window).toBe('5m'))
    expect(query).toContain('metric=memory')
    expect(query).toContain('window=5m')
  })
})
