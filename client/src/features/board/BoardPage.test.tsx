import { HttpResponse, delay, http } from 'msw'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router'
import { boardKeys, type BoardDetailDto, type CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { BoardPage } from './BoardPage'

beforeAll(() => {
  Element.prototype.scrollIntoView = vi.fn()
})

function card(overrides: Partial<CardDto> & { id: string; identifier: string }): CardDto {
  return {
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
    title: 'Untitled',
    description: '',
    importance: 'High', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 7,
    labels: [],
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
    ...overrides,
  }
}

const board: BoardDetailDto = {
  id: 'board-1',
  projectId: 'project-1',
  projectName: 'Project One',
  name: 'Delivery',
  description: '',
  trackerKind: 'Internal',
  maxConcurrentSessions: 1,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  columns: [
    {
      id: 'column-backlog',
      stateKey: 'backlog',
      name: 'Backlog',
      columnOrder: 0,
      cardStatus: 'Backlog',
      isActive: false,
      isTerminal: false,
      maxConcurrentSessions: null,
      cards: [
        card({
          id: 'card-1',
          identifier: 'CARD-0001',
          title: 'Drag me',
          description: 'Move through the board',
          labels: ['feature', 'backend'],
        }),
        card({
          id: 'card-3',
          identifier: 'CARD-0003',
          title: 'Pty host loses the first prompt',
          importance: 'Critical', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 4,
          labels: ['bug'],
        }),
      ],
    },
    {
      id: 'column-active',
      stateKey: 'in-progress',
      name: 'In Progress',
      columnOrder: 1,
      cardStatus: 'InProgress',
      isActive: true,
      isTerminal: false,
      maxConcurrentSessions: null,
      cards: [],
    },
    {
      id: 'column-review',
      stateKey: 'review',
      name: 'Review',
      columnOrder: 2,
      cardStatus: 'Review',
      isActive: false,
      isTerminal: false,
      maxConcurrentSessions: null,
      cards: [
        card({
          id: 'card-2',
          identifier: 'CARD-0002',
          boardColumnId: 'column-review',
          status: 'Review',
          title: 'Working label never clears',
          importance: 'Critical', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 4,
        }),
      ],
    },
    {
      id: 'column-done',
      stateKey: 'done',
      name: 'Done',
      columnOrder: 3,
      cardStatus: 'Done',
      isActive: false,
      isTerminal: true,
      maxConcurrentSessions: null,
      cards: [
        card({
          id: 'card-4',
          identifier: 'CARD-0004',
          boardColumnId: 'column-done',
          status: 'Done',
          title: 'Shipped already',
          completedAt: '2026-01-02T00:00:00Z',
        }),
      ],
    },
  ],
}

function boardSummary(overrides: Partial<BoardDetailDto> = {}) {
  return {
    id: overrides.id ?? board.id,
    projectId: overrides.projectId ?? board.projectId,
    projectName: overrides.projectName ?? board.projectName,
    name: overrides.name ?? board.name,
    description: overrides.description ?? board.description,
    trackerKind: overrides.trackerKind ?? board.trackerKind,
    maxConcurrentSessions: overrides.maxConcurrentSessions ?? board.maxConcurrentSessions,
    cardCount: overrides.columns?.reduce((total, column) => total + column.cards.length, 0) ?? 4,
    createdAt: overrides.createdAt ?? board.createdAt,
    updatedAt: overrides.updatedAt ?? board.updatedAt,
  }
}

function agentDefinitionsHandler() {
  return http.get('/api/agents/definitions', () =>
    HttpResponse.json({
      defaultDefinition: 'claude',
      definitions: [{ name: 'claude', kind: 'ClaudeCode', isDefault: true }],
    }),
  )
}

function boardHandlers(detail: BoardDetailDto = board) {
  return [
    http.get('/api/boards', () => HttpResponse.json([boardSummary()])),
    http.get('/api/boards/board-1', () => HttpResponse.json(detail)),
    http.get('/api/projects', () => HttpResponse.json([])),
    agentDefinitionsHandler(),
  ]
}

function renderBoardRoute(initialPath: string) {
  window.history.pushState({}, '', initialPath)
  return renderWithProviders(
    <Routes>
      <Route path="/boards" element={<BoardPage />} />
      <Route path="/boards/:id" element={<BoardPage />} />
    </Routes>,
  )
}

function setViewport(mobile: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: mobile && query.includes('max-width'),
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  })
}

