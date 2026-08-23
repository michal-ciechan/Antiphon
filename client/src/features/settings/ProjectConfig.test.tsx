import { HttpResponse, http } from 'msw'
import { fireEvent } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ProjectDto, UpdateProjectRequest } from '../../api/projects'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ProjectConfig } from './ProjectConfig'

const notificationMock = vi.hoisted(() => ({ show: vi.fn() }))

vi.mock('@mantine/notifications', () => ({
  notifications: notificationMock,
}))

const project: ProjectDto = {
  id: 'project-1',
  name: 'Antiphon',
  gitRepositoryUrl: 'https://example.test/repo.git',
  baseBranch: 'master',
  constitutionPath: 'AGENTS.md;CLAUDE.md;README.md',
  gitHubIntegrationEnabled: false,
  notificationsEnabled: false,
  createdAt: '2026-08-20T12:00:00Z',
  updatedAt: '2026-08-20T12:00:00Z',
  defaultLaunchEnv: { ANTHROPIC_BASE_URL: 'http://proxy:8080' },
}

describe('ProjectConfig default launch environment', () => {
  it('renders stored env and submits the parsed dict on save', async () => {
    let submitted: UpdateProjectRequest | null = null
    server.use(
      http.get('/api/projects', () => HttpResponse.json([project])),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/github/repos', () => HttpResponse.json([])),
      http.get('/api/projects/:id/api-keys', () => HttpResponse.json([])),
      http.put('/api/projects/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateProjectRequest
        return HttpResponse.json({ ...project, defaultLaunchEnv: submitted.defaultLaunchEnv })
      }),
    )

    renderWithProviders(<ProjectConfig />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit project' }))
    const environment = await screen.findByLabelText('Default launch environment (KEY=value per line)')
    expect(environment).toHaveValue('ANTHROPIC_BASE_URL=http://proxy:8080')
    expect(screen.getByText(/inherited by every agent and pool delegate/i)).toBeInTheDocument()

    fireEvent.change(environment, {
      target: { value: 'ANTHROPIC_BASE_URL=http://proxy:9090\nANTHROPIC_API_KEY={{key:proxy-key}}\nMALFORMED' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.defaultLaunchEnv).toEqual({
      ANTHROPIC_BASE_URL: 'http://proxy:9090',
      ANTHROPIC_API_KEY: '{{key:proxy-key}}',
    })
    expect(notificationMock.show).toHaveBeenCalledWith(expect.objectContaining({
      color: 'yellow',
      message: expect.stringContaining('Line 3 was ignored because it is not KEY=value.'),
    }))
  })
})
