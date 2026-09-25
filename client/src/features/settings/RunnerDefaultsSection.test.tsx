import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { RunnerDefaultsSection } from './RunnerDefaultsSection'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const defaults = {
  revision: 3,
  globalRunnerId: 'server2',
  kindDefaults: [{ agentKind: 'Codex', runnerId: 'desktop', inheritedRunnerId: 'server2', source: 'KindDefault' }],
  updatedAt: '2026-09-25T00:00:00Z',
  lastReason: 'operator',
  lastProvenance: 'Human',
  lastCallerTaskId: null,
  supportedKinds: ['Grok', 'ClaudeCode', 'Codex'],
  unresolvedReferences: ['gone-runner'],
}

type SavedBody = {
  expectedRevision?: number
  kindDefaults?: unknown[]
  globalRunnerId?: string | null
  reason?: string
}

describe('RunnerDefaultsSection', () => {
  it('saves a cleared kind override with the current revision and keeps a stale draft', async () => {
    let body: SavedBody | null = null
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(defaults)),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({
        revisions: [{ revision: 2, previousRevision: 1, globalRunnerId: null, kindDefaults: [], createdAt: '2026-09-25T00:00:00Z', reason: 'earlier', provenance: 'Human', callerTaskId: null }],
        nextBeforeRevision: null,
      })),
      http.get('/api/session-runners', () => HttpResponse.json([
        { runnerId: 'desktop', displayName: 'Desktop', platform: 'windows', available: true, dispatchEligible: true, unavailableReason: null, capacity: 6, occupied: 0, capacityKind: 'delegatedTasks', stale: false, features: [] },
        { runnerId: 'server2', displayName: 'server2', platform: 'linux', available: false, dispatchEligible: false, unavailableReason: 'offline', capacity: null, occupied: null, capacityKind: 'sessions', stale: true, features: [] },
      ])),
      http.put('/api/runner-defaults', async ({ request }) => {
        body = (await request.json()) as SavedBody
        return HttpResponse.json({ title: 'runner_defaults_revision_conflict' }, { status: 409 })
      }),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    expect(await screen.findByTestId('runner-defaults-unresolved')).toHaveTextContent('gone-runner')
    expect(screen.getByTestId('runner-defaults-kind-Codex')).toBeInTheDocument()
    await userEvent.click(screen.getByTestId('runner-defaults-kind-Codex'))
    await userEvent.click(await screen.findByRole('option', { name: 'Use global default' }))
    await userEvent.type(screen.getByTestId('runner-defaults-reason'), 'clear codex')
    await userEvent.click(screen.getByTestId('runner-defaults-save'))
    await waitFor(() => expect(body?.expectedRevision).toBe(3))
    expect(body?.kindDefaults).toEqual([])
    expect(body?.globalRunnerId).toBe('server2')
    expect(await screen.findByTestId('runner-defaults-conflict')).toHaveTextContent('draft')
    expect(screen.getByTestId('runner-defaults-reason')).toHaveValue('clear codex')
  })

  it('saves the global runner with the current revision', async () => {
    let body: SavedBody | null = null
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(defaults)),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({ revisions: [], nextBeforeRevision: null })),
      http.get('/api/session-runners', () => HttpResponse.json([
        { runnerId: 'desktop', displayName: 'Desktop', platform: 'windows', available: true, dispatchEligible: true, unavailableReason: null, capacity: 6, occupied: 0, capacityKind: 'delegatedTasks', stale: false, features: [] },
      ])),
      http.put('/api/runner-defaults', async ({ request }) => {
        body = (await request.json()) as SavedBody
        return HttpResponse.json({ ...defaults, revision: 4, globalRunnerId: 'desktop', kindDefaults: [] })
      }),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    await userEvent.click(await screen.findByTestId('runner-defaults-global'))
    await userEvent.click(await screen.findByRole('option', { name: /^Desktop/ }))
    await userEvent.type(screen.getByTestId('runner-defaults-reason'), 'prefer desktop')
    await userEvent.click(screen.getByTestId('runner-defaults-save'))
    await waitFor(() => expect(body?.expectedRevision).toBe(3))
    expect(body?.globalRunnerId).toBe('desktop')
    expect(body?.reason).toBe('prefer desktop')
    expect(body?.kindDefaults).toEqual([{ agentKind: 'Codex', runnerId: 'desktop' }])
  })

  it('restores history with the current revision', async () => {
    let body: SavedBody | null = null
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(defaults)),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({
        revisions: [{ revision: 2, previousRevision: 1, globalRunnerId: 'desktop', kindDefaults: [], createdAt: '2026-09-25T00:00:00Z', reason: 'earlier', provenance: 'Human', callerTaskId: null }],
        nextBeforeRevision: null,
      })),
      http.get('/api/session-runners', () => HttpResponse.json([])),
      http.put('/api/runner-defaults', async ({ request }) => {
        body = (await request.json()) as SavedBody
        return HttpResponse.json({ ...defaults, revision: 4, globalRunnerId: 'desktop', kindDefaults: [] })
      }),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    await userEvent.type(await screen.findByTestId('runner-defaults-reason'), 'restore earlier')
    await userEvent.click(screen.getByTestId('runner-defaults-restore-2'))
    await waitFor(() => expect(body?.expectedRevision).toBe(3))
    expect(body?.globalRunnerId).toBe('desktop')
    expect(body?.kindDefaults).toEqual([])
    expect(body?.reason).toBe('restore earlier')
  })

  it('keeps a load failure inside the section', async () => {
    server.use(http.get('/api/runner-defaults', () => new HttpResponse(null, { status: 500 })))
    renderWithProviders(<RunnerDefaultsSection />)
    expect(await screen.findByText(/Could not load runner defaults/)).toBeInTheDocument()
    expect(screen.queryByTestId('runner-defaults-save')).not.toBeInTheDocument()
  })
})