describe('the shape strip', () => {
  it('renders a node per state, with the empty state kept on screen', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1')

    const backlog = await screen.findByTestId('state-node-backlog')
    expect(within(backlog).getByText('2')).toBeInTheDocument()

    // 0 In Progress while work is supposedly happening IS the finding — the node never vanishes.
    const inProgress = screen.getByTestId('state-node-in-progress')
    expect(within(inProgress).getByText('0')).toBeInTheDocument()
    expect(within(inProgress).getByText('—')).toBeInTheDocument()

    expect(screen.getByTestId('state-node-review')).toBeInTheDocument()
    expect(screen.getByTestId('state-node-done')).toBeInTheDocument()
    expect(screen.getByText(/edges show the usual path/)).toBeInTheDocument()
  })

  it('opens the first populated non-terminal state after the first by default', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1')

    // Review is the state most likely to need eyes; Backlog and Done stay collapsed.
    expect(await screen.findByRole('article', { name: 'CARD-0002 Working label never clears' }))
      .toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'CARD-0001 Drag me' })).not.toBeInTheDocument()
  })

  it('filters the list to a state on node click and puts it in the URL', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1')

    await userEvent.click(await screen.findByTestId('state-node-backlog'))

    await waitFor(() => expect(new URLSearchParams(window.location.search).get('state')).toBe('backlog'))
    expect(await screen.findByRole('article', { name: 'CARD-0001 Drag me' })).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'CARD-0002 Working label never clears' }))
      .not.toBeInTheDocument()

    await userEvent.click(screen.getByTestId('state-node-backlog'))
    await waitFor(() => expect(new URLSearchParams(window.location.search).get('state')).toBeNull())
  })

  it('restores the selected state from the URL', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=done')

    expect(await screen.findByRole('article', { name: 'CARD-0004 Shipped already' })).toBeInTheDocument()
  })
})

describe('search and filters', () => {
  it('narrows the list and reports n of m without hiding any state', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1')

    await userEvent.type(await screen.findByLabelText('Search cards'), 'pty')

    await waitFor(() => expect(new URLSearchParams(window.location.search).get('q')).toBe('pty'))
    const backlog = await screen.findByTestId('state-node-backlog')
    expect(within(backlog).getByText('of 2')).toBeInTheDocument()
    // Every state is still on screen: the states are the fixed frame, only contents vary.
    expect(screen.getByTestId('state-node-review')).toBeInTheDocument()
    expect(screen.getByTestId('state-node-done')).toBeInTheDocument()
  })

  it('finds a card by the short form of its identifier', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=backlog')

    await userEvent.type(await screen.findByLabelText('Search cards'), '#3')

    expect(await screen.findByRole('article', { name: 'CARD-0003 Pty host loses the first prompt' }))
      .toBeInTheDocument()
    await waitFor(() =>
      expect(screen.queryByRole('article', { name: 'CARD-0001 Drag me' })).not.toBeInTheDocument())
  })

  it('says so when nothing matches, and clears back', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1')

    await userEvent.type(await screen.findByLabelText('Search cards'), 'zzzz')

    expect(await screen.findByText('No cards match the current filter.')).toBeInTheDocument()

    await userEvent.click(screen.getAllByRole('button', { name: 'Clear filters' })[0])
    await waitFor(() => expect(window.location.search).toBe(''))
  })
})

