import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHookWithProviders, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { host, observedAt } from './hostsFixtures'
import { hostKeys, useHostStats, type HostSeries } from '../../api/hosts'
import { useHostStatsLive } from './useHostStatsLive'

const signalr = vi.hoisted(() => ({
  connection: {
    state: 'Disconnected',
    handlers: new Map<string, (payload: unknown) => void>(),
    reconnected: undefined as undefined | (() => void),
    start: vi.fn(async function (this: { state: string }) { this.state = 'Connected' }),
    stop: vi.fn(async function (this: { state: string }) { this.state = 'Disconnected' }),
    invoke: vi.fn(async () => undefined),
    on: vi.fn(function (this: { handlers: Map<string, (payload: unknown) => void> }, event: string, handler: (payload: unknown) => void) { this.handlers.set(event, handler) }),
    off: vi.fn(function (this: { handlers: Map<string, (payload: unknown) => void> }, event: string) { this.handlers.delete(event) }),
    onreconnected: vi.fn(function (this: { reconnected?: () => void }, handler: () => void) { this.reconnected = handler }),
  },
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return signalr.connection }
  },
}))

describe('useHostStatsLive', () => {
  beforeEach(() => {
    signalr.connection.state = 'Disconnected'
    signalr.connection.handlers.clear()
    signalr.connection.reconnected = undefined
    signalr.connection.invoke.mockClear()
    signalr.connection.start.mockClear()
  })

  it('joins group hosts on start', async () => {
    renderHookWithProviders(() => useHostStatsLive())
    await waitFor(() => expect(signalr.connection.invoke).toHaveBeenCalledWith('JoinGroup', 'hosts'))
  })

  it('replaces the list cache on push without a refetch', async () => {
    let requests = 0
    server.use(http.get('/api/hosts/stats', () => { requests++; return HttpResponse.json([host()]) }))
    const view = renderHookWithProviders(() => useHostStatsLive())
    await waitFor(() => expect(signalr.connection.handlers.has('HostStatsUpdated')).toBe(true))
    const pushed = [host({ state: 'stale' })]
    act(() => signalr.connection.handlers.get('HostStatsUpdated')?.(pushed))
    expect(view.queryClient.getQueryData(hostKeys.stats)).toEqual(pushed)
    expect(requests).toBe(0)
  })

  it('appends a pushed point to a mounted series', async () => {
    const view = renderHookWithProviders(() => useHostStatsLive())
    const initial: HostSeries = { hostId: 'desktop', metric: 'cpu', window: '30m', intervalSeconds: 5, points: [{ t: '2026-09-26T11:59:55Z', v: 40 }] }
    view.queryClient.setQueryData(hostKeys.series('desktop', 'cpu', '30m'), initial)
    await waitFor(() => expect(signalr.connection.handlers.has('HostStatsUpdated')).toBe(true))
    act(() => signalr.connection.handlers.get('HostStatsUpdated')?.([host()]))
    const series = view.queryClient.getQueryData<HostSeries>(hostKeys.series('desktop', 'cpu', '30m'))
    expect(series?.points).toHaveLength(2)
    expect(series?.points.at(-1)).toEqual({ t: observedAt, v: 42 })
  })

  it('rejoins hosts and refetches after reconnect', async () => {
    let requests = 0
    server.use(http.get('/api/hosts/stats', () => { requests++; return HttpResponse.json([host()]) }))
    const view = renderHookWithProviders(() => { useHostStatsLive(); return useHostStats() })
    await waitFor(() => expect(signalr.connection.reconnected).toBeTypeOf('function'))
    await waitFor(() => expect(view.result.current.data).toHaveLength(1))
    const before = requests
    await act(async () => { signalr.connection.reconnected?.() })
    await waitFor(() => expect(requests).toBe(before + 1))
    expect(signalr.connection.invoke).toHaveBeenCalledTimes(2)
  })
})
