import { onlineManager, type QueryClient } from '@tanstack/react-query'
import { act, cleanup, fireEvent } from '@testing-library/react'
import { HttpResponse, http, delay } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
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
  presets: [
    {
      key: 'orchestrator',
      label: 'Standing orchestrator',
      description: 'Watches the board',
      alwaysOn: true,
      modelLevel: 'High',
      replyStyle: 'Normal',
      bundleKeys: ['orchestrator', 'board-api'],
      systemPromptTemplate: 'Watch {project} on {board} at {directory}',
      namePattern: '{project} Orchestrator',
      remoteControlEnabled: true,
      defaultWorkflowTemplateId: 'b0000000-0000-0000-0000-000000000001',
    },
    {
      key: 'worker',
      label: 'Worker',
      description: 'A worker',
      alwaysOn: false,
      modelLevel: 'High',
      replyStyle: 'Normal',
      bundleKeys: ['delegate-basics'],
      systemPromptTemplate: 'Work in {directory} for {project} on {board}; {repoUrl}',
      namePattern: '{project} Worker',
      remoteControlEnabled: false,
      defaultWorkflowTemplateId: null,
    },
  ],
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
  it('C458 new project submits worker workspace', async () => {
    let submitted: ProjectSetupRequest | null = null
    seed((request) => {
      submitted = request
      return HttpResponse.json({
        project: { id: 'project-1', name: 'starter', localRepositoryPath: directory },
        board: { id: 'board-1', projectId: 'project-1', projectName: 'starter', name: 'starter' },
        agent: null, readiness, notes: [],
      })
    })
    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await userEvent.type(await screen.findByRole('textbox', { name: 'Project directory' }), directory)
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('textbox', { name: 'Default worker workspace' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Shared' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByLabelText('Skip — no agent yet'))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.defaultWorkerWorkspace).toBe('Shared')
  })

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
      repositoryVisibility: 'Unknown',
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

  it.each(['Normal', 'Phone'] as const)('submits the first-agent preset with explicit %s style', async (style) => {
    let submitted: ProjectSetupRequest | null = null
    seed((request) => {
      submitted = request
      return HttpResponse.json({
        project: { id: 'project-1', name: 'starter', localRepositoryPath: directory },
        board: { id: 'board-1', projectId: 'project-1', projectName: 'starter', name: 'starter' },
        agent: { id: 'agent-1', name: 'starter Orchestrator', replyStyle: request.agent?.replyStyle ?? 'Normal' },
        readiness,
        notes: [],
      })
    })
    server.use(
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([{
        id: 'tui-claude',
        displayName: 'Claude',
        kind: 'ClaudeCode',
        isEnabled: true,
        isDefault: true,
        source: 'User',
        sourceDefinitionName: null,
        revisionId: 'rev-claude',
        revision: 1,
        revisionDetails: {
          id: 'rev-claude',
          revision: 1,
          executable: 'claude',
          arguments: [],
          discoveryArguments: [],
          versionArguments: [],
          workingDirectory: null,
          authenticationMode: 'WrapperManaged',
          nonSecretEnvironment: {},
          secretEnvironmentNames: [],
          modelArgumentName: '--model',
          guidance: '',
          createdAt: '2026-05-18T09:00:00Z',
        },
        commandPreview: { executable: 'claude', arguments: [], workingDirectory: null },
        secretEnvironment: [],
        models: [],
        capabilities: [{ name: 'remoteControl', state: 'Supported', reason: 'supported' }],
        validationSummary: {
          status: 'Succeeded',
          profileRevisionId: 'rev-claude',
          isCurrentRevision: true,
          runnerVersion: null,
          probedAt: '2026-05-18T09:00:00Z',
        },
        createdAt: '2026-05-18T09:00:00Z',
        updatedAt: '2026-05-18T09:00:00Z',
      }])),
      http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    )

    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await userEvent.type(await screen.findByRole('textbox', { name: 'Project directory' }), directory)
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))

    expect(await screen.findByText('Standing orchestrator')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('switch', { name: /Remote control/i })).toBeChecked())
    expect(screen.getByRole('radio', { name: 'Normal' })).toBeChecked()
    await userEvent.click(screen.getByRole('radio', { name: style }))
    await userEvent.click(screen.getByRole('button', { name: 'Back' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('radio', { name: style })).toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.agent).toMatchObject({
      preset: 'orchestrator',
      replyStyle: style,
      remoteControlEnabled: true,
      alwaysOn: true,
      bundleKeys: ['orchestrator', 'board-api'],
    })
  })

  it.each([
    ['configured Medium', 'Medium', 'default Medium tier.'],
    ['undefined level', undefined, 'default tier unknown.'],
    ['null level', null, 'default tier unknown.'],
    ['numeric 999', 999, 'default tier unknown.'],
  ])('C1234 delegation step names the default tier only when it is a model level: %s', async (_name, defaultLevel, expected) => {
    seed(() => HttpResponse.json({}))
    server.use(http.get('/api/projects/setup-catalog', () => HttpResponse.json({
      ...catalog,
      delegation: { ...catalog.delegation, defaultLevel },
    })))
    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await userEvent.type(await screen.findByRole('textbox', { name: 'Project directory' }), directory)
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByLabelText('Skip — no agent yet'))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))

    const summary = await screen.findByText(/concurrent task/)
    expect(summary.textContent).toBe(`Up to 1 concurrent task, depth 2, ${expected}`)
  })
})

