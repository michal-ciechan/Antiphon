import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import type { ProjectReadinessDto, ReadinessCheckDto } from '../../api/projectSetup'
import { server } from '../../test/mocks/server'
import { ProjectReadinessPanel } from './ProjectReadinessPanel'

const notificationMock = vi.hoisted(() => ({ show: vi.fn() }))

vi.mock('@mantine/notifications', () => ({
  notifications: notificationMock,
}))

function check(partial: Partial<ReadinessCheckDto> & Pick<ReadinessCheckDto, 'key'>): ReadinessCheckDto {
  return {
    level: 'Required',
    status: 'Ok',
    summary: `${partial.key} is fine`,
    detail: null,
    fix: null,
    ...partial,
  }
}

const allKeys = [
  'directory',
  'git-repository',
  'board',
  'agent',
  'agent-runner',
  'agent-directory',
  'delegation-root',
  'workflow-template',
  'orchestrator',
  'channel',
  'github',
] as const

function readiness(overrides: Partial<ProjectReadinessDto> = {}): ProjectReadinessDto {
  return {
    projectId: 'project-1',
    canDispatch: true,
    checks: allKeys.map((key) => check({ key })),
    ...overrides,
  }
}

describe('ProjectReadinessPanel', () => {
  it('shows Ready to dispatch when canDispatch is true', () => {
    renderWithProviders(<ProjectReadinessPanel readiness={readiness()} />)
    expect(screen.getByTestId('project-readiness-header')).toHaveTextContent('Ready to dispatch')
  })

  it('lists required-missing rows first and names how many things are missing', () => {
    renderWithProviders(
      <ProjectReadinessPanel
        readiness={readiness({
          canDispatch: false,
          checks: [
            check({ key: 'directory', status: 'Ok', summary: 'Directory exists' }),
            check({
              key: 'board',
              status: 'Missing',
              summary: 'This project has no board.',
              fix: { label: 'Create a board', route: '/boards' },
            }),
            check({
              key: 'agent',
              status: 'Missing',
              summary: 'No standing agent is linked to this project.',
              fix: { label: 'Add an agent', route: '/agents' },
            }),
            check({ key: 'git-repository', level: 'Recommended', status: 'Warning', summary: 'Not a git repo' }),
          ],
        })}
      />,
    )

    expect(screen.getByTestId('project-readiness-header')).toHaveTextContent(
      'Cannot dispatch yet — 2 things missing',
    )
    const rows = screen.getAllByTestId(/readiness-row-/)
    expect(rows[0]).toHaveAttribute('data-testid', 'readiness-row-board')
    expect(rows[1]).toHaveAttribute('data-testid', 'readiness-row-agent')
    expect(rows[2]).toHaveAttribute('data-testid', 'readiness-row-directory')
    expect(screen.getByRole('link', { name: 'Create a board' })).toHaveAttribute('href', '/boards')
  })

  it('calls onAction for create-directory instead of deep-linking when a handler is passed', async () => {
    const onAction = vi.fn()
    renderWithProviders(
      <ProjectReadinessPanel
        onAction={onAction}
        readiness={readiness({
          canDispatch: false,
          checks: [
            check({
              key: 'agent-directory',
              status: 'Missing',
              summary: 'Working directory does not exist',
              fix: { label: 'Create directory', action: 'create-directory', route: '/agents?agent=a1' },
            }),
          ],
        })}
      />,
    )

    expect(screen.queryByRole('link', { name: 'Create directory' })).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Create directory' }))
    expect(onAction).toHaveBeenCalledWith(
      'create-directory',
      expect.objectContaining({ key: 'agent-directory' }),
    )
  })

  it('posts ensure-directory for the agent-directory create-directory fix', async () => {
    const posted: string[] = []
    server.use(
      http.post('/api/agents/a1/ensure-directory', () => {
        posted.push('a1')
        return HttpResponse.json({ agentId: 'a1', workingDirectory: 'D:/missing' })
      }),
    )

    renderWithProviders(
      <ProjectReadinessPanel
        readiness={readiness({
          canDispatch: false,
          checks: [
            check({
              key: 'agent-directory',
              status: 'Missing',
              summary: 'Working directory does not exist',
              fix: { label: 'Create directory', action: 'create-directory', route: '/agents?agent=a1' },
            }),
          ],
        })}
      />,
    )

    expect(screen.queryByRole('link', { name: 'Create directory' })).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Create directory' }))
    await waitFor(() => expect(posted).toEqual(['a1']))
    expect(notificationMock.show).toHaveBeenCalledWith(
      expect.objectContaining({ color: 'green', message: 'Working directory created.' }),
    )
  })

  it('falls back to the route when create-directory has no agent id', () => {
    renderWithProviders(
      <ProjectReadinessPanel
        readiness={readiness({
          canDispatch: false,
          checks: [
            check({
              key: 'directory',
              status: 'Missing',
              summary: 'Directory does not exist',
              fix: { label: 'Create the directory', action: 'create-directory', route: '/settings?tab=projects' },
            }),
          ],
        })}
      />,
    )

    expect(screen.getByRole('link', { name: 'Create the directory' })).toHaveAttribute(
      'href',
      '/settings?tab=projects',
    )
    expect(screen.queryByRole('button', { name: 'Create the directory' })).toBeNull()
  })
})
