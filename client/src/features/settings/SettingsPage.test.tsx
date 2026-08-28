import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SettingsPage } from './SettingsPage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

describe('SettingsPage', () => {
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
      http.get('/api/projects/:id/readiness', ({ params }) => {
        readinessRequests += 1
        return HttpResponse.json({ projectId: params.id, canDispatch: true, checks: [] })
      }),
    )

    renderWithProviders(<SettingsPage />)

    await waitFor(() => expect(templateRequests).toBe(1))
    expect(readinessRequests).toBe(0)

    await userEvent.click(screen.getByRole('tab', { name: /projects/i }))
    await waitFor(() => expect(readinessRequests).toBe(1))
  })
})
