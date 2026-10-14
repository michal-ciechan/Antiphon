import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardDto } from '../../api/boards'
import { fireEvent } from '@testing-library/react'
import { act, renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentPicker } from './AgentPicker'
import { CardModal } from './CardModal'
import { loadCardEditModal } from './cardEditModalLoader'

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
  server.use(
    cardHandler(),
    http.get('/api/cards/:id/relationships', () => HttpResponse.json({ blockedBy: [], blocks: [] })),
    http.get('/api/schedules', () => HttpResponse.json({ schedules: [] })),
  )
})

describe('CardModal', () => {
  it('renders the Blocked-by panel in the sidebar from the relationships query', async () => {
    const getRelationships = vi.fn()
    server.use(agentDefinitionsHandler(), discussionHandler(), http.get('/api/cards/card-1/relationships', () => {
      getRelationships()
      return HttpResponse.json({ blockedBy: [{
        id: 'relationship-1', kind: 'BlockedBy', cardId: 'card-1', relatedCardId: 'blocker-49',
        relatedIdentifier: 'CARD-0049', relatedTitle: 'Ship the prerequisite', relatedBoardId: 'board-1',
        relatedStatus: 'InProgress', relatedArchivedAt: null, resolved: false, reason: null,
        createdBy: null, createdAt: '2026-10-14T00:00:00Z',
      }], blocks: [] })
    }))
    renderWithProviders(<CardModal boardId="board-1" card={card} opened onClose={() => undefined} />)
    const panel = await within(screen.getByTestId('card-detail-sidebar')).findByTestId('card-blockers')
    expect(await within(panel).findByText('CARD-0049')).toBeInTheDocument()
    expect(getRelationships).toHaveBeenCalledTimes(1)
  })

  it('creates with Inherit and stores private notes separately in the modal entry point', async () => {
    const post = vi.fn()
    server.use(http.post('/api/boards/board-1/cards', async ({ request }) => { post(await request.json()); return HttpResponse.json(card) }))
    renderWithProviders(<CardModal boardId="board-1" card={null} opened onClose={() => undefined} />)
    expect(screen.getByRole('textbox', { name: 'Card-file visibility' })).toHaveValue('Inherit')
    // Pasted, not typed: per-keystroke typing cost 2.9 s alone and hit the 20 s hang detector under
    // load (CARD-1256). Nothing here depends on intermediate keystrokes; the request body is asserted.
    const user = userEvent.setup()
    await user.click(screen.getByLabelText('Title'))
    await user.paste('Public title')
    await user.click(screen.getByLabelText('Description'))
    await user.paste('Public description')
    await user.click(screen.getByLabelText('Private notes (kept in Antiphon; excluded from card files)'))
    await user.paste('  C408_PRIVATE  ')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(post).toHaveBeenCalledWith(expect.objectContaining({ description: 'Public description', privateNotes: '  C408_PRIVATE  ', cardFileVisibility: 'Inherit' })))
  })

  it('shows the stored default task platform', async () => {
    server.use(agentDefinitionsHandler(), discussionHandler(), cardHandler({ ...card, requiredPlatform: 'Linux' }))
    renderWithProviders(<CardModal boardId="board-1" card={{ ...card, requiredPlatform: 'Linux' }} opened onClose={() => undefined} />)
    expect(await screen.findByTestId('card-default-platform')).toHaveTextContent('Default task platform: Linux')
  })

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
    // The dialog is a lazy chunk, and React.lazy suspends on its first render even with the module
    // cached; its retry was a time-sliced render costing seconds of CPU in jsdom, which a findBy*
    // window lost under load (CARD-1256). Resolve the very promise the lazy component uses, then
    // click inside one act: React's retry is a microtask behind the click's render, lands in act's
    // queue, and act renders the dialog to completion before returning. No polling window remains.
    await loadCardEditModal()
    server.use(agentDefinitionsHandler(), discussionHandler())
    renderWithProviders(
      <CardModal boardId="board-1" card={card} opened onClose={() => undefined} />,
    )

    expect(screen.queryByRole('heading', { name: 'Edit #1' })).not.toBeInTheDocument()
    const edit = screen.getByRole('button', { name: 'Edit card' })
    await act(async () => {
      fireEvent.click(edit)
    })

    expect(screen.getByRole('heading', { name: 'Edit #1' })).toBeInTheDocument()
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

  it('shows the review chip and raised-by line on an unrated import', async () => {
    const imported: CardDto = {
      ...card,
      identifier: 'CARD-0325',
      importanceProvenance: 'Auto',
      externalIssue: {
        trackerKind: 'GitHubIssues',
        key: '#30',
        url: 'https://github.com/acme/app/issues/30',
        author: 'bob',
        authorIsOperator: false,
        needsHumanReview: true,
      },
    }
    server.use(agentDefinitionsHandler(), discussionHandler(), cardHandler(imported))
    renderWithProviders(
      <CardModal boardId="board-1" card={imported} opened onClose={() => undefined} />,
    )

    expect(await screen.findByRole('link', { name: /GH #30/ })).toBeInTheDocument()
    expect(screen.getByText('review')).toBeInTheDocument()
    expect(screen.getByText('raised by bob')).toBeInTheDocument()
    expect(screen.getAllByText('auto').length).toBeGreaterThan(0)
  })

  it('prints rated by human under importance once provenance is Human', async () => {
    const rated: CardDto = {
      ...card,
      importance: 'High',
      importanceProvenance: 'Human',
      externalIssue: {
        trackerKind: 'GitHubIssues',
        key: '#30',
        url: 'https://github.com/acme/app/issues/30',
        author: 'bob',
        authorIsOperator: false,
        needsHumanReview: false,
      },
    }
    server.use(agentDefinitionsHandler(), discussionHandler(), cardHandler(rated))
    renderWithProviders(
      <CardModal boardId="board-1" card={rated} opened onClose={() => undefined} />,
    )

    expect(await screen.findByText('rated by human')).toBeInTheDocument()
    expect(screen.queryByText('review')).not.toBeInTheDocument()
  })

  it('sends null importance until the create-form select is touched', async () => {
    const createSpy = vi.fn()
    server.use(
      http.post('/api/boards/board-1/cards', async ({ request }) => {
        createSpy(await request.json())
        return HttpResponse.json(card, { status: 201 })
      }),
    )
    renderWithProviders(
      <CardModal boardId="board-1" card={null} opened onClose={() => undefined} />,
    )

    await userEvent.type(screen.getByLabelText('Title'), 'A new card')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(createSpy).toHaveBeenCalled())
    expect(createSpy.mock.calls[0][0]).toMatchObject({
      title: 'A new card',
      importance: null,
    })
  })

  it('sends the chosen importance after the create-form select is touched', async () => {
    const createSpy = vi.fn()
    server.use(
      http.post('/api/boards/board-1/cards', async ({ request }) => {
        createSpy(await request.json())
        return HttpResponse.json(card, { status: 201 })
      }),
    )
    renderWithProviders(
      <CardModal boardId="board-1" card={null} opened onClose={() => undefined} />,
    )

    await userEvent.type(screen.getByLabelText('Title'), 'A rated card')
    await userEvent.click(screen.getByPlaceholderText('Normal (auto)'))
    await userEvent.click(await screen.findByRole('option', { name: 'High' }))
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(createSpy).toHaveBeenCalled())
    expect(createSpy.mock.calls[0][0]).toMatchObject({
      title: 'A rated card',
      importance: 'High',
    })
  })
})
