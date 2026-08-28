import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ProjectSetupCatalogDto, ProjectSetupRequest } from '../../api/projectSetup'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ProjectSetupModal } from './ProjectSetupModal'

const directory = 'C:\\src\\starter'

const catalog: ProjectSetupCatalogDto = {
  modelLevels: [],
  replyStyles: [],
  bundles: [{ key: 'delegate-basics', version: '1', stamp: 'stamp', summary: 'Standing rules', chars: 12 }],
  profiles: [],
  presets: [{
    key: 'worker',
    label: 'Worker',
    description: 'A worker',
    alwaysOn: false,
    modelLevel: 'High',
    replyStyle: 'Normal',
    bundleKeys: ['delegate-basics'],
    systemPromptTemplate: 'Work in {directory} for {project} on {board}; {repoUrl}',
    namePattern: '{project} Worker',
  }],
  delegation: {
    allowedRoots: ['C:\\src'],
    allowedRootsIsEmpty: false,
    maxConcurrentTasks: 1,
    maxCostUsdPerRoot: 10,
    maxDepth: 2,
    defaultLevel: 'High',
  },
}

const readiness = {
  projectId: 'project-1',
  canDispatch: true,
  checks: [{ key: 'directory', level: 'Required', status: 'Ok', summary: 'Directory exists', detail: null, fix: null }],
} as const

function seed(post: (request: ProjectSetupRequest) => Response) {
  server.use(
    http.get('/api/projects/setup-catalog', () => HttpResponse.json(catalog)),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    http.get('/api/projects', () => HttpResponse.json([])),
    http.get('/api/filesystem/browse', () => HttpResponse.json({ normalizedPath: directory, exists: true, isDrivesListing: false, suggestions: [] })),
    http.get('/api/filesystem/workspaces', ({ request }) => HttpResponse.json(
      new URL(request.url).searchParams.getAll('path').map((path) => ({ path, isGitRepository: true, repoRoot: path, branch: 'master', isWorktree: false })),
    )),
    http.post('/api/projects/setup', async ({ request }) => post((await request.json()) as ProjectSetupRequest)),
  )
}

async function reachReview() {
  await userEvent.type(await screen.findByRole('textbox', { name: 'Project directory' }), directory)
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
  await userEvent.click(screen.getByLabelText('Skip — no agent yet'))
  expect(screen.getByLabelText('Start agent now')).toBeDisabled()
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
}

describe('ProjectSetupModal', () => {
  it('submits the five-step setup and shows returned readiness and notes', async () => {
    let submitted: ProjectSetupRequest | null = null
    seed((request) => {
      submitted = request
      return HttpResponse.json({
        project: { id: 'project-1', name: 'starter', localRepositoryPath: directory },
        board: { id: 'board-1', projectId: 'project-1', projectName: 'starter', name: 'starter' },
        agent: null,
        readiness,
        notes: ['Git remote read from the checkout.'],
      })
    })

    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await reachReview()
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted).toMatchObject({
      directory,
      name: 'starter',
      boardName: 'starter',
      gitRepositoryUrl: null,
      agent: null,
      startAgent: false,
    })
    expect(await screen.findByTestId('project-readiness-header')).toHaveTextContent('Ready to dispatch')
    expect(screen.getByText('Git remote read from the checkout.')).toBeInTheDocument()
  })

  it('renders a 409 conflict from setup', async () => {
    seed(() => HttpResponse.json({ detail: 'Directory already belongs to another project.' }, { status: 409 }))

    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await reachReview()
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByText('Directory already belongs to another project.')).toBeInTheDocument()
  })

  it('returns a directory validation error to the Directory step', async () => {
    seed(() => HttpResponse.json({ errors: { directory: ['Directory does not exist.'] } }, { status: 422 }))

    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await reachReview()
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))

    expect((await screen.findAllByText('Directory does not exist.')).length).toBeGreaterThan(0)
    expect(screen.getByRole('textbox', { name: 'Project directory' })).toBeInTheDocument()
  })
})
