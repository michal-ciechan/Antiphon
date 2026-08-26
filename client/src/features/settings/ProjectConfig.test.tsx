import { HttpResponse, http } from 'msw'
import { fireEvent } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ProjectDto, UpdateProjectRequest } from '../../api/projects'
import type { ProjectReadinessDto } from '../../api/projectSetup'
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

const emptyReadiness: ProjectReadinessDto = {
  projectId: 'project-1',
  canDispatch: false,
  checks: [
    {
      key: 'directory',
      level: 'Required',
      status: 'Missing',
      summary: 'No local directory is set on this project.',
      detail: null,
      fix: { label: 'Edit project', route: '/settings?tab=projects' },
    },
  ],
}

describe('ProjectConfig default launch environment', () => {
  it('renders stored env and submits the parsed dict on save', async () => {
    let submitted: UpdateProjectRequest | null = null
    server.use(
      http.get('/api/projects', () => HttpResponse.json([project])),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/github/repos', () => HttpResponse.json([])),
      http.get('/api/projects/:id/readiness', () => HttpResponse.json(emptyReadiness)),
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

describe('ProjectConfig readiness column', () => {
  it('replaces the Features badges with a readiness cell and empty-state copy', async () => {
    server.use(
      http.get('/api/projects', () => HttpResponse.json([project])),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/github/repos', () => HttpResponse.json([])),
      http.get('/api/projects/:id/readiness', () => HttpResponse.json(emptyReadiness)),
    )

    renderWithProviders(<ProjectConfig />)

    expect(await screen.findByRole('columnheader', { name: 'Readiness' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Features' })).not.toBeInTheDocument()
    expect(await screen.findByLabelText('1 thing missing')).toBeInTheDocument()
    expect(screen.queryByText('GitHub')).not.toBeInTheDocument()
    expect(screen.queryByText('Notifications')).not.toBeInTheDocument()
  })

  it('shows the empty-state copy when there are no projects', async () => {
    server.use(
      http.get('/api/projects', () => HttpResponse.json([])),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/github/repos', () => HttpResponse.json([])),
    )

    renderWithProviders(<ProjectConfig />)

    expect(
      await screen.findByText('No projects yet. Set up a project from a directory path.'),
    ).toBeInTheDocument()
  })
})