describe('moving a card', () => {
  it('submits the move with its reason and concurrency token, optimistically', async () => {
    const patchSpy = vi.fn()
    server.use(
      ...boardHandlers(),
      http.patch('/api/cards/card-2', async ({ request }) => {
        patchSpy(await request.json())
        await delay(50)
        return HttpResponse.json({
          card: { ...board.columns[2].cards[0], boardColumnId: 'column-done', status: 'Done' },
          spawnedSessionId: null,
          spawnSuppressed: false,
        })
      }),
    )
    const { queryClient } = renderBoardRoute('/boards/board-1?state=review')

    await userEvent.click(await screen.findByLabelText('Actions for CARD-0002'))
    await userEvent.click(await screen.findByTestId('move-to-done'))
    await userEvent.type(await screen.findByLabelText('Reason'), 'fixed by CARD-0041')
    await userEvent.click(screen.getByRole('button', { name: 'Move' }))

    const optimistic = queryClient.getQueryData<BoardDetailDto>(boardKeys.detailSummary('board-1'))
    expect(optimistic?.columns[2].cards).toHaveLength(0)
    expect(optimistic?.columns[3].cards.map((item) => item.id)).toContain('card-2')

    // spawn: true is what keeps the human UX bit-identical now that the server no longer spawns
    // unasked - the dialog already warned that an active target starts an agent.
    await waitFor(() => expect(patchSpy).toHaveBeenCalledWith({
      boardColumnId: 'column-done',
      concurrencyToken: 'token-1',
      reason: 'fixed by CARD-0041',
      spawn: true,
    }))
  })

  it('rolls the optimistic move back when the API rejects it', async () => {
    server.use(
      ...boardHandlers(),
      http.patch('/api/cards/card-2', () => HttpResponse.json({ title: 'Conflict' }, { status: 409 })),
    )
    const { queryClient } = renderBoardRoute('/boards/board-1?state=review')

    await userEvent.click(await screen.findByLabelText('Actions for CARD-0002'))
    await userEvent.click(await screen.findByTestId('move-to-done'))
    await userEvent.click(await screen.findByRole('button', { name: 'Move' }))

    await waitFor(() => {
      const restored = queryClient.getQueryData<BoardDetailDto>(boardKeys.detailSummary('board-1'))
      expect(restored?.columns[2].cards[0].id).toBe('card-2')
      expect(restored?.columns[3].cards.map((item) => item.id)).not.toContain('card-2')
    })
  })

  it('says out loud that moving into an active state spawns an agent', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=review')

    await userEvent.click(await screen.findByLabelText('Actions for CARD-0002'))
    await userEvent.click(await screen.findByTestId('move-to-in-progress'))

    expect(await screen.findByText(/spawns an agent session on it/)).toBeInTheDocument()
  })

  it('offers no move out of a terminal state', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=done')

    await userEvent.click(await screen.findByLabelText('Actions for CARD-0004'))

    expect(await screen.findByTestId('no-move-target')).toBeInTheDocument()
    expect(screen.queryByTestId('move-to-backlog')).not.toBeInTheDocument()
  })

  it('copies the canonical identifier, not the short form', async () => {
    const writeText = vi.fn()
    Object.assign(navigator, { clipboard: { writeText } })
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=done')

    await userEvent.click(await screen.findByLabelText('Actions for CARD-0004'))
    await userEvent.click(await screen.findByTestId('copy-card-id'))

    expect(writeText).toHaveBeenCalledWith('CARD-0004')
  })
})

