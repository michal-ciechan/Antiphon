import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto } from '../../api/agents'
import type { AgentSessionSummaryDto, BoardSummaryDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentsPage } from './AgentsPage'
import { notifications } from '@mantine/notifications'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

const herdrAgent: AgentSummaryDto = {
  id: 'agent-herdr',
  name: 'PM-DropCopy-Grok',
  slug: 'pm-dropcopy-grok',
  workingDirectory: 'D:/src/maven.dropcopy',
  details: '',
  defaultWorkflowTemplateId: null,
  defaultWorkflowTemplateName: null,
  assignmentPolicy: 'AutoPick',
  status: 'Idle',
  persistentSessionId: null,
  currentCardId: null,
  boardId: 'board-1',
  boardName: 'Antiphon',
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
}

const ptyAgent: AgentSummaryDto = {
  ...herdrAgent,
  id: 'agent-pty',
  name: 'Frontend Claude',
  slug: 'frontend-claude',
  sessionBackend: 'PtyHost',
}

const liveAttached: AgentSessionSummaryDto = {
  id: 'session-attached',
  definitionName: 'grok',
  agentKind: 'Grok',
  status: 'Running',
  cwd: 'D:/src/maven.dropcopy',
  createdAt: '2026-05-18T09:00:00Z',
  startedAt: '2026-05-18T09:00:00Z',
  lastSeenAt: '2026-05-18T09:00:00Z',
  endedAt: null,
  exitCode: null,
  failureReason: null,
  herdrOrigin: 'attached',
  herdrAgentStatus: 'idle',
}

const boardSummary: BoardSummaryDto = {
  id: 'board-1',
  projectId: 'project-1',
  projectName: 'Project One',
  name: 'Antiphon',
  description: '',
  trackerKind: 'Internal',
  maxConcurrentSessions: 1,
  cardCount: 0,
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
}

function handlers(summary: AgentSummaryDto, detail: AgentDetailDto = { ...summary, queue: [] }) {
  return [
    http.get('/api/agents', () => HttpResponse.json([summary])),
    http.get('/api/agents/bundles', () => HttpResponse.json([])),
    http.get('/api/agents/:id', () => HttpResponse.json(detail)),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    http.get('/api/boards', () => HttpResponse.json([boardSummary])),
    http.get('/api/projects/readiness', () => HttpResponse.json([])),
  ]
}

describe('CARD-0213 attach Herdr pane', () => {
  it('shows Attach… only for Herdr agents with no live session', async () => {
    server.use(...handlers(herdrAgent))
    renderWithProviders(<AgentsPage />)
    expect(await screen.findByRole('button', { name: 'Attach…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start' })).toBeInTheDocument()
  })

  it('does not show Attach… for a PtyHost agent', async () => {
    server.use(...handlers(ptyAgent))
    renderWithProviders(<AgentsPage />)
    expect(await screen.findByText('Frontend Claude')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Attach…' })).not.toBeInTheDocument()
  })

  it('labels Stop as Detach when the live session is attached-origin', async () => {
    const attached: AgentSummaryDto = {
      ...herdrAgent,
      status: 'Running',
      persistentSessionId: liveAttached.id,
      liveSession: liveAttached,
    }
    server.use(...handlers(attached, { ...attached, queue: [] }))
    renderWithProviders(<AgentsPage />)
    expect(await screen.findByText('PM-DropCopy-Grok')).toBeInTheDocument()
    expect(await screen.findByTestId('agent-session-stop')).toHaveTextContent('Detach')
    expect(screen.getAllByTestId('herdr-attached-chip')[0]).toHaveTextContent('attached')
  })

  it('renders the 409 message when attach is refused', async () => {
    server.use(
      ...handlers(herdrAgent),
      http.post('/api/agents/:id/attach-herdr', () =>
        HttpResponse.json(
          {
            type: 'https://httpstatuses.com/409',
            title: 'Conflict',
            status: 409,
            detail: 'pane w2:p3 is bound to session aaaa (attached)',
            code: 'herdr_pane_bound',
          },
          { status: 409 },
        ),
      ),
    )

    renderWithProviders(<AgentsPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Attach…' }))
    const input = await screen.findByTestId('attach-herdr-pane-id')
    await userEvent.type(input, 'w2:p3')
    await userEvent.click(screen.getByRole('button', { name: 'Attach' }))

    await waitFor(() => {
      expect(notifications.show).toHaveBeenCalledWith(
        expect.objectContaining({
          color: 'red',
          message: 'pane w2:p3 is bound to session aaaa (attached)',
        }),
      )
    })
  })
})
