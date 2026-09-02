import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentPicker } from './AgentPicker'
import { CardModal } from './CardModal'

vi.mock('./SessionTerminal', () => ({
  SessionTerminal: () => <div data-testid="session-terminal" />,
}))

const card: CardDto = {
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
  title: 'Implement terminal',
  description: 'Wire xterm to the session stream',
  importance: 'Normal', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Someday', rank: 10,
  labels: ['ui'],
  status: 'Backlog',
  concurrencyToken: 'token-1',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  startedAt: null,
  completedAt: null,
  terminalReason: null,
  sessions: [],
  revisionCount: 0,
  archivedAt: null,
  archivedReason: null,
  archivedBy: null,
}

function agentDefinitionsHandler() {
  return http.get('/api/agents/definitions', () =>
    HttpResponse.json({
      defaultDefinition: 'claude',
      definitions: [
        { name: 'claude', kind: 'ClaudeCode', isDefault: true },
        { name: 'raw', kind: 'Raw', isDefault: false },
      ],
    }),
  )
}

function getAgentInput() {
  return screen
    .getAllByLabelText('Agent')
    .find((element): element is HTMLInputElement =>
      element instanceof HTMLInputElement && element.getAttribute('type') !== 'hidden',
    ) as HTMLInputElement
}

describe('AgentPicker', () => {
  it('renders configured registry options and selects the default', async () => {
    const onChange = vi.fn()
    server.use(agentDefinitionsHandler())

    renderWithProviders(<AgentPicker value={null} onChange={onChange} />)

    await waitFor(() => expect(onChange).toHaveBeenCalledWith('claude'))
    await userEvent.click(getAgentInput())
    expect(await screen.findByText('claude (ClaudeCode, default)')).toBeInTheDocument()
    expect(screen.getByText('raw (Raw)')).toBeInTheDocument()
  })
})

function discussionHandler(cardId = 'card-1') {
  return http.get(`/api/cards/${cardId}/discussion`, () => HttpResponse.json([]))
}

function cardHandler(value: CardDto = card) {
  return http.get('/api/cards/card-1', () => HttpResponse.json(value))
}

beforeEach(() => {
  server.use(cardHandler())
})

