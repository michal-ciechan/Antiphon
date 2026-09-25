import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import { usePutRunnerDefaults, useRunnerDefaults } from './runnerDefaults'

const current = {
  revision: 4,
  globalRunnerId: 'server2',
  kindDefaults: [],
  updatedAt: '2026-09-25T00:00:00Z',
  lastReason: 'imported',
  lastProvenance: 'Migration',
  lastCallerTaskId: null,
  supportedKinds: ['Grok', 'ClaudeCode', 'Codex'],
  unresolvedReferences: [],
}

describe('runnerDefaults', () => {
  it('puts the complete snapshot and the current revision', async () => {
    let body: unknown = null
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(current)),
      http.put('/api/runner-defaults', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ ...current, revision: 5, globalRunnerId: 'desktop' })
      }),
    )
    const view = renderHookWithProviders(() => ({ defaults: useRunnerDefaults(), save: usePutRunnerDefaults() }))
    await waitFor(() => expect(view.result.current.defaults.data?.revision).toBe(4))
    view.result.current.save.mutate({
      expectedRevision: 4,
      globalRunnerId: 'desktop',
      kindDefaults: [],
      reason: 'prefer desktop',
      provenance: 'Human',
    })
    await waitFor(() => expect(body).toMatchObject({ expectedRevision: 4, globalRunnerId: 'desktop', provenance: 'Human' }))
  })
})
