import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, CreateAgentRequest, UpdateAgentRequest } from '../../api/agents'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentCreateModal } from './AgentCreateModal'
import { AgentSettingsModal } from './AgentSettingsModal'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

const claudeProfile = {
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
  capabilities: [{
    name: 'remoteControl',
    state: 'Supported',
    reason: "Claude supports Antiphon's remote-control launch behaviour.",
  }],
  validationSummary: {
    status: 'Succeeded',
    profileRevisionId: 'rev-claude',
    isCurrentRevision: true,
    runnerVersion: null,
    probedAt: '2026-05-18T09:00:00Z',
  },
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
}

const agent: AgentSummaryDto = {
  id: 'agent-1',
  name: 'Frontend Claude',
  slug: 'frontend-claude',
  workingDirectory: 'D:/src/app',
  details: 'UI work',
  defaultWorkflowTemplateId: null,
  defaultWorkflowTemplateName: null,
  assignmentPolicy: 'AutoPick',
  status: 'Idle',
  persistentSessionId: null,
  currentCardId: null,
  boardId: null,
  boardName: null,
  queueLength: 0,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  liveSession: null,
  alwaysOn: false,
  remoteControlEnabled: false,
  supervision: null,
  systemPromptAppend: null,
  modelLevel: 'High',
  working: false,
  sessionBackend: 'Herdr',
  herdrWorkspaceLabel: 'PredictionMarkets',
  herdrTabLabel: 'Orch',
}

const detail: AgentDetailDto = { ...agent, queue: [] }

function commonHandlers() {
  return [
    http.get('/api/filesystem/browse', ({ request }) => {
      const path = new URL(request.url).searchParams.get('path') ?? ''
      return HttpResponse.json({
        normalizedPath: path,
        exists: path.length > 0,
        isDrivesListing: path.length === 0,
        suggestions: [],
      })
    }),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([claudeProfile])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/agents/bundles', () => HttpResponse.json([])),
    http.get('/api/agents', () => HttpResponse.json([agent])),
    http.get('/api/agents/:id', () => HttpResponse.json(detail)),
  ]
}

describe('AgentHerdrPlacement', () => {
  it('create modal shows both inputs only in Herdr mode with the documented placeholders', async () => {
    server.use(...commonHandlers())
    renderWithProviders(<AgentCreateModal opened onClose={() => {}} />)
    expect(screen.queryByPlaceholderText('project name')).not.toBeInTheDocument()
    await userEvent.click(await screen.findByRole('radio', { name: 'Herdr' }))
    expect(await screen.findByPlaceholderText('project name')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('automatic placement')).toBeInTheDocument()
  })

  it('create submits camelCase herdrWorkspaceLabel and herdrTabLabel', async () => {
    let submitted: CreateAgentRequest | null = null
    server.use(
      ...commonHandlers(),
      http.post('/api/agents', async ({ request }) => {
        submitted = (await request.json()) as CreateAgentRequest
        return HttpResponse.json({ ...detail, ...submitted, id: 'new', queue: [] })
      }),
    )
    renderWithProviders(<AgentCreateModal opened onClose={() => {}} />)
    const name = screen.getAllByLabelText('Name').find((el) => el instanceof HTMLInputElement) as HTMLInputElement
    await userEvent.type(name, 'Orch Agent')
    const directory = screen
      .getAllByLabelText('Working directory')
      .find((el): el is HTMLInputElement => el instanceof HTMLInputElement)!
    await userEvent.clear(directory)
    await userEvent.type(directory, 'D:\\src\\app')
    await userEvent.click(await screen.findByRole('radio', { name: 'Herdr' }))
    await userEvent.type(screen.getByPlaceholderText('project name'), 'PredictionMarkets')
    await userEvent.type(screen.getByPlaceholderText('automatic placement'), 'Orch')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.herdrWorkspaceLabel).toBe('PredictionMarkets')
    expect(submitted!.herdrTabLabel).toBe('Orch')
  })

  it('settings modal pre-fills from detail and sends only changed fields', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...commonHandlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, ...submitted })
      }),
    )
    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    expect(await screen.findByDisplayValue('PredictionMarkets')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Orch')).toBeInTheDocument()
    await userEvent.clear(screen.getByDisplayValue('Orch'))
    await userEvent.type(screen.getByPlaceholderText('automatic placement'), 'Orch2')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.herdrTabLabel).toBe('Orch2')
    expect(submitted!.herdrWorkspaceLabel).toBeUndefined()
  })

  it('clearing a field sends an empty string not undefined', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...commonHandlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, herdrTabLabel: null })
      }),
    )
    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    await screen.findByDisplayValue('Orch')
    await userEvent.clear(screen.getByDisplayValue('Orch'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.herdrTabLabel).toBe('')
  })

  it('switching backend away and back keeps typed labels', async () => {
    server.use(...commonHandlers())
    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    const tab = await screen.findByDisplayValue('Orch')
    await userEvent.clear(tab)
    await userEvent.type(screen.getByPlaceholderText('automatic placement'), 'Kept')
    await userEvent.click(screen.getByRole('radio', { name: 'Pty host' }))
    expect(screen.queryByPlaceholderText('automatic placement')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('radio', { name: 'Herdr' }))
    expect(await screen.findByDisplayValue('Kept')).toBeInTheDocument()
  })

  it('saving labels on a live agent PATCHes and never POSTs start', async () => {
    let patched = false
    let started = false
    const live = {
      ...agent,
      status: 'Running' as const,
      persistentSessionId: 'sess-1',
      liveSession: { id: 'sess-1', status: 'Running' },
    }
    server.use(
      ...commonHandlers(),
      http.get('/api/agents/:id', () => HttpResponse.json({ ...detail, ...live, queue: [] })),
      http.patch('/api/agents/:id', async () => {
        patched = true
        return HttpResponse.json({ ...detail, ...live, queue: [] })
      }),
      http.post('/api/agents/:id/start', () => {
        started = true
        return HttpResponse.json({ ...detail, ...live, queue: [] })
      }),
    )
    renderWithProviders(
      <AgentSettingsModal agent={live as AgentSummaryDto} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    await screen.findByDisplayValue('Orch')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(patched).toBe(true))
    expect(started).toBe(false)
  })
})
