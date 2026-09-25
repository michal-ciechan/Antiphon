import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import { useSessionRunners } from './sessionRunners'

describe('sessionRunners', () => {
  it('reads the catalogue including an offline runner', async () => {
    server.use(http.get('/api/session-runners', () => HttpResponse.json([
      { runnerId: 'desktop', displayName: 'Desktop', platform: 'windows', platformObservedAt: null, available: true, dispatchEligible: true, unavailableReason: null, capacity: 6, occupied: 1, capacityKind: 'delegatedTasks', capacityObservedAt: null, stale: false, features: [] },
      { runnerId: 'runner-b', displayName: 'Runner B', platform: null, platformObservedAt: null, available: false, dispatchEligible: false, unavailableReason: 'stale', capacity: null, occupied: null, capacityKind: 'sessions', capacityObservedAt: null, stale: true, features: [] },
    ])))
    const view = renderHookWithProviders(() => useSessionRunners())
    await waitFor(() => expect(view.result.current.data).toHaveLength(2))
    expect(view.result.current.data?.[1].occupied).toBeNull()
    expect(view.result.current.data?.[0].capacityKind).toBe('delegatedTasks')
    expect(view.result.current.data?.[0].platform).toBe('windows')
    expect(view.result.current.data?.[1].platform).toBeNull()
  })
})
