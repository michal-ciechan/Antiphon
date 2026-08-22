import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, UpdateAgentRequest } from '../../api/agents'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentsPage } from './AgentsPage'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

/**
 * CARD-0139 T2 — Kind is on the client agent types and round-trips through a fixture + render.
 * There is no raw Kind selector (D7): the profile picker already labels every option with its kind.
 */
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
  kind: 'Codex',
}

const detail: AgentDetailDto = { ...agent, queue: [] }

describe('agent kind on the wire', () => {
  it('an omitted Kind on UpdateAgentRequest means unchanged', () => {
    const request: UpdateAgentRequest = {
      name: 'A',
      workingDirectory: 'C:\\tmp',
      assignmentPolicy: 'AutoPick',
    }
    expect(request.kind).toBeUndefined()
  })

  it('renders an agent whose summary carries kind', async () => {
    server.use(
      http.get('/api/agents', () => HttpResponse.json([agent])),
      http.get('/api/agents/bundles', () => HttpResponse.json([])),
      http.get('/api/agents/:id', () => HttpResponse.json(detail)),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    )

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('Frontend Claude')).toBeInTheDocument()
  })
})
