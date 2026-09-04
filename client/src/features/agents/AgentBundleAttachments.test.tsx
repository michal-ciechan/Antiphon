import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type {
  AgentDetailDto,
  AgentSummaryDto,
  InstructionBundleDto,
  UpdateAgentRequest,
} from '../../api/agents'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from './AgentSettingsModal'
import { AgentsPage } from './AgentsPage'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

/**
 * CARD-0058 slice 6 — attaching a bundle to one agent, and the badge that says a running session is
 * carrying older instructions.
 *
 * The settings modal stays informational (nothing is typed into a live composer). The list/detail
 * badge offers "Refresh now" for Auto/Relaunch/Notify, which hits POST /refresh-policy with force.
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

const catalog: InstructionBundleDto[] = [
  {
    key: 'board-api',
    version: '1a2b3c4d',
    stamp: 'board-api v1a2b3c4d',
    summary: 'Working the Antiphon board.',
    chars: 2400,
  },
  {
    key: 'delegate-basics',
    version: '5e1c2c6a',
    stamp: 'delegate-basics v5e1c2c6a',
    summary: 'You are running as an Antiphon delegate.',
    chars: 1800,
  },
]

function handlers(
  summary: AgentSummaryDto[] = [agent],
  detailDto: AgentDetailDto = detail,
  bundles: InstructionBundleDto[] = catalog,
) {
  return [
    http.get('/api/agents', () => HttpResponse.json(summary)),
    // Ahead of the ':id' pattern, which is loose in MSW and would otherwise answer this with an
    // agent detail object. (The server's own route is guid-constrained, so it cannot collide there.)
    http.get('/api/agents/bundles', () => HttpResponse.json(bundles)),
    http.get('/api/agents/:id', () => HttpResponse.json(detailDto)),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
  ]
}

/** The MultiSelect's search field. Its pill remove buttons are aria-hidden, so selection changes
 *  are made by toggling options in the dropdown rather than by clicking a pill. */
const bundlePicker = () => screen.findByRole('textbox', { name: 'Attached bundles' })

