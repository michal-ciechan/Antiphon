import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, CreateAgentRequest, StartAgentRequest, UpdateAgentRequest } from '../../api/agents'
import type { AgentTuiProfileDto, AgentTuiRunnerTypeDto } from '../../api/agentTui'
import type { CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentAddWorkModal } from './AgentAddWorkModal'
import { AgentCreateModal } from './AgentCreateModal'
import { AgentSettingsModal } from './AgentSettingsModal'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

const grokReason = 'Claude-style remote control is not available.'

const grokProfile = (overrides: Partial<AgentTuiProfileDto> = {}): AgentTuiProfileDto => ({
  id: 'tui-grok',
  displayName: 'Grok',
  kind: 'Grok',
  isEnabled: true,
  isDefault: true,
  source: 'User',
  sourceDefinitionName: null,
  revisionId: 'rev-grok',
  revision: 1,
  revisionDetails: {
    id: 'rev-grok',
    revision: 1,
    executable: 'grok',
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
  commandPreview: { executable: 'grok', arguments: [], workingDirectory: null },
  secretEnvironment: [],
  models: [],
  capabilities: [{ name: 'remoteControl', state: 'Unsupported', reason: grokReason }],
  validationSummary: {
    status: 'Succeeded',
    profileRevisionId: 'rev-grok',
    isCurrentRevision: true,
    runnerVersion: null,
    probedAt: '2026-05-18T09:00:00Z',
  },
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  ...overrides,
})

const claudeProfile = (): AgentTuiProfileDto =>
  grokProfile({
    id: 'tui-claude',
    displayName: 'Claude',
    kind: 'ClaudeCode',
    isDefault: false,
    revisionId: 'rev-claude',
    revisionDetails: {
      ...grokProfile().revisionDetails,
      id: 'rev-claude',
      executable: 'claude',
    },
    commandPreview: { executable: 'claude', arguments: [], workingDirectory: null },
    capabilities: [
      { name: 'remoteControl', state: 'Supported', reason: 'Claude supports Antiphon\'s remote-control launch behaviour.' },
    ],
    validationSummary: {
      status: 'Succeeded',
      profileRevisionId: 'rev-claude',
      isCurrentRevision: true,
      runnerVersion: null,
      probedAt: '2026-05-18T09:00:00Z',
    },
  })

const runnerTypes: AgentTuiRunnerTypeDto[] = [
  {
    kind: 'ClaudeCode',
    displayName: 'Claude Code',
    description: '',
    defaultModelArgumentName: '--model',
    authenticationModes: ['WrapperManaged'],
    curatedModels: [],
    capabilities: [{ name: 'remoteControl', state: 'Supported', reason: 'Claude supports Antiphon\'s remote-control launch behaviour.' }],
    guidance: '',
  },
  {
    kind: 'Grok',
    displayName: 'Grok',
    description: '',
    defaultModelArgumentName: '--model',
    authenticationModes: ['WrapperManaged'],
    curatedModels: [],
    capabilities: [{ name: 'remoteControl', state: 'Unsupported', reason: grokReason }],
    guidance: '',
  },
]

function browseHandler() {
  return http.get('/api/filesystem/browse', ({ request }) => {
    const path = new URL(request.url).searchParams.get('path') ?? ''
    return HttpResponse.json({
      normalizedPath: path,
      exists: path.length > 0,
      isDrivesListing: path.length === 0,
      suggestions: [],
    })
  })
}

const grokAgent: AgentSummaryDto = {
  id: 'agent-grok',
  name: 'HGP Grok',
  slug: 'hgp-grok',
  workingDirectory: 'D:/src/app',
  details: '',
  defaultWorkflowTemplateId: null,
  defaultWorkflowTemplateName: null,
  assignmentPolicy: 'AutoPick',
  status: 'Idle',
  persistentSessionId: null,
  currentCardId: null,
  boardId: 'board-1',
  boardName: 'Board',
  queueLength: 0,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  liveSession: null,
  alwaysOn: false,
  remoteControlEnabled: true,
  supervision: null,
  systemPromptAppend: null,
  modelLevel: 'High',
  working: false,
  kind: 'Grok',
  tuiProfileId: null,
}

const claudeAgent: AgentSummaryDto = {
  ...grokAgent,
  id: 'agent-claude',
  name: 'Frontend Claude',
  slug: 'frontend-claude',
  remoteControlEnabled: false,
  kind: 'ClaudeCode',
  tuiProfileId: 'tui-claude',
}

const boardSummary = {
  id: 'board-1',
  projectId: 'project-1',
  projectName: 'Project',
  name: 'Board',
  description: '',
  trackerKind: 'Internal',
  maxConcurrentSessions: 1,
  cardCount: 0,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
}

const createdCard: CardDto = {
  id: 'card-1',
  boardId: 'board-1',
  boardColumnId: 'col-1',
  ownerSessionId: null,
  currentWorktreeId: null,
  assignedAgentId: null,
  assignedAgentName: null,
  agentQueuePosition: null,
  activeWorkflowRunId: null,
  workflowRunStatus: null,
  currentWorkflowStageName: null,
  identifier: 'CARD-0001',
  title: 'Do the thing',
  description: '',
  importance: 'Critical', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 4,
  labels: [],
  status: 'Backlog',
  concurrencyToken: 'token-1',
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  startedAt: null,
  completedAt: null,
  terminalReason: null,
  sessions: [],
  revisionCount: 0,
  archivedAt: null,
  archivedReason: null,
  archivedBy: null,
}

describe('CARD-0212 remote control capability gate', () => {
  it('create modal disables the switch for an Unsupported default profile and submits false', async () => {
    let submitted: CreateAgentRequest | null = null
    const grok = grokProfile({ isDefault: true })
    const claude = claudeProfile()
    server.use(
      browseHandler(),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([grok, claude])),
      http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
      http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/agents/bundles', () => HttpResponse.json([])),
      http.post('/api/agents', async ({ request }) => {
        submitted = (await request.json()) as CreateAgentRequest
        return HttpResponse.json({ id: 'new-agent', ...submitted, status: 'Idle', queue: [] })
      }),
    )

    renderWithProviders(<AgentCreateModal opened onClose={() => {}} />)

    expect(await screen.findByText(grokReason)).toBeInTheDocument()
    const rcSwitch = screen.getByRole('switch', { name: /Remote control/i })
    expect(rcSwitch).toBeDisabled()
    expect(rcSwitch).not.toBeChecked()

    const name = screen.getAllByLabelText('Name').find((el) => el instanceof HTMLInputElement) as HTMLInputElement
    await userEvent.type(name, 'Grok Agent')
    const directory = screen
      .getAllByLabelText('Working directory')
      .find((el): el is HTMLInputElement => el instanceof HTMLInputElement)!
    await userEvent.clear(directory)
    await userEvent.type(directory, 'D:\\src\\grok-agent')

    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.remoteControlEnabled).toBe(false)
  })

  it('create modal enables the switch when the default profile Supports remote control', async () => {
    const claude = claudeProfile()
    claude.isDefault = true
    server.use(
      browseHandler(),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([claude])),
      http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
      http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/agents/bundles', () => HttpResponse.json([])),
    )

    renderWithProviders(<AgentCreateModal opened onClose={() => {}} />)

    const rcSwitch = await screen.findByRole('switch', { name: /Remote control/i })
    await waitFor(() => expect(rcSwitch).toBeEnabled())
  })

  it('settings modal for a stale Grok row disables the switch and saves false', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      http.get('/api/agents/bundles', () => HttpResponse.json([])),
      http.get('/api/agents/:id', () => HttpResponse.json({ ...grokAgent, queue: [] } satisfies AgentDetailDto)),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
      http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...grokAgent, queue: [], remoteControlEnabled: false })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={grokAgent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    expect(await screen.findByText(grokReason)).toBeInTheDocument()
    const rcSwitch = screen.getByRole('switch', { name: /Remote control/i })
    expect(rcSwitch).toBeDisabled()
    expect(rcSwitch).not.toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.remoteControlEnabled).toBe(false)
  })

  it('add-work modal hides the checkbox for Grok and starts with remoteControl false', async () => {
    let started: StartAgentRequest | null = null
    server.use(
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
      http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
      http.post('/api/boards/:id/cards', () => HttpResponse.json(createdCard)),
      http.post('/api/agents/:id/queue', () => HttpResponse.json({ ...grokAgent, queue: [] })),
      http.post('/api/agents/:id/start', async ({ request }) => {
        started = (await request.json()) as StartAgentRequest
        return HttpResponse.json({ ...grokAgent, queue: [] })
      }),
    )

    renderWithProviders(<AgentAddWorkModal agent={grokAgent} opened onClose={() => {}} />)

    expect(screen.queryByRole('checkbox', { name: /Remote control/i })).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Title'), 'Do the thing')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))
    await waitFor(() => expect(started).not.toBeNull())
    expect(started).toEqual({ remoteControl: false })
  })

  it('add-work modal shows the checkbox for Claude and defaults it on', async () => {
    let started: StartAgentRequest | null = null
    server.use(
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([claudeProfile()])),
      http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
      http.post('/api/boards/:id/cards', () => HttpResponse.json(createdCard)),
      http.post('/api/agents/:id/queue', () => HttpResponse.json({ ...claudeAgent, queue: [] })),
      http.post('/api/agents/:id/start', async ({ request }) => {
        started = (await request.json()) as StartAgentRequest
        return HttpResponse.json({ ...claudeAgent, queue: [] })
      }),
    )

    renderWithProviders(<AgentAddWorkModal agent={claudeAgent} opened onClose={() => {}} />)

    const checkbox = await screen.findByRole('checkbox', { name: /Remote control/i })
    expect(checkbox).toBeChecked()

    await userEvent.type(screen.getByLabelText('Title'), 'Do the thing')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))
    await waitFor(() => expect(started).not.toBeNull())
    expect(started).toEqual({ remoteControl: true })
  })
})
