import { HttpResponse, http } from 'msw'
import { notifications } from '@mantine/notifications'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto } from '../../api/agents'
import type { BoardDetailDto, BoardSummaryDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentsPage } from './AgentsPage'

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
}

const agentDetail: AgentDetailDto = {
  ...agentSummary,
  queue: [],
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
        },
      ],
    },
  ],
}

function agentHandlers(summary: AgentSummaryDto[] = [agentSummary], detail: AgentDetailDto = agentDetail) {
  return [
    http.get('/api/agents', () => HttpResponse.json(summary)),
    http.get('/api/agents/:id', () => HttpResponse.json(detail)),
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
  it('renders agent roster with status and queue length', async () => {
    server.use(...agentHandlers())

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('Frontend Claude')).toBeInTheDocument()
    expect(screen.getAllByText('Idle')[0]).toBeInTheDocument()
    expect(screen.getByText('2 queued')).toBeInTheDocument()
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

    await userEvent.click(await screen.findByRole('button', { name: 'Settings Frontend Claude' }))

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

    await userEvent.click(await screen.findByRole('button', { name: 'Settings Frontend Claude' }))
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

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(
        expect.objectContaining({ color: 'red', message: 'Agent name is required.' }),
      ),
    )
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
        return HttpResponse.json({ ...queuedDetail, status: 'Working', persistentSessionId: 'session-1' })
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
        return HttpResponse.json({ ...queuedDetail, status: 'Working', persistentSessionId: 'session-1' })
      }),
    )

    renderWithProviders(<AgentsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Start' }))

    await waitFor(() => expect(startSpy).toHaveBeenCalledWith({ remoteControl: true }))
    // Once running, the control flips to Stop.
    expect(await screen.findByRole('button', { name: 'Stop' })).toBeInTheDocument()
  })
})
