import { HttpResponse, http } from 'msw'
import { Link } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { ROLE_POLICY_FIXTURE } from '../../test/mocks/handlers'
import { server } from '../../test/mocks/server'
import { SettingsPage } from './SettingsPage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

interface RoutingFetches {
  templates: number
  chainList: number
  effectiveRoles: string[]
  pins: number
  usage: number
  runnerDefaults: number
  rolePolicy: number
}

/** The routing tab's chain handler matches the matrix list and each role-policy effective read. */
function trackRoutingFetches(): RoutingFetches {
  const fetches: RoutingFetches = {
    templates: 0,
    chainList: 0,
    effectiveRoles: [],
    pins: 0,
    usage: 0,
    runnerDefaults: 0,
    rolePolicy: 0,
  }
  server.use(
    http.get('/api/settings/templates', () => {
      fetches.templates += 1
      return HttpResponse.json([])
    }),
    http.get('/api/settings/providers', () => HttpResponse.json([])),
    http.get('/api/settings/template-groups', () => HttpResponse.json([])),
    http.get('/api/role-policy', () => {
      fetches.rolePolicy += 1
      return HttpResponse.json(ROLE_POLICY_FIXTURE)
    }),
    http.get('/api/complexity-chains', ({ request }) => {
      const role = new URL(request.url).searchParams.get('role')
      if (role) fetches.effectiveRoles.push(role)
      else fetches.chainList += 1
      return HttpResponse.json({ chains: [], roles: [], complexities: ['Hard', 'Medium', 'Easy'] })
    }),
    http.get('/api/routing-pins', () => {
      fetches.pins += 1
      return HttpResponse.json({ pins: [] })
    }),
    http.get('/api/subscription-usage', () => {
      fetches.usage += 1
      return HttpResponse.json([])
    }),
    http.get('/api/runner-defaults', ({ request }) => {
      if (new URL(request.url).pathname !== '/api/runner-defaults') return undefined
      fetches.runnerDefaults += 1
      return HttpResponse.json({
        revision: 1, globalRunnerId: null, kindDefaults: [], updatedAt: '2026-09-25T00:00:00Z',
        lastReason: null, lastProvenance: null, lastCallerTaskId: null,
        supportedKinds: ['Grok', 'ClaudeCode', 'Codex'], unresolvedReferences: [],
      })
    }),
  )
  return fetches
}

function expectRoutingIdle(fetches: RoutingFetches) {
  expect(fetches.chainList).toBe(0)
  expect(fetches.effectiveRoles).toEqual([])
  expect(fetches.pins).toBe(0)
  expect(fetches.usage).toBe(0)
  expect(fetches.runnerDefaults).toBe(0)
  expect(fetches.rolePolicy).toBe(0)
}

function expectRoutingMountedOnce(fetches: RoutingFetches) {
  const roles = ROLE_POLICY_FIXTURE.roles.map((row) => row.role).sort()
  expect(fetches.chainList).toBe(1)
  expect([...fetches.effectiveRoles].sort()).toEqual(roles)
  expect(fetches.pins).toBe(1)
  expect(fetches.usage).toBe(1)
  expect(fetches.runnerDefaults).toBe(1)
  expect(fetches.rolePolicy).toBe(1)
}

