import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, UpdateAgentRequest } from '../../api/agents'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from './AgentSettingsModal'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

/**
 * CARD-0186 S1 — AlwaysOn no longer disables or silently remaps the herdr session-backend control.
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
  sessionBackend: 'Herdr',
}

const detail: AgentDetailDto = { ...agent, queue: [] }

function handlers() {
  return [
    http.get('/api/agents', () => HttpResponse.json([agent])),
    http.get('/api/agents/bundles', () => HttpResponse.json([])),
    http.get('/api/agents/:id', () => HttpResponse.json(detail)),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
  ]
}

describe('AgentSettingsModal session backend', () => {
  it('does not disable or remap herdr when AlwaysOn is toggled on', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, alwaysOn: true })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    const herdr = await screen.findByRole('radio', { name: 'Herdr' })
    expect(herdr).toBeChecked()
    expect(herdr).toBeEnabled()

    await userEvent.click(screen.getByRole('switch', { name: /Always on/i }))

    expect(herdr).toBeChecked()
    expect(herdr).toBeEnabled()
    expect(
      screen.getByText(/an always-on agent is resumed into a new pane by supervision/i),
    ).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.sessionBackend).toBe('Herdr')
    expect(submitted!.alwaysOn).toBe(true)
  })
})
