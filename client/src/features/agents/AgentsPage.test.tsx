import { HttpResponse, http } from 'msw'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, InstructionBundleDto } from '../../api/agents'
import type { ProjectReadinessDto } from '../../api/projectSetup'
import type { AgentTuiProfileDto } from '../../api/agentTui'
import type { AgentSessionSummaryDto, BoardDetailDto, BoardSummaryDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentCard, AgentsPage } from './AgentsPage'

vi.mock('@mantine/notifications', () => ({
  notifications: {
    show: vi.fn(),
  },
}))

const agentSummary: AgentSummaryDto = {
  id: 'agent-1',
  name: 'Frontend Claude',
  slug: 'frontend-claude',
  workingDirectory: 'D:/src/app',
  details: 'UI work',
  defaultWorkflowTemplateId: 'template-1',
  defaultWorkflowTemplateName: 'One Shot',
  assignmentPolicy: 'AutoPick',
  status: 'Idle',
  persistentSessionId: null,
  currentCardId: null,
  boardId: 'board-1',
  boardName: 'Frontend Board',
  queueLength: 2,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  liveSession: null,
  alwaysOn: false,
  remoteControlEnabled: false,
  supervision: null,
  systemPromptAppend: null,
  modelLevel: 'High',
  working: false,
  // CARD-0212: useRemoteControlSupport resolves via the profile, which needs a matching
  // Supported capability below - without both, every remote-control default in this file
  // reads as unsupported and every start call this file asserts on would send `false`.
  tuiProfileId: 'tui-profile-1',
}

const agentDetail: AgentDetailDto = {
  ...agentSummary,
  queue: [],
}

const agentTuiProfile: AgentTuiProfileDto = {
  id: 'tui-profile-1',
  displayName: 'Local Claude',
  kind: 'ClaudeCode',
  isEnabled: true,
  isDefault: true,
  source: 'User',
  sourceDefinitionName: null,
  revisionId: 'tui-profile-revision-1',
  revision: 1,
  revisionDetails: {
    id: 'tui-profile-revision-1',
    revision: 1,
    executable: 'claude',
    arguments: [],
    discoveryArguments: [],
    versionArguments: ['--version'],
    workingDirectory: 'D:/src/app',
    authenticationMode: 'WrapperManaged',
    nonSecretEnvironment: {},
    secretEnvironmentNames: [],
    modelArgumentName: '--model',
    guidance: '',
    createdAt: '2026-05-18T09:00:00Z',
  },
  commandPreview: {
    executable: 'claude',
    arguments: [],
    workingDirectory: 'D:/src/app',
  },
  secretEnvironment: [],
  models: [],
  // CARD-0212: this fixture's agent is ClaudeCode, which the real catalog marks Supported -
  // an empty list here would make useRemoteControlSupport read every test's default as
  // unsupported, contradicting this file's own "remote control is on by default" fixtures.
  capabilities: [{ name: 'remoteControl', state: 'Supported', reason: 'Claude supports Antiphon\'s remote-control launch behaviour.' }],
  validationSummary: {
    status: 'Succeeded',
    profileRevisionId: 'tui-profile-revision-1',
    isCurrentRevision: true,
    runnerVersion: 'Claude Code',
    probedAt: '2026-05-18T09:00:00Z',
  },
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
}

const liveSession: AgentSessionSummaryDto = {
  id: 'session-1',
  definitionName: 'claude',
  agentKind: 'ClaudeCode',
  status: 'Running',
  cwd: 'D:/src/app',
  createdAt: '2026-05-18T09:00:00Z',
  startedAt: '2026-05-18T09:00:00Z',
  lastSeenAt: '2026-05-18T09:00:00Z',
  endedAt: null,
  exitCode: null,
  failureReason: null,
}

const boardSummary: BoardSummaryDto = {
  id: 'board-1',
  projectId: 'project-1',
  projectName: 'Project One',
  name: 'Delivery',
  description: '',
  trackerKind: 'Internal',
  maxConcurrentSessions: 1,
  cardCount: 1,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
}