describe('AgentSettingsModal bundle attachments', () => {
  it('offers the catalog with each bundle summarised, not just keyed', async () => {
    server.use(...handlers())

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    await userEvent.click(await bundlePicker())
    expect(await screen.findByRole('option', { name: /board-api/ })).toBeInTheDocument()
    expect(screen.getByText('Working the Antiphon board.')).toBeInTheDocument()
  })

  it('shows the bundles already attached to this agent', async () => {
    server.use(...handlers([agent], { ...detail, attachedBundleKeys: ['board-api'] }))

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    // Asserted on the picker's own state rather than on the rendered pill: the dropdown is kept
    // mounted while closed, so the key appears in the DOM more than once and matching it by text
    // would pass whether or not the agent actually carries it.
    await userEvent.click(await bundlePicker())
    expect(await screen.findByRole('option', { name: /board-api/ })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    expect(screen.getByRole('option', { name: /delegate-basics/ })).toHaveAttribute(
      'aria-selected',
      'false',
    )
  })

  it('submits the attachment with the rest of the settings', async () => {
    // One save, not two: attachments ride the same PATCH as the style and the preamble, so a modal
    // that half-succeeded could never leave the agent in a state nobody chose.
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers(),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, attachedBundleKeys: submitted.bundleKeys ?? [] })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    await userEvent.click(await bundlePicker())
    await userEvent.click(await screen.findByRole('option', { name: /board-api/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.bundleKeys).toEqual(['board-api'])
  })

  it('sends an empty list rather than nothing when every bundle is removed', async () => {
    // null on the request means "leave unchanged", so detaching the last bundle has to arrive as an
    // explicit empty array or the operator's removal would be silently ignored.
    let submitted: UpdateAgentRequest | null = null
    server.use(
      ...handlers([agent], { ...detail, attachedBundleKeys: ['board-api'] }),
      http.patch('/api/agents/:id', async ({ request }) => {
        submitted = (await request.json()) as UpdateAgentRequest
        return HttpResponse.json({ ...detail, attachedBundleKeys: [] })
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )
    // Deselected by toggling the option off — the pill's own remove control is aria-hidden, which
    // is Mantine's doing and not something a test should reach around.
    await userEvent.click(await bundlePicker())
    await userEvent.click(await screen.findByRole('option', { name: /board-api/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted!.bundleKeys).toEqual([])
  })

  it('warns that the running session restarts with the new instructions, and offers no way to force it', async () => {
    server.use(
      ...handlers([agent], {
        ...detail,
        bundlesOutOfDate: true,
        composedBundles: ['board-api v1a2b3c4d'],
        attachedBundleKeys: ['board-api'],
      }),
    )

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    expect(await screen.findByText('Restarts with updated instructions')).toBeInTheDocument()
    expect(screen.getByText(/nothing is typed into a live session/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /restart|apply now|reload/i })).not.toBeInTheDocument()
  })

  it('says nothing when the running session is carrying exactly what the repo says', async () => {
    server.use(...handlers([agent], { ...detail, bundlesOutOfDate: false }))

    renderWithProviders(
      <AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />,
    )

    await bundlePicker()
    expect(screen.queryByText('Restarts with updated instructions')).not.toBeInTheDocument()
  })
})

describe('bundle drift badge', () => {
  it('marks the drifted agent and only that one', async () => {
    // Both cases on one page, deliberately: the badge's job is to distinguish, and the field is
    // OPTIONAL — a server that predates it omits it entirely, which must read as "no drift" rather
    // than badging every agent in the list.
    server.use(
      ...handlers([
        { ...agent, id: 'agent-1', name: 'Drifted Claude', bundlesOutOfDate: true },
        { ...agent, id: 'agent-2', name: 'Current Claude' },
      ]),
    )

    renderWithProviders(<AgentsPage />)

    await screen.findByText('Current Claude')
    expect(screen.getAllByText('bundles')).toHaveLength(1)
  })

  it('marks file-only drift from policyDrift even when bundlesOutOfDate is false', async () => {
    server.use(
      ...handlers([
        {
          ...agent,
          id: 'agent-1',
          name: 'File drifted',
          bundlesOutOfDate: false,
          policyDrift: { bundles: [], files: ['AGENTS.md'] },
        },
        { ...agent, id: 'agent-2', name: 'Current Claude' },
      ]),
    )

    renderWithProviders(<AgentsPage />)

    await screen.findByText('Current Claude')
    expect(screen.getAllByText('bundles')).toHaveLength(1)
  })

  it('offers Refresh now and says it refreshes at the next idle window', async () => {
    let refreshBody: { force?: boolean } | null = null
    server.use(
      ...handlers([
        {
          ...agent,
          id: 'agent-1',
          name: 'Drifted Claude',
          bundlesOutOfDate: true,
          policyDrift: { bundles: ['orchestrator'], files: ['AGENTS.md'], mode: 'Auto' },
        },
      ]),
      http.post('/api/agents/:id/refresh-policy', async ({ request }) => {
        refreshBody = (await request.json()) as { force?: boolean }
        return HttpResponse.json({
          refreshed: true,
          notified: false,
          agent: { ...detail, id: 'agent-1', name: 'Drifted Claude', bundlesOutOfDate: false },
        })
      }),
    )

    renderWithProviders(<AgentsPage />)

    const buttons = await screen.findAllByRole('button', { name: 'Refresh now' })
    expect(buttons.length).toBeGreaterThanOrEqual(1)
    await userEvent.click(buttons[0])
    await waitFor(() => expect(refreshBody).not.toBeNull())
    expect(refreshBody!.force).toBe(true)
  })

  it('does not offer Refresh now when policy refresh is Off', async () => {
    server.use(
      ...handlers([
        {
          ...agent,
          id: 'agent-1',
          name: 'Drifted Claude',
          bundlesOutOfDate: true,
          policyDrift: { bundles: ['orchestrator'], files: [], mode: 'Off' },
        },
      ]),
    )

    renderWithProviders(<AgentsPage />)

    await screen.findByText('Drifted Claude')
    expect(screen.getByText('bundles')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Refresh now' })).not.toBeInTheDocument()
  })
})