const repairStates = ['loading', 'paused', 'failed', 'stale failure'] as const
type RepairState = typeof repairStates[number]
const supportedProfile = {
  id: 'tui-claude', displayName: 'Claude', kind: 'ClaudeCode', isEnabled: true, isDefault: true,
  revisionDetails: { authenticationMode: 'WrapperManaged' }, validationSummary: { status: 'Succeeded' },
  capabilities: [{ name: 'remoteControl', state: 'Supported', reason: 'supported' }],
}
const repairCatalog = {
  ...catalog,
  profiles: [{ id: 'tui-claude', displayName: 'Claude', kind: 'ClaudeCode', isDefault: true, hasActiveRevision: true }],
  bundles: ['orchestrator', 'board-api'].map((key) => ({ key, version: '1', stamp: 's', summary: '', chars: 1 })),
  presets: [{ ...catalog.presets[0], modelLevel: 'Frontier', replyStyle: 'Phone' }],
}

function seedRepair() {
  const submitted = vi.fn()
  seed((request) => {
    submitted(request)
    return HttpResponse.json({
      project: { id: 'project-1', name: 'starter', localRepositoryPath: directory },
      board: { id: 'board-1', projectId: 'project-1', projectName: 'starter', name: 'starter' },
      agent: null, readiness, notes: [],
    })
  })
  server.use(
    http.get('/api/projects/setup-catalog', () => HttpResponse.json(repairCatalog)),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([supportedProfile])),
    http.get('/api/agent-tui/profiles/tui-claude/models', () => HttpResponse.json([])),
  )
  return submitted
}

function unreadHandler(url: string, state: RepairState) {
  server.use(http.get(url, async () => {
    if (state === 'loading') await delay('infinite')
    return HttpResponse.json({ detail: 'catalog unavailable' }, { status: 503 })
  }))
}

async function firstAgent() {
  fireEvent.change(await screen.findByRole('textbox', { name: 'Project directory' }), { target: { value: directory } })
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
}

async function expectRead(queryClient: QueryClient, key: readonly string[], state: RepairState) {
  await waitFor(() => expect(queryClient.getQueryState(key)).toMatchObject({
    status: state.includes('fail') ? 'error' : 'pending',
    fetchStatus: state === 'loading' ? 'fetching' : state === 'paused' ? 'paused' : 'idle',
  }))
}

async function recover(queryClient: QueryClient, key: readonly string[]) {
  await act(async () => {
    onlineManager.setOnline(true)
    await queryClient.cancelQueries({ queryKey: key, exact: true })
    await queryClient.refetchQueries({ queryKey: key, exact: true })
  })
}