const boardDetail: BoardDetailDto = {
  ...boardSummary,
  columns: [
    {
      id: 'column-backlog',
      stateKey: 'backlog',
      name: 'Backlog',
      columnOrder: 0,
      cardStatus: 'Backlog',
      isActive: false,
      isTerminal: false,
      maxConcurrentSessions: null,
      cards: [
        {
          id: 'card-1',
          boardId: 'board-1',
          boardColumnId: 'column-backlog',
          ownerSessionId: null,
          currentWorktreeId: null,
          assignedAgentId: null,
          assignedAgentName: null,
          agentQueuePosition: null,
          activeWorkflowRunId: null,
          workflowRunStatus: null,
          currentWorkflowStageName: null,
          identifier: 'CARD-0001',
          title: 'Build agent UI',
          description: 'Create the roster page',
          priority: 1,
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
        },
      ],
    },
  ],
}

const instructionBundles: InstructionBundleDto[] = [
  {
    key: 'board-api',
    version: '1a2b3c4d',
    stamp: 'board-api v1a2b3c4d',
    summary: 'Working the Antiphon board.',
    chars: 2400,
  },
]

const readyReadiness: ProjectReadinessDto = {
  projectId: 'project-1',
  canDispatch: true,
  checks: [
    { key: 'agent-directory', level: 'Required', status: 'Ok', summary: 'Working directory exists' },
    { key: 'agent-runner', level: 'Required', status: 'Ok', summary: 'Runner profile is enabled' },
  ],
}

function agentHandlers(summary: AgentSummaryDto[] = [agentSummary], detail: AgentDetailDto = agentDetail) {
  return [
    http.get('/api/agents', () => HttpResponse.json(summary)),
    // BEFORE the ':id' pattern, which would otherwise swallow it and answer an agent detail
    // object where the settings modal expects a list of bundles (CARD-0058 slice 6).
    http.get('/api/agents/bundles', () => HttpResponse.json(instructionBundles)),
    http.get('/api/agents/:id', () => HttpResponse.json(detail)),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([agentTuiProfile])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    http.get('/api/boards', () => HttpResponse.json([boardSummary])),
    http.get('/api/projects/readiness', ({ request }) => {
      const ids = (new URL(request.url).searchParams.get('ids') ?? '').split(',').filter(Boolean)
      return HttpResponse.json(ids.map((id) => ({ ...readyReadiness, projectId: id })))
    }),
  ]
}

// The working-directory autocomplete in AgentCreateModal browses on focus/typing.
// Report any non-empty path as existing so the missing-dir rule doesn't gate Create,
// and satisfy MSW's onUnhandledRequest: 'error'.
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

function getVisibleInput(label: string) {
  return screen
    .getAllByLabelText(label)
    .find((element): element is HTMLInputElement =>
      element instanceof HTMLInputElement && element.getAttribute('type') !== 'hidden',
    ) as HTMLInputElement
}

