import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent } from '../../test/utils'
import type { ProjectReadinessDto, ReadinessCheckDto } from '../../api/projectSetup'
import { ProjectReadinessPanel } from './ProjectReadinessPanel'

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
  })

  it('renders fix buttons that deep-link and that call onAction', async () => {
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

    const links = screen.getAllByRole('link', { name: 'Create directory' })
    expect(links[0]).toHaveAttribute('href', '/agents?agent=a1')
    await userEvent.click(screen.getByRole('button', { name: 'Create directory' }))
    expect(onAction).toHaveBeenCalledWith(
      'create-directory',
      expect.objectContaining({ key: 'agent-directory' }),
    )
  })
})