describe('CardModal', () => {
  it('fetches the full card only after the modal opens', async () => {
    const getSpy = vi.fn()
    server.use(agentDefinitionsHandler(), discussionHandler(), http.get('/api/cards/card-1', () => {
      getSpy()
      return HttpResponse.json(card)
    }))

    renderWithProviders(<CardModal boardId="board-1" card={card} opened onClose={() => undefined} />)

    await waitFor(() => expect(getSpy).toHaveBeenCalledTimes(1))
  })

  it('a card in Needs decision leads with its question and a Decide button', async () => {
    server.use(
      agentDefinitionsHandler(), discussionHandler(), cardHandler({ ...card, status: 'NeedsDecision' }),
      http.get('/api/attention', () => HttpResponse.json({ generatedAt: '2026-08-27T12:00:00Z', runnerConsulted: true, items: [{
        kind: 'CardNeedsDecision', severity: 'Critical', taskId: null, sessionId: null, agentId: null, messageId: null,
        cardId: 'card-1', boardId: 'board-1', title: 'CARD-0001 — Implement terminal', headline: 'Needs a decision', evidence: 'Should this use WebGL?', sinceUtc: '2026-08-27T10:00:00Z', subtreeCostUsd: null, actions: ['OpenCard'],
      }] })),
      http.get('/api/cards/card-1/revisions', () => HttpResponse.json([])),
    )
    renderWithProviders(<CardModal boardId="board-1" card={{ ...card, status: 'NeedsDecision' }} columns={[{
      id: 'column-backlog', stateKey: 'backlog', name: 'Backlog', columnOrder: 0, cardStatus: 'Backlog', isActive: false, isTerminal: false, maxConcurrentSessions: null, cards: [],
    }]} opened onClose={() => undefined} />)

    await waitFor(() => expect(screen.getByTestId('waiting-on-decision')).toHaveTextContent('Should this use WebGL?'))
    expect(screen.getByRole('button', { name: 'Decide…' })).toBeInTheDocument()
  })

  it('falls back to the revision history when the feed has no row yet', async () => {
    server.use(
      agentDefinitionsHandler(), discussionHandler(), cardHandler({ ...card, status: 'NeedsDecision' }),
      http.get('/api/attention', () => HttpResponse.json({ generatedAt: '2026-08-27T12:00:00Z', runnerConsulted: true, items: [] })),
      http.get('/api/cards/card-1/revisions', () => HttpResponse.json([{
        id: 'revision-1', cardId: 'card-1', revisionNumber: 1, kind: 'Reopen', title: null, description: null, importance: null, urgency: null, dueAt: null, labels: null,
        fromColumnId: null, toColumnId: 'column-decision', fromStatus: 'Done', toStatus: 'NeedsDecision', reason: 'Pick a database.', editedBy: null,
        createdAt: '2026-08-27T10:00:00Z', terminalReason: null, completedAt: null,
      }])),
    )
    renderWithProviders(<CardModal boardId="board-1" card={{ ...card, status: 'NeedsDecision' }} opened onClose={() => undefined} />)

    await waitFor(() => expect(screen.getByTestId('waiting-on-decision')).toHaveTextContent('Pick a database.'))
  })

  it('posts spawn with the selected agent definition', async () => {
    const spawnSpy = vi.fn()
    server.use(
      agentDefinitionsHandler(),
      discussionHandler(),
      http.post('/api/cards/card-1/spawn', async ({ request }) => {
        spawnSpy(await request.json())
        return HttpResponse.json({ cardId: 'card-1', sessionId: 'session-1' }, { status: 202 })
      }),
    )

    renderWithProviders(
      <CardModal boardId="board-1" card={card} opened onClose={() => undefined} />,
    )

    await waitFor(() => expect(getAgentInput()).toHaveValue('claude (ClaudeCode, default)'))
    await userEvent.click(screen.getByRole('button', { name: 'Spawn' }))

    await waitFor(() => expect(spawnSpy).toHaveBeenCalledWith({
      definitionName: 'claude',
      cols: 120,
      rows: 30,
    }))
  })

  it('opens the edit dialog from the header actions, prefilled', async () => {
    server.use(agentDefinitionsHandler(), discussionHandler())
    renderWithProviders(
      <CardModal boardId="board-1" card={card} opened onClose={() => undefined} />,
    )

    expect(screen.queryByRole('heading', { name: 'Edit #1' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Edit card' }))

    expect(await screen.findByRole('heading', { name: 'Edit #1' })).toBeInTheDocument()
    expect(screen.getByLabelText('Title')).toHaveValue('Implement terminal')
    expect(screen.getByLabelText('Description')).toHaveValue('Wire xterm to the session stream')
  })

  it('does not fetch the history until the History tab is opened', async () => {
    const revisionsSpy = vi.fn()
    server.use(
      agentDefinitionsHandler(),
      discussionHandler(),
      http.get('/api/cards/card-1/revisions', () => {
        revisionsSpy()
        return HttpResponse.json([])
      }),
    )

    renderWithProviders(
      <CardModal boardId="board-1" card={{ ...card, revisionCount: 7 }} opened onClose={() => undefined} />,
    )

    // `keepMounted={false}` on the tabs is what buys this — no explicit lazy-loading code.
    const tab = await screen.findByRole('tab', { name: 'History (7)' })
    expect(revisionsSpy).not.toHaveBeenCalled()

    await userEvent.click(tab)
    await waitFor(() => expect(revisionsSpy).toHaveBeenCalled())
  })

  it('refuses to spawn on an archived card, and says why', async () => {
    server.use(agentDefinitionsHandler(), discussionHandler())
    renderWithProviders(
      <CardModal
        boardId="board-1"
        card={{ ...card, archivedAt: '2026-08-12T09:00:00Z', archivedReason: 'duplicate' }}
        opened
        onClose={() => undefined}
      />,
    )

    const spawn = await screen.findByRole('button', { name: 'Spawn' })
    expect(spawn).toBeDisabled()
    expect(spawn).toHaveAttribute('title', 'Unarchive this card before starting work on it')
    expect(screen.getByText('archived')).toBeInTheDocument()
  })

  it('renders the external tracker link when externalIssue is present', async () => {
    server.use(agentDefinitionsHandler(), discussionHandler())
    renderWithProviders(
      <CardModal
        boardId="board-1"
        card={{
          ...card,
          identifier: 'CARD-0176',
          externalIssue: {
            trackerKind: 'GitHubIssues',
            key: '#3',
            url: 'https://github.com/acme/app/issues/3',
          },
        }}
        opened
        onClose={() => undefined}
      />,
    )

    const link = await screen.findByRole('link', { name: /GH #3/ })
    expect(link).toHaveAttribute('href', 'https://github.com/acme/app/issues/3')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('renders no tracker link when the card is not linked', async () => {
    server.use(agentDefinitionsHandler(), discussionHandler())
    renderWithProviders(
      <CardModal boardId="board-1" card={card} opened onClose={() => undefined} />,
    )

    expect(screen.queryByRole('link', { name: /GH #/ })).not.toBeInTheDocument()
  })

  it('closes itself once the card is archived — it is about to leave the board payload', async () => {
    const onClose = vi.fn()
    server.use(
      agentDefinitionsHandler(),
      discussionHandler(),
      http.post('/api/cards/card-1/archive', () =>
        HttpResponse.json({ ...card, archivedAt: '2026-08-12T09:00:00Z' })),
    )

    renderWithProviders(
      <CardModal
        boardId="board-1"
        card={card}
        columns={[{
          id: 'column-done',
          stateKey: 'done',
          name: 'Done',
          columnOrder: 1,
          cardStatus: 'Done',
          isActive: false,
          isTerminal: true,
          maxConcurrentSessions: null,
          cards: [],
        }]}
        opened
        onClose={onClose}
      />,
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Move to' }))
    await userEvent.click(await screen.findByTestId('archive-card'))
    await userEvent.type(screen.getByLabelText(/^Reason/), 'duplicate of CARD-0042')
    await userEvent.click(screen.getByRole('button', { name: 'Archive' }))

    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('disables spawn while a session is stopping', async () => {
    const stoppingCard: CardDto = {
      ...card,
      sessions: [
        {
          id: 'session-1', definitionName: 'claude', agentKind: 'ClaudeCode', status: 'Stopping',
          cwd: 'D:/repo', createdAt: '2026-01-01T00:00:00Z', startedAt: '2026-01-01T00:00:00Z',
          lastSeenAt: '2026-01-01T00:00:01Z', endedAt: null, exitCode: null, failureReason: null,
        },
      ],
    }
    server.use(agentDefinitionsHandler(), discussionHandler(), cardHandler(stoppingCard))
    renderWithProviders(
      <CardModal
        boardId="board-1"
        card={stoppingCard}
        opened
        onClose={() => undefined}
      />,
    )

    await waitFor(() => expect(getAgentInput()).toHaveValue('claude (ClaudeCode, default)'))
    expect(screen.getByRole('button', { name: 'Spawn' })).toBeDisabled()
  })
})
