import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, UpdateAgentRequest } from '../../api/agents'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from './AgentSettingsModal'
import { AgentsPage } from './AgentsPage'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

/**
 * CARD-0060 slice 5 — the style is visible, changeable, and submitted; the bundles it composes are
 * shown read-only. The chip's most important behaviour is the one it does NOT have: `Normal` shows
 * nothing, because Normal composes no instruction and a chip for it would appear on every agent.
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
}

const detail: AgentDetailDto = { ...agent, queue: [] }

function handlers(summary: AgentSummaryDto[] = [agent], detailDto: AgentDetailDto = detail) {
  return [
    http.get('/api/agents', () => HttpResponse.json(summary)),
    // BEFORE the ':id' pattern, which would otherwise swallow it (CARD-0058 slice 6).
    http.get('/api/agents/bundles', () => HttpResponse.json([])),
    http.get('/api/agents/:id', () => HttpResponse.json(detailDto)),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
  ]
}

describe('AgentSettingsModal reply style', () => {
  it('shows the agent’s current style selected', async () => {
    server.use(...handlers())

    renderWithProviders(
      <AgentSettingsModal
        agent={{ ...agent, replyStyle: 'Caveman' }}
        opened
        onClose={() => {}}
        onDeleted={() => {}}
      />,
    )

    await waitFor(() =>
      expect(screen.getByRole('radio', { name: 'Caveman' })).toBeChecked(),
    )
  })

  it('defaults to Normal when the server sent no style at all', async () => {
    // An older server response omits the field. Falling back to Normal keeps the modal honest about
    // what the agent will actually launch with, which is nothing.
    server.use(...handlers())

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    await waitFor(() => expect(screen.getByRole('radio', { name: 'Normal' })).toBeChecked())
  })

  it('submits the chosen style with the update', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, replyStyle: submitted.replyStyle ?? 'Normal' })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    await screen.findByRole('radio', { name: 'Terse' })
    await userEvent.click(screen.getByRole('radio', { name: 'Terse' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.replyStyle).toBe('Terse')
  })

  it('lists the bundles the next launch will carry', async () => {
    server.use(
      ...handlers([agent], { ...detail, replyStyle: 'Caveman', composedBundles: ['style-caveman v1a2b3c4d'] }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    expect(await screen.findByText('style-caveman v1a2b3c4d')).toBeInTheDocument()
  })

  it('says so plainly when the agent carries no bundles', async () => {
    // The overwhelmingly common case, and the one an empty list would silently look like a bug.
    server.use(...handlers([agent], { ...detail, composedBundles: [] }))

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    expect(
      await screen.findByText(/this agent launches with its own system prompt alone/i),
    ).toBeInTheDocument()
  })
})

describe('reply style chip', () => {
  it('renders the style on the agent card', async () => {
    server.use(...handlers([{ ...agent, replyStyle: 'Explanatory' }]))

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText('explanatory')).toBeInTheDocument()
  })

  it('renders nothing at all for Normal', async () => {
    server.use(...handlers([{ ...agent, replyStyle: 'Normal' }]))

    renderWithProviders(<AgentsPage />)

    await screen.findByText('Frontend Claude')
    expect(screen.queryByText('normal')).not.toBeInTheDocument()
  })
})