describe('SettingsPage', () => {
  afterEach(() => {
    window.history.pushState({}, '', '/')
  })

  it('defers project readiness until the Projects tab is opened', async () => {
    let templateRequests = 0
    let readinessRequests = 0

    server.use(
      http.get('/api/settings/templates', () => {
        templateRequests += 1
        return HttpResponse.json([])
      }),
      http.get('/api/settings/providers', () => HttpResponse.json([])),
      http.get('/api/settings/template-groups', () => HttpResponse.json([])),
      http.get('/api/projects', () =>
        HttpResponse.json([
          {
            id: 'project-1',
            name: 'Antiphon',
            gitRepositoryUrl: 'https://example.test/antiphon.git',
            baseBranch: 'master',
            constitutionPath: 'AGENTS.md',
            gitHubIntegrationEnabled: false,
            notificationsEnabled: false,
            createdAt: '2026-08-28T12:00:00Z',
            updatedAt: '2026-08-28T12:00:00Z',
            defaultLaunchEnv: {},
          },
        ]),
      ),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/github/repos', () => HttpResponse.json([])),
      http.get('/api/projects/readiness', ({ request }) => {
        readinessRequests += 1
        const ids = (new URL(request.url).searchParams.get('ids') ?? '').split(',').filter(Boolean)
        return HttpResponse.json(ids.map((id) => ({ projectId: id, canDispatch: true, checks: [] })))
      }),
    )

    renderWithProviders(<SettingsPage />)

    await waitFor(() => expect(templateRequests).toBe(1))
    expect(readinessRequests).toBe(0)

    await userEvent.click(screen.getByRole('tab', { name: /projects/i }))
    await waitFor(() => expect(readinessRequests).toBe(1))
  })

  it('does not fetch routing queries until the Routing tab is opened', async () => {
    const fetches = trackRoutingFetches()

    window.history.pushState({}, '', '/settings')
    renderWithProviders(<SettingsPage />)

    await waitFor(() => expect(fetches.templates).toBe(1))
    expectRoutingIdle(fetches)

    await userEvent.click(screen.getByRole('tab', { name: /routing/i }))
    await waitFor(() => expect(screen.getByTestId('routing-settings-tab')).toBeInTheDocument())
    await waitFor(() => expectRoutingMountedOnce(fetches))
  })

  it('?tab=routing selects the tab and mounts routing queries without fetching templates', async () => {
    const fetches = trackRoutingFetches()

    window.history.pushState({}, '', '/settings?tab=routing')
    renderWithProviders(<SettingsPage />)

    const routingTab = await screen.findByRole('tab', { name: /routing/i })
    expect(routingTab).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByTestId('routing-settings-tab')).toBeInTheDocument()
    await waitFor(() => expectRoutingMountedOnce(fetches))
    expect(fetches.templates).toBe(0)
    expect(screen.getByRole('tab', { name: /templates/i })).toHaveAttribute('aria-selected', 'false')
  })

  // CARD-0506 V-16: the selected tab follows the URL after mount (router link, back/forward), and a
  // tab click writes the URL. The concurrency GET answers 503 so the panel mounts without a fixture.
  it('C506 V16 controlled-tab', async () => {
    let concurrencyRequests = 0
    server.use(
      http.get('/api/settings/templates', () => HttpResponse.json([])),
      http.get('/api/settings/providers', () => HttpResponse.json([])),
      http.get('/api/settings/template-groups', () => HttpResponse.json([])),
      http.get('/api/projects', () => HttpResponse.json([])),
      http.get('/api/dispatch-concurrency', () => {
        concurrencyRequests += 1
        return HttpResponse.json({ title: 'Service Unavailable', status: 503 }, { status: 503 })
      }),
      http.get('/api/dispatch-concurrency/revisions', () => HttpResponse.json({ revisions: [], nextBeforeRevision: null })),
    )
    // jsdom queues a history traversal on timers before it fires popstate, so a synchronous act only
    // schedules it; await the real event (registered first) so the findBy waits start after it.
    const traverse = (go: () => void) =>
      act(async () => {
        await new Promise<void>((resolve) => {
          window.addEventListener('popstate', () => resolve(), { once: true })
          go()
        })
      })
    const selected = (name: string) => expect(screen.getByRole('tab', { name })).toHaveAttribute('aria-selected', 'true')

    window.history.pushState({}, '', '/settings?tab=routing')
    renderWithProviders(
      <>
        <SettingsPage />
        <Link to="/settings?tab=concurrency">open concurrency</Link>
        <Link to="/settings?tab=unknown">open unknown</Link>
      </>,
    )

    expect(await screen.findByTestId('routing-settings-tab')).toBeInTheDocument()
    selected('Routing')
    const names = screen.getAllByRole('tab').map((tab) => tab.textContent)
    expect(names.slice(names.indexOf('Routing'), names.indexOf('Routing') + 3)).toEqual(['Routing', 'Concurrency', 'Status'])
    expect(concurrencyRequests).toBe(0)

    await userEvent.click(screen.getByRole('link', { name: 'open concurrency' }))
    expect(await screen.findByTestId('dispatch-concurrency-section')).toBeInTheDocument()
    selected('Concurrency')
    expect(screen.getByRole('tab', { name: 'Routing' })).toHaveAttribute('aria-selected', 'false')
    expect(screen.queryByTestId('routing-settings-tab')).toBeNull()
    await waitFor(() => expect(concurrencyRequests).toBe(1))

    await traverse(() => window.history.back())
    expect(await screen.findByTestId('routing-settings-tab')).toBeInTheDocument()
    selected('Routing')
    expect(screen.queryByTestId('dispatch-concurrency-section')).toBeNull()

    await traverse(() => window.history.forward())
    expect(await screen.findByTestId('dispatch-concurrency-section')).toBeInTheDocument()
    selected('Concurrency')

    await userEvent.click(screen.getByRole('tab', { name: 'Routing' }))
    expect(await screen.findByTestId('routing-settings-tab')).toBeInTheDocument()
    expect(new URLSearchParams(window.location.search).get('tab')).toBe('routing')

    await userEvent.click(screen.getByRole('link', { name: 'open unknown' }))
    await waitFor(() => selected('Templates'))
    expect(screen.queryByTestId('routing-settings-tab')).toBeNull()
  })
})
