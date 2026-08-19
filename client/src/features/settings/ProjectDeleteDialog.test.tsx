import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ProjectDeletionImpactDto, ProjectDto } from '../../api/projects'
import { server } from '../../test/mocks/server'
import { renderWithProviders } from '../../test/utils'
import { ProjectDeleteDialog } from './ProjectDeleteDialog'
import { describeImpact } from './describeImpact'

const PROJECT: ProjectDto = {
  id: 'p1',
  name: 'Antiphon',
  gitRepositoryUrl: 'https://example.test/repo.git',
  baseBranch: 'master',
  constitutionPath: 'AGENTS.md',
  gitHubIntegrationEnabled: false,
  notificationsEnabled: false,
  createdAt: '2026-08-08T00:00:00Z',
  updatedAt: '2026-08-08T00:00:00Z',
}

function impact(overrides: Partial<ProjectDeletionImpactDto> = {}): ProjectDeletionImpactDto {
  const base = {
    projectId: 'p1',
    projectName: 'Antiphon',
    boardCount: 0,
    cardCount: 0,
    openCardCount: 0,
    runningSessionCount: 0,
    detachedAgentCount: 0,
    workflowCount: 0,
    blockers: [] as string[],
    ...overrides,
  }
  return {
    ...base,
    requiresConfirmation:
      overrides.requiresConfirmation ??
      (base.boardCount > 0 || base.cardCount > 0 || base.detachedAgentCount > 0),
    canDelete: overrides.canDelete ?? base.blockers.length === 0,
  }
}

// The shared MSW server is started and reset by src/test/setup.ts; tests here only add handlers.
let deleteRequests: string[] = []

afterEach(() => {
  deleteRequests = []
})

function mockApi(body: ProjectDeletionImpactDto) {
  server.use(
    http.get('*/api/projects/p1/deletion-impact', () => HttpResponse.json(body)),
    http.delete('*/api/projects/p1', ({ request }) => {
      deleteRequests.push(new URL(request.url).search)
      return new HttpResponse(null, { status: 204 })
    }),
  )
}

describe('describeImpact', () => {
  it('counts singular and plural correctly', () => {
    expect(describeImpact(impact({ boardCount: 1, cardCount: 1 }))).toEqual(['1 board', '1 card'])
    expect(describeImpact(impact({ boardCount: 2, cardCount: 5 }))).toEqual(['2 boards', '5 cards'])
  })

  it('calls out outstanding cards — the warning the issue asks for', () => {
    expect(describeImpact(impact({ cardCount: 5, openCardCount: 2 }))).toContain(
      '5 cards — 2 still open',
    )
  })

  it('says agents are detached, not deleted', () => {
    expect(describeImpact(impact({ detachedAgentCount: 2 })).join(' ')).toContain(
      'detached, not deleted',
    )
  })

  it('lists nothing for an empty project', () => {
    expect(describeImpact(impact())).toEqual([])
  })
})

describe('ProjectDeleteDialog', () => {
  it('deletes an empty project on one click, without force', async () => {
    mockApi(impact())
    const user = userEvent.setup()
    renderWithProviders(<ProjectDeleteDialog project={PROJECT} onClose={vi.fn()} />)

    const confirm = await screen.findByRole('button', { name: 'Delete' })
    await waitFor(() => expect(confirm).toBeEnabled())
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()

    await user.click(confirm)

    await waitFor(() => expect(deleteRequests).toEqual(['']))
  })

  it('holds the confirm button until the impact is acknowledged', async () => {
    mockApi(impact({ boardCount: 1, cardCount: 3, openCardCount: 2 }))
    const user = userEvent.setup()
    renderWithProviders(<ProjectDeleteDialog project={PROJECT} onClose={vi.fn()} />)

    const warning = await screen.findByTestId('project-deletion-impact')
    expect(warning).toHaveTextContent('1 board')
    expect(warning).toHaveTextContent('3 cards — 2 still open')

    const confirm = screen.getByRole('button', { name: 'Delete' })
    expect(confirm).toBeDisabled()

    await user.click(screen.getByRole('checkbox'))
    await waitFor(() => expect(confirm).toBeEnabled())
    await user.click(confirm)

    // Acknowledging is what authorises the cascade.
    await waitFor(() => expect(deleteRequests).toEqual(['?force=true']))
  })

  it('refuses outright when something blocks the delete', async () => {
    mockApi(
      impact({
        boardCount: 1,
        workflowCount: 2,
        blockers: ['2 workflows still reference it — delete those first.'],
        canDelete: false,
      }),
    )
    renderWithProviders(<ProjectDeleteDialog project={PROJECT} onClose={vi.fn()} />)

    expect(await screen.findByText(/2 workflows still reference it/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled()
    // No checkbox to tick — force does not override a blocker.
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('closes and reports success once the project is gone', async () => {
    mockApi(impact({ boardCount: 1 }))
    const onClose = vi.fn()
    const onDeleted = vi.fn()
    const user = userEvent.setup()
    renderWithProviders(
      <ProjectDeleteDialog project={PROJECT} onClose={onClose} onDeleted={onDeleted} />,
    )

    await screen.findByTestId('project-deletion-impact')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(onDeleted).toHaveBeenCalled())
    expect(onClose).toHaveBeenCalled()
  })

  it('surfaces a server refusal instead of silently closing', async () => {
    server.use(
      http.get('*/api/projects/p1/deletion-impact', () => HttpResponse.json(impact({ boardCount: 1 }))),
      http.delete('*/api/projects/p1', () =>
        HttpResponse.json({ detail: "'Antiphon' still has 1 board." }, { status: 409 }),
      ),
    )
    const onClose = vi.fn()
    const user = userEvent.setup()
    renderWithProviders(<ProjectDeleteDialog project={PROJECT} onClose={onClose} />)

    await screen.findByTestId('project-deletion-impact')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.getByText(/still has 1 board/)).toBeInTheDocument())
    expect(onClose).not.toHaveBeenCalled()
  })

  it('renders nothing until a project is chosen', () => {
    renderWithProviders(<ProjectDeleteDialog project={null} onClose={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })
})