describe('AgentsPage', () => {
  it('renders agent roster with queue length and no badge for quiet states', async () => {
    server.use(...agentHandlers())

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('Frontend Claude')).toBeInTheDocument()
    expect(screen.getByText('2 queued')).toBeInTheDocument()
    // Idle is a quiet state: no status badge — liveness is the terminal icon's colour.
    expect(screen.queryByText('Idle')).not.toBeInTheDocument()
  })

  // "Working" must mean mid-turn RIGHT NOW (transcript-derived), never merely "started".
  it('shows the Working spinner badge only for the transcript-working agent', async () => {
    const workingAgent: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-2',
      name: 'Busy Agent',
      slug: 'busy-agent',
      status: 'Running',
      working: true,
    }
    const startedIdleAgent: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-3',
      name: 'Started But Idle',
      slug: 'started-idle',
      status: 'Running', // started, session live — but not mid-turn
      working: false,
    }
    server.use(...agentHandlers([workingAgent, startedIdleAgent], { ...agentDetail, id: 'agent-2' }))

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('Busy Agent')).toBeInTheDocument()
    expect(screen.getByTestId('agent-working-agent-2')).toHaveTextContent('Working')
    expect(screen.queryByTestId('agent-working-agent-3')).not.toBeInTheDocument()
  })

  // The detail header renders the same activity badge as the card.
  it('shows the Working spinner in the detail header when the detail is mid-turn', async () => {
    // Card quiet (working: false) so the only spinner badge on the page is the header's.
    server.use(...agentHandlers([agentSummary], { ...agentDetail, working: true }))

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByTestId('agent-working-agent-1')).toHaveTextContent('Working')
  })

  it('shows no activity badge for the quiet lifecycle states', async () => {
    const quiet = (id: string, status: AgentSummaryDto['status']): AgentSummaryDto => ({
      ...agentSummary,
      id,
      name: `Agent ${status}`,
      slug: `agent-${status.toLowerCase()}`,
      status,
    })
    server.use(
      ...agentHandlers(
        [quiet('agent-idle', 'Idle'), quiet('agent-ready', 'Ready'), quiet('agent-stopped', 'Stopped')],
        { ...agentDetail, id: 'agent-idle', status: 'Idle' },
      ),
    )

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('Agent Idle')).toBeInTheDocument()
    // Quiet states carry no badge — liveness lives in the terminal icon's colour.
    expect(screen.queryByText('Idle')).not.toBeInTheDocument()
    expect(screen.queryByText('Ready')).not.toBeInTheDocument()
    expect(screen.queryByText('Stopped')).not.toBeInTheDocument()
    expect(screen.queryByText('Working')).not.toBeInTheDocument()
  })

  it('badges the attention states: Review, Failed, Disconnected', async () => {
    const attention = (id: string, status: AgentSummaryDto['status']): AgentSummaryDto => ({
      ...agentSummary,
      id,
      name: `Agent ${status}`,
      slug: `agent-${status.toLowerCase()}`,
      status,
    })
    server.use(
      ...agentHandlers(
        [
          attention('agent-review', 'WaitingForHumanReview'),
          attention('agent-failed', 'Failed'),
          attention('agent-disc', 'Disconnected'),
        ],
        { ...agentDetail, id: 'agent-review', status: 'WaitingForHumanReview' },
      ),
    )

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('Agent Failed')).toBeInTheDocument()
    // WaitingForHumanReview renders as the short "Review" label (card + selected detail header).
    expect(screen.getAllByText('Review').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.getByText('Disconnected')).toBeInTheDocument()
  })

  // Pins the badge precedence: a genuinely mid-turn agent shows the spinner even when the
  // lifecycle status is an attention state.
  it('prefers the Working spinner over the attention badge when mid-turn', async () => {
    const failedButWorking: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-2',
      name: 'Failed Yet Busy',
      slug: 'failed-yet-busy',
      status: 'Failed',
      working: true,
    }
    server.use(...agentHandlers([failedButWorking], { ...agentDetail, id: 'agent-2', status: 'Failed', working: true }))

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByTestId('agent-working-agent-2')).toHaveTextContent('Working')
    expect(screen.queryByText('Failed')).not.toBeInTheDocument()
  })

  // Stop gating is liveSession-OR-started: a live session with a quiet lifecycle status must
  // still offer Stop (the session is real), not Start.
  it('offers Stop when a live session exists even though the lifecycle status is quiet', async () => {
    const stoppedWithSession: AgentSummaryDto = {
      ...agentSummary,
      status: 'Stopped',
      persistentSessionId: 'session-1',
      liveSession,
    }
    server.use(...agentHandlers([stoppedWithSession], { ...stoppedWithSession, queue: [] }))

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByRole('button', { name: 'Stop' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start' })).not.toBeInTheDocument()
  })

  // Liveness is the terminal icon's colour/tooltip, not a badge.
  it('describes terminal liveness in the card terminal icon tooltip', async () => {
    const runningAgent: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-live',
      name: 'Live Agent',
      slug: 'live-agent',
      liveSession,
    }
    const startingAgent: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-starting',
      name: 'Starting Agent',
      slug: 'starting-agent',
      liveSession: { ...liveSession, id: 'session-2', status: 'Starting' },
    }
    server.use(
      ...agentHandlers([runningAgent, startingAgent, agentSummary], { ...agentDetail, id: 'agent-live' }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.hover(await screen.findByRole('button', { name: 'Terminal Live Agent' }))
    expect(await screen.findByText('Terminal — live now')).toBeInTheDocument()
    await userEvent.hover(screen.getByRole('button', { name: 'Terminal Starting Agent' }))
    expect(await screen.findByText('Terminal starting…')).toBeInTheDocument()
    await userEvent.hover(screen.getByRole('button', { name: 'Terminal Frontend Claude' }))
    expect(await screen.findByText('No terminal — start agent')).toBeInTheDocument()
  })

  // Settings-via-menu is covered by the edit/delete tests below; here we pin the files link.
  it('links the files view from the card menu', async () => {
    server.use(...agentHandlers())

    renderWithProviders(<AgentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Agent menu Frontend Claude' }))
    const filesItem = await screen.findByRole('menuitem', { name: /Open files view/ })
    expect(filesItem).toHaveAttribute('href', '/agents/agent-1/files')
  })

  it('links the selected agent to its board', async () => {
    server.use(...agentHandlers())

    renderWithProviders(<AgentsPage />)

    const boardLink = await screen.findByRole('link', { name: /Frontend Board/ })
    expect(boardLink).toHaveAttribute('href', '/boards/board-1')
  })

  it('edits an agent and changes its board via the settings modal', async () => {
    const patchSpy = vi.fn()
    server.use(
      ...agentHandlers(),
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.patch('/api/agents/:id', async ({ request }) => {
        patchSpy(await request.json())
        return HttpResponse.json({ ...agentDetail, details: 'updated details' })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Agent menu Frontend Claude' }))
    await userEvent.click(await screen.findByRole('menuitem', { name: /Edit settings/ }))

    const detailsField = await screen.findByLabelText('Details')
    await userEvent.clear(detailsField)
    await userEvent.type(detailsField, 'updated details')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(patchSpy).toHaveBeenCalledTimes(1))
    expect(patchSpy.mock.calls[0][0]).toMatchObject({
      name: 'Frontend Claude',
      details: 'updated details',
      boardId: 'board-1',
      assignmentPolicy: 'AutoPick',
    })
  })

  it('deletes an agent via the settings modal', async () => {
    const deleteSpy = vi.fn()
    server.use(
      ...agentHandlers(),
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.delete('/api/agents/:id', ({ params }) => {
        deleteSpy(params.id)
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Agent menu Frontend Claude' }))
    await userEvent.click(await screen.findByRole('menuitem', { name: /Edit settings/ }))
    // Two-step confirmation: the trigger reveals the confirm button (same label).
    await userEvent.click(await screen.findByRole('button', { name: 'Delete agent' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Delete agent' }))

    await waitFor(() => expect(deleteSpy).toHaveBeenCalledWith('agent-1'))
  })

  it('loads selected agent detail and shows queue card title', async () => {
    const backendAgent: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-2',
      name: 'Backend Codex',
      slug: 'backend-codex',
      workingDirectory: 'D:/src/api',
      queueLength: 1,
    }
    const backendDetail: AgentDetailDto = {
      ...backendAgent,
      queue: [
        {
          cardId: 'card-2',
          boardId: 'board-1',
          boardName: 'Delivery',
          identifier: 'CARD-0002',
          title: 'Wire queue endpoints',
          priority: 2,
          queuePosition: 1,
          activeWorkflowRunId: 'run-1',
          workflowStatus: 'Queued',
          currentStageName: 'Implement',
        },
      ],
    }

    server.use(
      http.get('/api/agents', () => HttpResponse.json([agentSummary, backendAgent])),
      http.get('/api/agents/:id', ({ params }) =>
        HttpResponse.json(params.id === 'agent-2' ? backendDetail : agentDetail),
      ),
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.get('/api/projects/readiness', () => HttpResponse.json([readyReadiness])),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Agent Backend Codex' }))

    expect(await screen.findByText(/Wire queue endpoints/)).toBeInTheDocument()
    expect(screen.getByText('Implement')).toBeInTheDocument()
  })

  it('supports keyboard selection from the agent roster', async () => {
    const backendAgent: AgentSummaryDto = {
      ...agentSummary,
      id: 'agent-2',
      name: 'Backend Codex',
      slug: 'backend-codex',
      workingDirectory: 'D:/src/api',
      queueLength: 1,
    }
    const backendDetail: AgentDetailDto = {
      ...backendAgent,
      queue: [
        {
          cardId: 'card-2',
          boardId: 'board-1',
          boardName: 'Delivery',
          identifier: 'CARD-0002',
          title: 'Keyboard selected card',
          priority: 2,
          queuePosition: 1,
          activeWorkflowRunId: 'run-1',
          workflowStatus: 'Queued',
          currentStageName: 'Implement',
        },
      ],
    }

    server.use(
      http.get('/api/agents', () => HttpResponse.json([agentSummary, backendAgent])),
      http.get('/api/agents/:id', ({ params }) =>
        HttpResponse.json(params.id === 'agent-2' ? backendDetail : agentDetail),
      ),
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.get('/api/projects/readiness', () => HttpResponse.json([readyReadiness])),
    )

    renderWithProviders(<AgentsPage />)

    const backendButton = await screen.findByRole('button', { name: 'Agent Backend Codex' })
    backendButton.focus()
    await userEvent.keyboard('{Enter}')

    expect(await screen.findByText(/Keyboard selected card/)).toBeInTheDocument()
  })

  it('creates an agent from the modal', async () => {
    const createSpy = vi.fn()
    server.use(
      ...agentHandlers([]),
      browseHandler(),
      http.post('/api/agents', async ({ request }) => {
        createSpy(await request.json())
        return HttpResponse.json({ ...agentDetail, id: 'agent-created', queueLength: 0 }, { status: 201 })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(screen.getByRole('button', { name: 'New Agent' }))
    await userEvent.type(await screen.findByLabelText('Name'), 'Frontend Claude')
    await userEvent.type(getVisibleInput('Working directory'), 'D:/src/app')
    await userEvent.type(screen.getByLabelText('Details'), 'UI work')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(createSpy).toHaveBeenCalledWith({
        name: 'Frontend Claude',
        workingDirectory: 'D:/src/app',
        details: 'UI work',
        assignmentPolicy: 'AutoPick',
        createWorkingDirectory: false,
        tuiProfileId: 'tui-profile-1',
        modelId: null,
        modelLevel: 'High',
        // CARD-0060: create carries a style, and Normal is what an untouched picker means.
        replyStyle: 'Normal',
        // CARD-0008: supervision flags ride create; untouched switches stay off.
        alwaysOn: false,
        remoteControlEnabled: false,
        bundleKeys: [],
        systemPromptAppend: null,
      }),
    )
  })

  it('creates a supervised agent when the always-on and remote-control switches are on', async () => {
    const createSpy = vi.fn()
    server.use(
      ...agentHandlers([]),
      browseHandler(),
      http.post('/api/agents', async ({ request }) => {
        createSpy(await request.json())
        return HttpResponse.json({ ...agentDetail, id: 'agent-created', queueLength: 0 }, { status: 201 })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(screen.getByRole('button', { name: 'New Agent' }))
    await userEvent.type(await screen.findByLabelText('Name'), 'Supervised Claude')
    await userEvent.type(getVisibleInput('Working directory'), 'D:/src/app')

    const alwaysOn = await screen.findByRole('switch', { name: /Always on/i })
    const remoteControl = screen.getByRole('switch', { name: /Remote control/i })
    expect(alwaysOn).not.toBeChecked()
    expect(remoteControl).not.toBeChecked()

    await userEvent.click(alwaysOn)
    await userEvent.click(remoteControl)
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(createSpy).toHaveBeenCalledWith({
        name: 'Supervised Claude',
        workingDirectory: 'D:/src/app',
        details: null,
        assignmentPolicy: 'AutoPick',
        createWorkingDirectory: false,
        tuiProfileId: 'tui-profile-1',
        modelId: null,
        modelLevel: 'High',
        replyStyle: 'Normal',
        alwaysOn: true,
        remoteControlEnabled: true,
        bundleKeys: [],
        systemPromptAppend: null,
      }),
    )
  })

  it('drafts agent fields from a description before creating', async () => {
    const draftSpy = vi.fn()
    const createSpy = vi.fn()
    server.use(
      ...agentHandlers([]),
      browseHandler(),
      http.post('/api/agents/draft', async ({ request }) => {
        draftSpy(await request.json())
        return HttpResponse.json({
          name: 'Frontend Agent',
          workingDirectory: 'D:/src/Antiphon/client',
          details: 'Owns React and Mantine UI work.',
          assignmentPolicy: 'ManualConfirm',
          usedAi: true,
        })
      }),
      http.post('/api/agents', async ({ request }) => {
        createSpy(await request.json())
        return HttpResponse.json({
          ...agentDetail,
          id: 'agent-drafted',
          name: 'Frontend Agent',
          workingDirectory: 'D:/src/Antiphon/client',
          details: 'Owns React and Mantine UI work.',
          assignmentPolicy: 'ManualConfirm',
          queueLength: 0,
        }, { status: 201 })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(screen.getByRole('button', { name: 'New Agent' }))
    await userEvent.type(
      await screen.findByLabelText('Describe what you want'),
      'Frontend agent for D:/src/Antiphon/client with manual review',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Draft details' }))

    expect(await screen.findByDisplayValue('Frontend Agent')).toBeInTheDocument()
    expect(screen.getByDisplayValue('D:/src/Antiphon/client')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Owns React and Mantine UI work.')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Manual confirm')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(draftSpy).toHaveBeenCalledWith({
        description: 'Frontend agent for D:/src/Antiphon/client with manual review',
      }),
    )
    await waitFor(() =>
      expect(createSpy).toHaveBeenCalledWith({
        name: 'Frontend Agent',
        workingDirectory: 'D:/src/Antiphon/client',
        details: 'Owns React and Mantine UI work.',
        assignmentPolicy: 'ManualConfirm',
        createWorkingDirectory: false,
        tuiProfileId: 'tui-profile-1',
        modelId: null,
        modelLevel: 'High',
        // CARD-0060: create carries a style, and Normal is what an untouched picker means.
        replyStyle: 'Normal',
        // CARD-0008: draft path does not guess these; they stay off until toggled.
        alwaysOn: false,
        remoteControlEnabled: false,
        bundleKeys: [],
        systemPromptAppend: null,
      }),
    )
  })

  it('shows backend validation text when agent creation fails', async () => {
    server.use(
      ...agentHandlers([]),
      browseHandler(),
      http.post('/api/agents', () =>
        HttpResponse.json({
          title: 'Validation failed',
          detail: 'One or more validation errors occurred.',
          errors: {
            Name: ['Agent name is required.'],
          },
        }, { status: 422 }),
      ),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(screen.getByRole('button', { name: 'New Agent' }))
    await userEvent.type(await screen.findByLabelText('Name'), 'Frontend Claude')
    await userEvent.type(getVisibleInput('Working directory'), 'D:/src/app')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('Agent name is required.')).toBeInTheDocument()
  })

  it('creates a new card, queues it, and starts the agent in remote control', async () => {
    const createSpy = vi.fn()
    const assignSpy = vi.fn()
    const startSpy = vi.fn()
    const newCard = { ...boardDetail.columns[0].cards[0], id: 'new-card', title: 'Wire the thing' }
    const queuedDetail: AgentDetailDto = {
      ...agentDetail,
      queue: [
        {
          cardId: 'new-card',
          boardId: 'board-1',
          boardName: 'Frontend Board',
          identifier: 'CARD-0002',
          title: 'Wire the thing',
          priority: 0,
          queuePosition: 1,
          activeWorkflowRunId: null,
          workflowStatus: null,
          currentStageName: null,
        },
      ],
    }

    server.use(
      ...agentHandlers([agentSummary], agentDetail),
      http.get('/api/boards', () => HttpResponse.json([boardSummary])),
      http.post('/api/boards/board-1/cards', async ({ request }) => {
        createSpy(await request.json())
        return HttpResponse.json(newCard, { status: 201 })
      }),
      http.post('/api/agents/agent-1/queue', async ({ request }) => {
        assignSpy(await request.json())
        return HttpResponse.json(queuedDetail)
      }),
      http.post('/api/agents/agent-1/start', async ({ request }) => {
        startSpy(await request.json())
        return HttpResponse.json({ ...queuedDetail, status: 'Running', persistentSessionId: 'session-1' })
      }),
    )

    renderWithProviders(<AgentsPage />)

    // The agent owns board-1, so the modal creates the card there directly (no board picker).
    await userEvent.click(await screen.findByRole('button', { name: 'Add Card' }))
    await userEvent.type(await screen.findByLabelText('Title'), 'Wire the thing')
    await userEvent.type(screen.getByLabelText('Description'), 'do it well')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(createSpy).toHaveBeenCalledTimes(1))
    expect(createSpy.mock.calls[0][0]).toMatchObject({ title: 'Wire the thing', description: 'do it well' })
    await waitFor(() => expect(assignSpy).toHaveBeenCalledWith({ cardId: 'new-card' }))
    // Remote control is on by default, so the booted agent should be put into remote control.
    await waitFor(() => expect(startSpy).toHaveBeenCalledWith({ remoteControl: true }))
  })

  it('starts the agent process from the detail panel', async () => {
    const startSpy = vi.fn()
    const queuedDetail: AgentDetailDto = {
      ...agentDetail,
      queue: [
        {
          cardId: 'card-1',
          boardId: 'board-1',
          boardName: 'Frontend Board',
          identifier: 'CARD-0001',
          title: 'Build agent UI',
          priority: 1,
          queuePosition: 1,
          activeWorkflowRunId: null,
          workflowStatus: null,
          currentStageName: null,
        },
      ],
    }

    server.use(
      ...agentHandlers([agentSummary], queuedDetail),
      http.post('/api/agents/agent-1/start', async ({ request }) => {
        startSpy(await request.json())
        return HttpResponse.json({ ...queuedDetail, status: 'Running', persistentSessionId: 'session-1' })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Start' }))

    // Remote control comes from the persisted agent setting; the start call itself carries no flag.
    await waitFor(() => expect(startSpy).toHaveBeenCalledWith({}))
    // Once running, the control flips to Stop.
    expect(await screen.findByRole('button', { name: 'Stop' })).toBeInTheDocument()
  })

  it('offers to start the agent from the card CLI icon when no terminal is running', async () => {
    const startSpy = vi.fn()
    server.use(
      ...agentHandlers(),
      http.post('/api/agents/agent-1/start', async ({ request }) => {
        startSpy(await request.json())
        return HttpResponse.json({ ...agentDetail, status: 'Running', persistentSessionId: 'session-1' })
      }),
    )

    renderWithProviders(<AgentsPage />)

    // The card's terminal icon opens the CLI modal; with no live session it offers to start.
    await userEvent.click(await screen.findByRole('button', { name: 'Terminal Frontend Claude' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Start agent' }))

    // Remote control mirrors the agent's persisted setting (false in this fixture) — explicitly
    // passing the persisted value is equivalent to omitting it server-side.
    await waitFor(() => expect(startSpy).toHaveBeenCalledWith({ fresh: false, remoteControl: false }))
  })

  it('chips directory missing from the project readiness checks', async () => {
    server.use(
      http.get('/api/projects/readiness', () =>
        HttpResponse.json([{
          ...readyReadiness,
          canDispatch: false,
          checks: [
            {
              key: 'agent-directory',
              level: 'Required',
              status: 'Missing',
              summary: 'Working directory does not exist',
            },
            { key: 'agent-runner', level: 'Required', status: 'Ok', summary: 'Runner profile is enabled' },
          ],
        } satisfies ProjectReadinessDto]),
      ),
      ...agentHandlers(),
    )

    renderWithProviders(<AgentsPage />)

    await waitFor(() =>
      expect(screen.getByTestId('agent-readiness-agent-1')).toHaveTextContent('directory missing'),
    )
  })

  it('requests readiness only for the selected agent project', async () => {
    const agents = Array.from({ length: 49 }, (_, index) => ({
      ...agentSummary,
      id: `agent-${index + 1}`,
      name: `Agent ${index + 1}`,
      boardId: `board-${index + 1}`,
    }))
    const boards = agents.map((_, index) => ({
      ...boardSummary,
      id: `board-${index + 1}`,
      projectId: `project-${index + 1}`,
    }))
    const requests: string[] = []
    server.use(
      http.get('/api/projects/readiness', ({ request }) => {
        requests.push(new URL(request.url).searchParams.get('ids') ?? '')
        return HttpResponse.json([{ ...readyReadiness, projectId: 'project-1' }])
      }),
      http.get('/api/boards', () => HttpResponse.json(boards)),
      ...agentHandlers(agents, { ...agentDetail, id: 'agent-1' }),
    )

    renderWithProviders(<AgentsPage />)

    await waitFor(() => expect(requests).toEqual(['project-1']))
  })

  it('memoises cards so a readiness update renders only the changed row', async () => {
    const agents = Array.from({ length: 49 }, (_, index) => ({
      ...agentSummary,
      id: `memo-agent-${index + 1}`,
      name: `Memo agent ${index + 1}`,
    }))
    const renders: string[] = []
    const noopAgent = () => undefined
    const trackRender = (agentId: string) => renders.push(agentId)

    function Roster() {
      const [readiness, setReadiness] = useState<ProjectReadinessDto | undefined>()
      return (
        <>
          <button onClick={() => setReadiness(readyReadiness)}>Load readiness</button>
          {agents.map((agent, index) => (
            <AgentCard
              key={agent.id}
              agent={agent}
              isSelected={index === 0}
              projectId={index === 0 ? 'project-1' : undefined}
              readiness={index === 0 ? readiness : undefined}
              onSelect={noopAgent}
              onOpenTerminal={noopAgent}
              onEditSettings={noopAgent}
              onRendered={trackRender}
            />
          ))}
        </>
      )
    }

    renderWithProviders(<Roster />)
    renders.length = 0
    await userEvent.click(screen.getByRole('button', { name: 'Load readiness' }))

    await waitFor(() => expect(renders).toEqual(['memo-agent-1']))
  })
})
