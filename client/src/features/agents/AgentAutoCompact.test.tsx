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
 * CARD-0082 S2 — per-agent auto-compact overrides on the settings modal. Empty / null is the
 * "use the installation default" state; a chosen value is submitted as the override.
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

describe('AgentSettingsModal auto-compact overrides', () => {
  it('starts empty so the agent uses the installation default', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json(detail)
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getByLabelText('Auto-compact idle minutes')).toHaveValue('')
    expect(screen.getByLabelText('Auto-compact context percent')).toHaveValue('')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.autoCompactEnabled).toBeNull()
    expect(submitted!.autoCompactIdleMinutes).toBeNull()
    expect(submitted!.autoCompactContextPercent).toBeNull()
  })

  it('submits chosen overrides with the update', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({
          ...detail,
          autoCompactEnabled: submitted.autoCompactEnabled ?? null,
          autoCompactIdleMinutes: submitted.autoCompactIdleMinutes ?? null,
          autoCompactContextPercent: submitted.autoCompactContextPercent ?? null,
        })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal
        agent={{ ...agent, autoCompactEnabled: false, autoCompactIdleMinutes: 60, autoCompactContextPercent: 80 }}
        opened
        onClose={() => {}}
        onDeleted={() => {}}
      />,
    )

    await screen.findByRole('button', { name: 'Save' })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.autoCompactEnabled).toBe(false)
    expect(submitted!.autoCompactIdleMinutes).toBe(60)
    expect(submitted!.autoCompactContextPercent).toBe(80)
  })
})