describe('archived cards', () => {
  const archivedCard = card({
    id: 'card-5',
    identifier: 'CARD-0005',
    title: 'Duplicate of CARD-0042',
    archivedAt: '2026-08-12T09:00:00Z',
    archivedReason: 'duplicate',
    archivedBy: 'operator',
  })

  /** Mirrors the server: archived cards are absent unless `?includeArchived=true` is asked for. */
  function archiveAwareHandlers() {
    const asked: boolean[] = []
    return {
      asked,
      handlers: [
        http.get('/api/boards', () => HttpResponse.json([boardSummary()])),
        http.get('/api/projects', () => HttpResponse.json([])),
        agentDefinitionsHandler(),
        http.get('/api/boards/board-1', ({ request }) => {
          const includeArchived = new URL(request.url).searchParams.get('includeArchived') === 'true'
          asked.push(includeArchived)
          return HttpResponse.json({
            ...board,
            columns: board.columns.map((column) => column.stateKey === 'backlog'
              ? { ...column, cards: includeArchived ? [...column.cards, archivedCard] : column.cards }
              : column),
          })
        }),
      ],
    }
  }

  it('leaves archived cards out until the chip is on', async () => {
    const { asked, handlers } = archiveAwareHandlers()
    server.use(...handlers)
    renderBoardRoute('/boards/board-1?state=backlog')

    expect(await screen.findByRole('article', { name: 'CARD-0001 Drag me' })).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: /CARD-0005/ })).not.toBeInTheDocument()
    expect(asked).toEqual([false])
  })

  it('refetches with includeArchived when the chip goes on, and puts it in the URL', async () => {
    const { asked, handlers } = archiveAwareHandlers()
    server.use(...handlers)
    renderBoardRoute('/boards/board-1?state=backlog')
    await screen.findByRole('article', { name: 'CARD-0001 Drag me' })

    await userEvent.click(screen.getByText('Archived'))

    // The URL carries the toggle, so a link to this board keeps showing the archived card.
    await waitFor(() => expect(new URLSearchParams(window.location.search).get('archived')).toBe('1'))
    const archivedRow = await screen.findByRole('article', { name: 'CARD-0005 Duplicate of CARD-0042' })
    expect(within(archivedRow).getByText('archived')).toBeInTheDocument()
    expect(archivedRow).toHaveStyle({ opacity: '0.55' })
    // A REFETCH under a different cache key, not a client-side filter — the card was never here.
    expect(asked).toContain(true)
  })

  it('restores the archived view straight from the URL', async () => {
    const { handlers } = archiveAwareHandlers()
    server.use(...handlers)
    renderBoardRoute('/boards/board-1?state=backlog&archived=1')

    expect(await screen.findByRole('article', { name: 'CARD-0005 Duplicate of CARD-0042' }))
      .toBeInTheDocument()
  })
})

