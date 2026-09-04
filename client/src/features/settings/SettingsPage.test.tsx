import { HttpResponse, http } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SettingsPage } from './SettingsPage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

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
    let templateRequests = 0
    let chainRequests = 0
    let pinRequests = 0
    let usageRequests = 0

    server.use(
      http.get('/api/settings/templates', () => {
        templateRequests += 1
        return HttpResponse.json([])
      }),
      http.get('/api/settings/providers', () => HttpResponse.json([])),
      http.get('/api/settings/template-groups', () => HttpResponse.json([])),
      http.get('/api/complexity-chains', () => {
        chainRequests += 1
        return HttpResponse.json({ chains: [], roles: [], complexities: ['Hard', 'Medium', 'Easy'] })
      }),
      http.get('/api/routing-pins', () => {
        pinRequests += 1
        return HttpResponse.json({ pins: [] })
      }),
      http.get('/api/subscription-usage', () => {
        usageRequests += 1
        return HttpResponse.json([])
      }),
    )

    window.history.pushState({}, '', '/settings')
    renderWithProviders(<SettingsPage />)

    await waitFor(() => expect(templateRequests).toBe(1))
    expect(chainRequests).toBe(0)
    expect(pinRequests).toBe(0)
    expect(usageRequests).toBe(0)

    await userEvent.click(screen.getByRole('tab', { name: /routing/i }))
    await waitFor(() => expect(screen.getByTestId('routing-settings-tab')).toBeInTheDocument())
    await waitFor(() => {
      expect(chainRequests).toBe(1)
      expect(pinRequests).toBe(1)
      expect(usageRequests).toBe(1)
    })
  })

  it('?tab=routing selects the tab and mounts routing queries without fetching templates', async () => {
    let templateRequests = 0
    let chainRequests = 0
    let pinRequests = 0
    let usageRequests = 0

    server.use(
      http.get('/api/settings/templates', () => {
        templateRequests += 1
        return HttpResponse.json([])
      }),
      http.get('/api/settings/providers', () => HttpResponse.json([])),
      http.get('/api/settings/template-groups', () => HttpResponse.json([])),
      http.get('/api/complexity-chains', () => {
        chainRequests += 1
        return HttpResponse.json({ chains: [], roles: [], complexities: ['Hard', 'Medium', 'Easy'] })
      }),
      http.get('/api/routing-pins', () => {
        pinRequests += 1
        return HttpResponse.json({ pins: [] })
      }),
      http.get('/api/subscription-usage', () => {
        usageRequests += 1
        return HttpResponse.json([])
      }),
    )

    window.history.pushState({}, '', '/settings?tab=routing')
    renderWithProviders(<SettingsPage />)

    const routingTab = await screen.findByRole('tab', { name: /routing/i })
    expect(routingTab).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByTestId('routing-settings-tab')).toBeInTheDocument()
    await waitFor(() => {
      expect(chainRequests).toBe(1)
      expect(pinRequests).toBe(1)
      expect(usageRequests).toBe(1)
    })
    expect(templateRequests).toBe(0)
    expect(screen.getByRole('tab', { name: /templates/i })).toHaveAttribute('aria-selected', 'false')
  })
})