describe('C1274 repair setup read boundaries', () => {
  afterEach(() => { cleanup(); onlineManager.setOnline(true) })

  it.each(repairStates)('C1274 repair setup catalog %s withholds the agent draft and recovers preset payload', async (state) => {
    const submitted = seedRepair()
    const key = ['projects', 'setup-catalog']
    if (state === 'paused') onlineManager.setOnline(false)
    else if (state !== 'stale failure') unreadHandler('/api/projects/setup-catalog', state)
    const { queryClient } = renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    if (state === 'stale failure') {
      await waitFor(() => expect(queryClient.getQueryData(key)).toEqual(repairCatalog))
      unreadHandler('/api/projects/setup-catalog', state)
      await act(async () => { await queryClient.refetchQueries({ queryKey: key, exact: true }) })
    }
    await expectRead(queryClient, key, state)
    await firstAgent()
    const label = state === 'loading' ? 'Setup catalog loading.' : state === 'paused'
      ? 'Setup catalog unknown (read paused offline).' : 'Setup catalog unknown (read failed: catalog unavailable).'
    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(screen.queryByLabelText('Attached bundles')).not.toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Always on' })).not.toBeInTheDocument()
    expect(screen.queryByPlaceholderText('No preset prompt')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Create project' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))
    expect(submitted).not.toHaveBeenCalled()

    server.use(http.get('/api/projects/setup-catalog', () => HttpResponse.json(repairCatalog)))
    await recover(queryClient, key)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create project' })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))
    await waitFor(() => expect(submitted).toHaveBeenCalledWith(expect.objectContaining({
      agent: expect.objectContaining({ preset: 'orchestrator', modelLevel: 'Frontier', replyStyle: 'Phone',
        alwaysOn: true, remoteControlEnabled: true, bundleKeys: ['orchestrator', 'board-api'] }),
    })))
  })

  it.each(repairStates)('C1274 repair delegation catalog %s shows unknown instead of zero limits', async (state) => {
    seedRepair()
    const key = ['projects', 'setup-catalog']
    if (state === 'paused') onlineManager.setOnline(false)
    else if (state !== 'stale failure') unreadHandler('/api/projects/setup-catalog', state)
    const { queryClient } = renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    if (state === 'stale failure') {
      await waitFor(() => expect(queryClient.getQueryData(key)).toEqual(repairCatalog))
      unreadHandler('/api/projects/setup-catalog', state)
      await act(async () => { await queryClient.refetchQueries({ queryKey: key, exact: true }) })
    }
    await expectRead(queryClient, key, state)
    await firstAgent()
    await userEvent.click(screen.getByLabelText('Skip — no agent yet'))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    const label = state === 'loading' ? 'Delegation settings loading.' : state === 'paused'
      ? 'Delegation settings unknown (read paused offline).' : 'Delegation settings unknown (read failed: catalog unavailable).'
    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(screen.queryByText('This directory is not under an allowed delegation root.')).not.toBeInTheDocument()
    expect(screen.queryByText(/Up to .* concurrent task/)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Create project' })).toBeDisabled()
  })

  it.each(repairStates)('C1274 repair profiles %s show remote support unknown and block agent create', async (state) => {
    const submitted = seedRepair()
    const key = ['agent-tui', 'profiles']
    const { queryClient } = renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await firstAgent()
    await waitFor(() => expect(screen.getByRole('switch', { name: /^Remote control/ })).toBeChecked())
    if (state === 'paused') onlineManager.setOnline(false)
    else unreadHandler('/api/agent-tui/profiles', state)
    act(() => {
      if (state === 'stale failure') void queryClient.refetchQueries({ queryKey: key, exact: true })
      else void queryClient.resetQueries({ queryKey: key, exact: true })
    })
    await expectRead(queryClient, key, state)
    const label = state === 'loading' ? 'AI Agent TUI profiles loading.' : state === 'paused'
      ? 'AI Agent TUI profiles unknown (read paused offline).' : 'AI Agent TUI profiles unknown (read failed: catalog unavailable).'
    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(screen.queryByText('Not available for this runner.')).not.toBeInTheDocument()
    expect(screen.getByRole('switch', { name: /^Remote control/ })).toBeDisabled()
    expect(screen.getByRole('switch', { name: /^Remote control/ })).toHaveAccessibleName('Remote control Remote control support unknown until AI Agent TUI profiles load.')
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Create project' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))
    expect(submitted).not.toHaveBeenCalled()
    server.use(http.get('/api/agent-tui/profiles', () => HttpResponse.json([supportedProfile])))
    await recover(queryClient, key)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create project' })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))
    await waitFor(() => expect(submitted).toHaveBeenCalledWith(expect.objectContaining({
      agent: expect.objectContaining({ remoteControlEnabled: true, alwaysOn: true, bundleKeys: ['orchestrator', 'board-api'] }),
    })))
  })

  it.each(repairStates)('C1274 repair selected models %s show unknown and block agent create', async (state) => {
    const submitted = seedRepair()
    const { queryClient } = renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await firstAgent()
    await userEvent.click(await screen.findByRole('textbox', { name: /Runner profile/ }))
    await userEvent.click(await screen.findByRole('option', { name: 'Claude (ClaudeCode) · default' }))
    const key = ['agent-tui', 'models', 'tui-claude']
    await waitFor(() => expect(queryClient.getQueryData(key)).toEqual([]))
    if (state === 'paused') onlineManager.setOnline(false)
    else unreadHandler('/api/agent-tui/profiles/tui-claude/models', state)
    act(() => {
      if (state === 'stale failure') void queryClient.refetchQueries({ queryKey: key, exact: true })
      else void queryClient.resetQueries({ queryKey: key, exact: true })
    })
    await expectRead(queryClient, key, state)
    const label = state === 'loading' ? 'Agent models loading.' : state === 'paused'
      ? 'Agent models unknown (read paused offline).' : 'Agent models unknown (read failed: catalog unavailable).'
    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Exact model' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Create project' })).toBeDisabled()
    expect(submitted).not.toHaveBeenCalled()
  })

  it('C1274 repair empty presets are explicit and require skipping the first agent', async () => {
    const submitted = seedRepair()
    server.use(http.get('/api/projects/setup-catalog', () => HttpResponse.json({ ...repairCatalog, presets: [], bundles: [] })))
    renderWithProviders(<ProjectSetupModal opened onClose={() => undefined} />)
    await firstAgent()
    expect(await screen.findByText('No agent presets available.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Create project' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Back' }))
    await userEvent.click(screen.getByRole('button', { name: 'Back' }))
    await userEvent.click(screen.getByLabelText('Skip — no agent yet'))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Create project' })).toBeEnabled()
    await userEvent.click(screen.getByRole('button', { name: 'Create project' }))
    await waitFor(() => expect(submitted).toHaveBeenCalledWith(expect.objectContaining({ agent: null, startAgent: false })))
  })
})
