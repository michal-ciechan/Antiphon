import { HttpResponse, http } from 'msw'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AGENT_REPLY_STYLE_OPTIONS } from '../../api/agents'
import type { AgentDetailDto, AgentSummaryDto, UpdateAgentRequest } from '../../api/agents'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from './AgentSettingsModal'
import { AgentsPage } from './AgentsPage'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

beforeAll(() => {
  Element.prototype.scrollIntoView = vi.fn()
})

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

  it.each(['Terse', 'Phone'] as const)('submits and reloads %s with the update', async (style) => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, replyStyle: submitted.replyStyle ?? 'Normal' })
      }),
    )

    const view = renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    await screen.findByRole('radio', { name: style })
    await userEvent.click(screen.getByRole('radio', { name: style }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.replyStyle).toBe(style)
    view.unmount()
    server.use(...handlers([{ ...agent, replyStyle: style }], { ...detail, replyStyle: style }))
    renderWithProviders(<AgentSettingsModal agent={{ ...agent, replyStyle: style }} opened onClose={() => {}} onDeleted={() => {}} />)
    await waitFor(() => expect(screen.getByRole('radio', { name: style })).toBeChecked())
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
  it.each(['Explanatory', 'Phone'] as const)('renders %s on the agent card', async (style) => {
    server.use(...handlers([{ ...agent, replyStyle: style }]))

    renderWithProviders(<AgentsPage />)

    expect(await screen.findByText(style.toLowerCase())).toBeInTheDocument()
  })

  it('renders nothing at all for Normal', async () => {
    server.use(...handlers([{ ...agent, replyStyle: 'Normal' }]))

    renderWithProviders(<AgentsPage />)

    await screen.findByText('Frontend Claude')
    expect(screen.queryByText('normal')).not.toBeInTheDocument()
  })
})

describe('AgentSettingsModal policy refresh mode', () => {
  it('defaults to Auto when the server sent no mode', async () => {
    server.use(...handlers())

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    expect(await screen.findByRole('textbox', { name: /policy refresh/i })).toHaveValue(
      'Auto (relaunch when idle)',
    )
  })

  it('submits the chosen mode with the update', async () => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({
          ...detail,
          policyDrift: { bundles: [], files: [], mode: submitted.policyRefreshMode ?? 'Auto' },
        })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    await userEvent.click(await screen.findByRole('textbox', { name: /policy refresh/i }))
    await userEvent.click(await screen.findByRole('option', { name: 'Notify only' }))
    await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.policyRefreshMode).toBe('Notify')
  })
})


describe('Phone audience selection', () => {
  it('describes the channel audience explicitly', () => {
    expect(AGENT_REPLY_STYLE_OPTIONS.find(s => s.value === 'Phone')?.description).toBe(
      'Minimal Telegram/Slack replies. Short bullets, about 5–7 words; no tables. Delegate reports keep their own contracts.',
    )
  })

  it.each(['Phone', 'Brief'] as const)('channel preamble buttons preserve %s', async (style) => {
    let submitted: UpdateAgentRequest | null = null
    server.use(
      http.get('/api/agents/preamble-preset', ({ request }) => HttpResponse.json({ template: `Channel ${new URL(request.url).searchParams.get('provider')}` })),
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = await request.json() as UpdateAgentRequest
        return HttpResponse.json({ ...detail, replyStyle: style })
      }),
    )
    renderWithProviders(<AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />)
    await userEvent.click(await screen.findByRole('radio', { name: style }))
    for (const channel of ['Telegram', 'Slack']) {
      await userEvent.click(screen.getByRole('button', { name: `Use ${channel} preset` }))
      await waitFor(() => expect(screen.getByRole('textbox', { name: 'System prompt (appended)' })).toHaveValue(`Channel ${channel.toLowerCase()}`))
      expect(screen.getByRole('radio', { name: style })).toBeChecked()
    }
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(submitted?.replyStyle).toBe(style))
  })
})