describe('creating a board', () => {
  it('navigates to the new board, not the All-cards view', async () => {
    const created: BoardDetailDto = {
      ...board,
      id: 'board-created',
      name: 'Sprint Two',
      columns: board.columns.map((column) => ({
        ...column,
        id: `${column.id}-created`,
        cards: [],
      })),
    }

    server.use(
      http.get('/api/boards', () => HttpResponse.json([boardSummary()])),
      http.get('/api/boards/board-1', () => HttpResponse.json(board)),
      http.get('/api/boards/board-created', () => HttpResponse.json(created)),
      http.get('/api/projects', () =>
        HttpResponse.json([
          {
            id: 'project-1',
            name: 'Project One',
            gitRepositoryUrl: 'https://example.test/repo.git',
            baseBranch: 'master',
            constitutionPath: '',
            gitHubIntegrationEnabled: false,
            notificationsEnabled: false,
            createdAt: '2026-01-01T00:00:00Z',
            updatedAt: '2026-01-01T00:00:00Z',
          },
        ]),
      ),
      agentDefinitionsHandler(),
      http.post('/api/boards', () => HttpResponse.json(created)),
    )

    renderBoardRoute('/boards')
    await userEvent.click(await screen.findByRole('button', { name: 'New Board' }))

    const dialog = await screen.findByRole('dialog', { name: 'New Board' })
    await userEvent.click(within(dialog).getByRole('textbox', { name: 'Project' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Project One' }))
    await userEvent.type(within(dialog).getByLabelText('Name'), 'Sprint Two')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(window.location.pathname).toBe('/boards/board-created'))
    expect(await screen.findByText('Project One / Sprint Two')).toBeInTheDocument()
    expect(screen.queryByText('All cards')).not.toBeInTheDocument()
  })
})

describe('creating a card', () => {
  async function openCreateDialog() {
    renderBoardRoute('/boards/board-1')
    await userEvent.click(await screen.findByRole('button', { name: 'New Card' }))
    return {
      title: screen.getByLabelText('Title') as HTMLInputElement,
      description: screen.getByLabelText('Description') as HTMLTextAreaElement,
    }
  }

  it('counts the description against the shared limit and blocks an over-limit create', async () => {
    server.use(...boardHandlers())
    const { title, description } = await openCreateDialog()

    await userEvent.type(title, 'A new card')
    expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled()

    setInputValue(description, 'x'.repeat(20_001))
    expect(screen.getByText('20,001 / 20,000')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled()
  })

  it('renders a 422 on the field it names, not as an opaque failure', async () => {
    server.use(
      ...boardHandlers(),
      http.post('/api/boards/board-1/cards', () =>
        HttpResponse.json({
          title: 'One or more validation errors occurred.',
          status: 422,
          errors: { Title: ['Title must be at most 300 characters; got 301.'] },
        }, { status: 422 })),
    )
    const { title } = await openCreateDialog()

    await userEvent.type(title, 'A new card')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('Title must be at most 300 characters; got 301.')).toBeInTheDocument()
  })
})

/** Sets a controlled input's value in one shot — 20k characters is not a thing to type. */
function setInputValue(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(
    element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype,
    'value',
  )?.set
  setter?.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
}

describe('the mobile state pager', () => {
  it('shows one state with the states that feed it and the ones it leads to', async () => {
    setViewport(true)
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=review')

    expect(await screen.findByTestId('state-pager')).toBeInTheDocument()
    expect(screen.queryByTestId('shape-strip')).not.toBeInTheDocument()

    expect(screen.getByTestId('pager-connector-from')).toHaveAccessibleName('from In Progress, 0 cards')
    expect(screen.getByTestId('pager-connector-to')).toHaveAccessibleName('to Done, 1 cards')
    expect(screen.getByRole('article', { name: 'CARD-0002 Working label never clears' })).toBeInTheDocument()

    setViewport(false)
  })

  it('navigates along a connector and keeps ?state= in the URL', async () => {
    setViewport(true)
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=review')

    await userEvent.click(await screen.findByTestId('pager-connector-to'))

    await waitFor(() => expect(new URLSearchParams(window.location.search).get('state')).toBe('done'))
    expect(await screen.findByRole('article', { name: 'CARD-0004 Shipped already' })).toBeInTheDocument()

    setViewport(false)
  })

  it('keeps an empty state on the pager and explains what it means', async () => {
    setViewport(true)
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=in-progress')

    const empty = await screen.findByTestId('state-empty-in-progress')
    expect(empty).toHaveTextContent(/Nothing is in In Progress/)
    expect(empty).toHaveTextContent(/spawns an agent/)

    setViewport(false)
  })

  it('jumps to a state from the dot rail', async () => {
    setViewport(true)
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1?state=review')

    await screen.findByTestId('state-pager')
    await userEvent.click(screen.getByRole('button', { name: 'Go to Backlog, 2 cards' }))

    await waitFor(() => expect(new URLSearchParams(window.location.search).get('state')).toBe('backlog'))

    setViewport(false)
  })
})

describe('BoardPage board selector', () => {
  it('requests the selected board summary representation', async () => {
    const queries: string[] = []
    server.use(
      http.get('/api/boards/board-1', ({ request }) => {
        queries.push(new URL(request.url).search)
        return HttpResponse.json(board)
      }),
      http.get('/api/boards', () => HttpResponse.json([boardSummary()])),
      http.get('/api/projects', () => HttpResponse.json([])),
      agentDefinitionsHandler(),
    )
    renderBoardRoute('/boards/board-1')
    await screen.findByRole('button', { name: 'New Card' })
    expect(queries).toContain('?view=summary')
  })

  it('opens the card workspace from the card query string and clears it on close', async () => {
    server.use(...boardHandlers())

    renderBoardRoute('/boards/board-1?card=card-1')

    const cardPage = await screen.findByTestId('card-detail-page')
    expect(cardPage).toBeInTheDocument()
    expect(within(cardPage).getByRole('heading', { name: 'Drag me' })).toBeInTheDocument()
    expect(within(cardPage).getByText('#1')).toBeInTheDocument()
    expect(screen.getByTestId('card-detail-sidebar')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close card' }))

    await waitFor(() => expect(window.location.search).toBe(''))
  })

  it('stores the selected card in the URL when a board card opens', async () => {
    server.use(...boardHandlers())

    renderBoardRoute('/boards/board-1?state=backlog')

    await userEvent.click(await screen.findByRole('article', { name: 'CARD-0001 Drag me' }))

    expect(await screen.findByTestId('card-detail-page')).toBeInTheDocument()
    expect(new URLSearchParams(window.location.search).get('card')).toBe('card-1')
  })

  it('keeps /boards on the All selection and shows cards from every board', async () => {
    const supportBoard: BoardDetailDto = {
      ...board,
      id: 'board-2',
      projectId: 'project-2',
      projectName: 'Project Two',
      name: 'Support',
      columns: [
        { ...board.columns[0], id: 'column-2-backlog', cards: [] },
        {
          ...board.columns[1],
          id: 'column-2-active',
          cards: [
            card({
              id: 'card-9',
              identifier: 'CARD-0009',
              boardId: 'board-2',
              boardColumnId: 'column-2-active',
              title: 'Ship API',
              status: 'InProgress',
              labels: ['api'],
            }),
          ],
        },
        { ...board.columns[2], id: 'column-2-review', cards: [] },
        { ...board.columns[3], id: 'column-2-done', cards: [] },
      ],
    }

    server.use(
      http.get('/api/boards', () =>
        HttpResponse.json([
          boardSummary(),
          {
            id: 'board-2',
            projectId: 'project-2',
            projectName: 'Project Two',
            name: 'Support',
            description: '',
            trackerKind: 'Internal',
            maxConcurrentSessions: 1,
            cardCount: 1,
            createdAt: '2026-01-01T00:00:00Z',
            updatedAt: '2026-01-02T00:00:00Z',
          },
        ]),
      ),
      http.get('/api/boards/board-1', () => HttpResponse.json(board)),
      http.get('/api/boards/board-2', () => HttpResponse.json(supportBoard)),
      http.get('/api/projects', () => HttpResponse.json([])),
    )

    window.history.pushState({}, '', '/boards')
    renderWithProviders(<BoardPage />)

    expect(await screen.findByDisplayValue('All')).toBeInTheDocument()
    expect(await screen.findByText('All cards')).toBeInTheDocument()
    expect(await screen.findByRole('article', { name: 'CARD-0001 Drag me' })).toBeInTheDocument()
    expect(await screen.findByRole('article', { name: 'CARD-0009 Ship API' })).toBeInTheDocument()
    expect(screen.getByTestId('board-column-backlog')).toBeInTheDocument()
    expect(screen.getByTestId('board-column-in-progress')).toBeInTheDocument()
    expect(screen.getAllByText('Delivery').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Support').length).toBeGreaterThan(0)

    await userEvent.click(screen.getByDisplayValue('All'))

    expect(await screen.findByText('Project One / Delivery')).toBeInTheDocument()
  })
})

describe('tracker sync button (CARD-0166 S7)', () => {
  it('is hidden on Internal boards', async () => {
    server.use(...boardHandlers())
    renderBoardRoute('/boards/board-1')
    await screen.findByRole('button', { name: 'New Card' })
    expect(screen.queryByRole('button', { name: 'Sync tracker now' })).not.toBeInTheDocument()
  })

  it('shows on GitHubIssues boards and POSTs the per-board sync endpoint', async () => {
    const githubBoard: BoardDetailDto = { ...board, trackerKind: 'GitHubIssues' }
    let syncPosts = 0
    server.use(
      http.get('/api/boards', () => HttpResponse.json([boardSummary(githubBoard)])),
      http.get('/api/boards/board-1', () => HttpResponse.json(githubBoard)),
      http.get('/api/projects', () => HttpResponse.json([])),
      agentDefinitionsHandler(),
      http.post('/api/boards/board-1/tracker/sync', () => {
        syncPosts += 1
        return HttpResponse.json({
          boards: [{
            boardId: 'board-1',
            boardName: 'Delivery',
            issuesPulled: 2,
            commentsIn: 0,
            commentsOut: 1,
            labelsChanged: 1,
            stateChanges: 0,
            creates: 0,
            skips: [],
          }],
          concurrentRunSkipped: false,
        })
      }),
    )
    renderBoardRoute('/boards/board-1')
    const syncButton = await screen.findByRole('button', { name: 'Sync tracker now' })
    await userEvent.click(syncButton)
    await waitFor(() => {
      expect(syncPosts).toBe(1)
    })
  })
})
