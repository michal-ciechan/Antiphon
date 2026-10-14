import { HttpResponse, delay, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { act, renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { runnerDefaultKeys } from '../../api/runnerDefaults'
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
    const captured: { body: SavedBody | null } = { body: null }
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
        captured.body = (await request.json()) as SavedBody
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
    await waitFor(() => expect(captured.body?.expectedRevision).toBe(3))
    expect(captured.body?.kindDefaults).toEqual([])
    expect(captured.body?.globalRunnerId).toBe('server2')
    expect(await screen.findByTestId('runner-defaults-conflict')).toHaveTextContent('draft')
    expect(screen.getByTestId('runner-defaults-reason')).toHaveValue('clear codex')
  })

  it('saves the global runner with the current revision', async () => {
    const captured: { body: SavedBody | null } = { body: null }
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(defaults)),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({ revisions: [], nextBeforeRevision: null })),
      http.get('/api/session-runners', () => HttpResponse.json([
        { runnerId: 'desktop', displayName: 'Desktop', platform: 'windows', available: true, dispatchEligible: true, unavailableReason: null, capacity: 6, occupied: 0, capacityKind: 'delegatedTasks', stale: false, features: [] },
      ])),
      http.put('/api/runner-defaults', async ({ request }) => {
        captured.body = (await request.json()) as SavedBody
        return HttpResponse.json({ ...defaults, revision: 4, globalRunnerId: 'desktop', kindDefaults: [] })
      }),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    await userEvent.click(await screen.findByTestId('runner-defaults-global'))
    await userEvent.click(await screen.findByRole('option', { name: /^Desktop/ }))
    await userEvent.type(screen.getByTestId('runner-defaults-reason'), 'prefer desktop')
    await userEvent.click(screen.getByTestId('runner-defaults-save'))
    await waitFor(() => expect(captured.body?.expectedRevision).toBe(3))
    expect(captured.body?.globalRunnerId).toBe('desktop')
    expect(captured.body?.reason).toBe('prefer desktop')
    expect(captured.body?.kindDefaults).toEqual([{ agentKind: 'Codex', runnerId: 'desktop' }])
  })

  it('restores history with the current revision', async () => {
    const captured: { body: SavedBody | null } = { body: null }
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(defaults)),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({
        revisions: [{ revision: 2, previousRevision: 1, globalRunnerId: 'desktop', kindDefaults: [], createdAt: '2026-09-25T00:00:00Z', reason: 'earlier', provenance: 'Human', callerTaskId: null }],
        nextBeforeRevision: null,
      })),
      http.get('/api/session-runners', () => HttpResponse.json([])),
      http.put('/api/runner-defaults', async ({ request }) => {
        captured.body = (await request.json()) as SavedBody
        return HttpResponse.json({ ...defaults, revision: 4, globalRunnerId: 'desktop', kindDefaults: [] })
      }),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    await userEvent.type(await screen.findByTestId('runner-defaults-reason'), 'restore earlier')
    await userEvent.click(screen.getByTestId('runner-defaults-restore-2'))
    await waitFor(() => expect(captured.body?.expectedRevision).toBe(3))
    expect(captured.body?.globalRunnerId).toBe('desktop')
    expect(captured.body?.kindDefaults).toEqual([])
    expect(captured.body?.reason).toBe('restore earlier')
  })

  it('clears the conflict banner when a newer revision arrives', async () => {
    let current = defaults
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(current)),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({ revisions: [], nextBeforeRevision: null })),
      http.get('/api/session-runners', () => HttpResponse.json([])),
      http.put('/api/runner-defaults', () =>
        HttpResponse.json({ title: 'runner_defaults_revision_conflict' }, { status: 409 })),
    )
    const { queryClient } = renderWithProviders(<RunnerDefaultsSection />)
    await userEvent.type(await screen.findByTestId('runner-defaults-reason'), 'stale save')
    await userEvent.click(screen.getByTestId('runner-defaults-save'))
    expect(await screen.findByTestId('runner-defaults-conflict')).toBeInTheDocument()
    current = { ...defaults, revision: 4, updatedAt: '2026-09-26T00:00:00Z' }
    await act(async () => { await queryClient.invalidateQueries({ queryKey: runnerDefaultKeys.current }) })
    expect(await screen.findByText(/Revision 4\./)).toBeInTheDocument()
    expect(screen.queryByTestId('runner-defaults-conflict')).not.toBeInTheDocument()
  })

  it('keeps a load failure inside the section', async () => {
    server.use(http.get('/api/runner-defaults', () => new HttpResponse(null, { status: 500 })))
    renderWithProviders(<RunnerDefaultsSection />)
    expect(await screen.findByText(/Could not load runner defaults/)).toBeInTheDocument()
    expect(screen.queryByTestId('runner-defaults-save')).not.toBeInTheDocument()
  })
})

describe('C1270 runner defaults read state', () => {
  const desktopRunner = { runnerId: 'desktop', displayName: 'Desktop', platform: 'windows', available: true, dispatchEligible: true, unavailableReason: null, capacity: 6, occupied: 0, capacityKind: 'delegatedTasks', stale: false, features: [] }
  const server2Runner = { runnerId: 'server2', displayName: 'server2', platform: 'linux', available: true, dispatchEligible: true, unavailableReason: null, capacity: null, occupied: null, capacityKind: 'sessions', stale: false, features: [] }

  it.each(['failed', 'loading', 'empty'] as const)('C1270 V-2 runner defaults history read %s', async (state) => {
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json(defaults)),
      http.get('/api/session-runners', () => HttpResponse.json([desktopRunner, server2Runner])),
      http.get('/api/runner-defaults/revisions', async () => {
        if (state === 'failed') return HttpResponse.json({ title: 'boom' }, { status: 500 })
        if (state === 'loading') await delay('infinite')
        return HttpResponse.json({ revisions: [], nextBeforeRevision: null })
      }),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    const history = await screen.findByTestId('runner-defaults-history')
    const expected = {
      failed: 'History unknown (revisions read failed: boom).',
      loading: 'History loading.',
      empty: 'No revisions yet.',
    }[state]
    expect(await within(history).findByText(expected)).toBeInTheDocument()
    if (state !== 'empty') expect(history).not.toHaveTextContent('No revisions yet.')
  })

  it.each(['failed', 'loaded'] as const)('C1270 V-2 runner defaults catalogue read %s', async (state) => {
    server.use(
      http.get('/api/runner-defaults', () => HttpResponse.json({ ...defaults, unresolvedReferences: [] })),
      http.get('/api/runner-defaults/revisions', () => HttpResponse.json({ revisions: [], nextBeforeRevision: null })),
      http.get('/api/session-runners', () => state === 'failed'
        ? HttpResponse.json({ title: 'boom' }, { status: 500 })
        : HttpResponse.json([desktopRunner, server2Runner])),
    )
    renderWithProviders(<RunnerDefaultsSection />)
    const global = await screen.findByTestId('runner-defaults-global')
    if (state === 'failed') {
      expect(global).toHaveValue('server2 · catalogue unknown')
      expect(screen.getByText('Runner catalogue unknown (session runners read failed: boom).')).toBeInTheDocument()
      expect(screen.getByTestId('runner-defaults-section')).not.toHaveTextContent('missing from catalogue')
    } else {
      expect(global).toHaveValue('server2 · linux')
      expect(screen.getByTestId('runner-defaults-section')).not.toHaveTextContent('catalogue unknown')
    }
  })
})
