import { HttpResponse, http } from 'msw'
import { fireEvent } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto, UpdateAgentRequest } from '../../api/agents'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from './AgentSettingsModal'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

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
  launchEnv: { ANTHROPIC_API_KEY: '{{key:anthropic-default}}' },
}

const detail: AgentDetailDto = { ...agent, queue: [] }

describe('AgentSettingsModal launch environment', () => {
  it('shows the placeholder guidance and submits parsed KEY=value entries', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      // BEFORE ':id', which otherwise treats "bundles" as an agent id.
      http.get('/api/agents/bundles', () => HttpResponse.json([])),
      http.get('/api/agents/:id', () => HttpResponse.json(detail)),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, launchEnv: submitted.launchEnv })
      }),
    )

    renderWithProviders(<AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />)

    const environment = await screen.findByLabelText('Launch environment (KEY=value per line)')
    expect(environment).toHaveValue('ANTHROPIC_API_KEY={{key:anthropic-default}}')
    expect(screen.getByText(/project keys override global/i)).toBeInTheDocument()
    // user-event interprets braces as keyboard descriptors; a browser paste must preserve this
    // placeholder syntax literally.
    fireEvent.change(environment, {
      target: { value: 'OPENAI_API_KEY={{key:openai-project}}\nLOG_LEVEL=debug' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.launchEnv).toEqual({
      OPENAI_API_KEY: '{{key:openai-project}}',
      LOG_LEVEL: 'debug',
    })
  })
})
