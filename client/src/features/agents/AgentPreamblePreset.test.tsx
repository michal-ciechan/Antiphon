import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto } from '../../api/agents'
import { renderWithProviders, screen, userEvent } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from './AgentSettingsModal'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const agent: AgentSummaryDto = {
  id: 'agent-1', name: 'Frontend Claude', slug: 'frontend-claude', workingDirectory: 'D:/src/app',
  details: 'UI work', defaultWorkflowTemplateId: null, defaultWorkflowTemplateName: null,
  assignmentPolicy: 'AutoPick', status: 'Idle', persistentSessionId: null, currentCardId: null,
  boardId: null, boardName: null, queueLength: 0, createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z', liveSession: null, alwaysOn: false,
  remoteControlEnabled: false, supervision: null, systemPromptAppend: null, modelLevel: 'High', working: false,
}

describe('AgentSettingsModal channel preamble presets', () => {
  it('offers Telegram and Slack presets and loads the selected provider', async () => {
    server.use(
      http.get('/api/agents/bundles', () => HttpResponse.json([])),
      http.get('/api/agents/preamble-preset', ({ request }) => {
        expect(new URL(request.url).searchParams.get('provider')).toBe('slack')
        return HttpResponse.json({ template: 'Slack preset template' })
      }),
      http.get('/api/agents/:id', () => HttpResponse.json({ ...agent, queue: [] } satisfies AgentDetailDto)),
      http.get('/api/boards', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
      http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    )

    renderWithProviders(<AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />)

    expect(await screen.findByRole('button', { name: 'Use Telegram preset' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Use Slack preset' }))
    expect(await screen.findByDisplayValue('Slack preset template')).toBeInTheDocument()
  })
})
