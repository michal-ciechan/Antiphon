import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
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
  priority: 2,
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

describe('CardModal', () => {
  // CARD-0175: an imported card's identifier is CARD-nnnn now, so the GitHub number only survives
  // on the ref. Without this badge the tracker key is invisible and `#12` on the board would be
  // indistinguishable from GitHub's `#12` — the collision the card exists to remove.
  it('links the tracker issue when the card is linked, and shows nothing when it is not', async () => {
    server.use(agentDefinitionsHandler(), discussionHandler())

    const { unmount } = renderWithProviders(
      <CardModal
        boardId="board-1"
        card={{
          ...card,
          externalIssue: { trackerKind: 'GitHubIssues', key: '#3', url: 'https://github.test/acme/app/issues/3' },
        }}
        opened
        onClose={() => undefined}
      />,
    )

    const link = await screen.findByTestId('card-external-issue')
    expect(link).toHaveTextContent('GH #3')
    expect(link).toHaveAttribute('href', 'https://github.test/acme/app/issues/3')
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
    // The card's own identifier is unchanged beside it.
    expect(screen.getByText('#1')).toBeInTheDocument()
    unmount()

    renderWithProviders(
      <CardModal boardId="board-1" card={card} opened onClose={() => undefined} />,
    )

    await waitFor(() => expect(screen.getByText('Implement terminal')).toBeInTheDocument())
    expect(screen.queryByTestId('card-external-issue')).not.toBeInTheDocument()
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
    server.use(agentDefinitionsHandler(), discussionHandler())
    renderWithProviders(
      <CardModal
        boardId="board-1"
        card={{
          ...card,
          sessions: [
            {
              id: 'session-1',
              definitionName: 'claude',
              agentKind: 'ClaudeCode',
              status: 'Stopping',
              cwd: 'D:/repo',
              createdAt: '2026-01-01T00:00:00Z',
              startedAt: '2026-01-01T00:00:00Z',
              lastSeenAt: '2026-01-01T00:00:01Z',
              endedAt: null,
              exitCode: null,
              failureReason: null,
            },
          ],
        }}
        opened
        onClose={() => undefined}
      />,
    )

    await waitFor(() => expect(getAgentInput()).toHaveValue('claude (ClaudeCode, default)'))
    expect(screen.getByRole('button', { name: 'Spawn' })).toBeDisabled()
  })
})
