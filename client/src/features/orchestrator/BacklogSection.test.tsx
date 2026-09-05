import { HttpResponse, http } from 'msw'
import { afterEach, describe, expect, it } from 'vitest'
import type { BoardColumnDto, BoardSummaryDto, CardDto, CardListDto, CardQuadrant } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { BacklogSection } from './BacklogSection'

function card(overrides: Partial<CardDto> = {}): CardDto {
  return {
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
    title: 'A backlog card',
    description: '',
    importance: 'Normal',
    urgency: 'Normal',
    dueAt: null,
    urgentSince: null,
    effectiveUrgency: 'Normal',
    quadrant: 'Someday',
    rank: 10,
    labels: [],
    status: 'Backlog',
    concurrencyToken: 'token-1',
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
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

function board(id: string, name: string): BoardSummaryDto {
  return {
    id,
    projectId: 'project-1',
    projectName: 'Antiphon',
    name,
    description: '',
    trackerKind: 'Internal',
    maxConcurrentSessions: 1,
    cardCount: 1,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  }
}

const DEFAULT_COLUMNS: BoardColumnDto[] = [
  {
    id: 'column-backlog',
    stateKey: 'backlog',
    name: 'Backlog',
    columnOrder: 0,
    cardStatus: 'Backlog',
    isActive: false,
    isTerminal: false,
    maxConcurrentSessions: null,
    cards: [],
  },
  {
    id: 'column-in-progress',
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
    cards: [],
  },
  {
    id: 'column-needs-decision',
    stateKey: 'needs-decision',
    name: 'Needs decision',
    columnOrder: 3,
    cardStatus: 'NeedsDecision',
    isActive: false,
    isTerminal: false,
    maxConcurrentSessions: null,
    cards: [],
  },
  {
    id: 'column-done',
    stateKey: 'done',
    name: 'Done',
    columnOrder: 4,
    cardStatus: 'Done',
    isActive: false,
    isTerminal: true,
    maxConcurrentSessions: null,
    cards: [],
  },
  {
    id: 'column-canceled',
    stateKey: 'canceled',
    name: 'Canceled',
    columnOrder: 5,
    cardStatus: 'Canceled',
    isActive: false,
    isTerminal: true,
    maxConcurrentSessions: null,
    cards: [],
  },
]

function serve({
  cards = [],
  truncated = false,
  boards = [board('board-1', 'Antiphon')],
  cardsError = false,
  columns = DEFAULT_COLUMNS,
}: {
  cards?: CardDto[]
  truncated?: boolean
  boards?: BoardSummaryDto[]
  cardsError?: boolean
  columns?: BoardColumnDto[]
} = {}) {
  server.use(
    http.get('/api/cards', () => {
      if (cardsError) return HttpResponse.json({ title: 'cards down' }, { status: 500 })
      return HttpResponse.json<CardListDto>({ cards, truncated })
    }),
    http.get('/api/boards', () => HttpResponse.json(boards)),
    http.get('/api/boards/:id/columns', () => HttpResponse.json(columns)),
  )
}

function box(quadrant: CardQuadrant) {
  return screen.getByTestId(`backlog-box-${quadrant}`)
}

describe('BacklogSection', () => {
  afterEach(() => {
    window.history.pushState({}, '', '/')
  })

  it('renders four boxes with their counts', async () => {
    serve({
      cards: [
        card({ id: 's1', identifier: 'CARD-0007', quadrant: 'Schedule', rank: 7, importance: 'High' }),
        card({ id: 's2', identifier: 'CARD-0008', quadrant: 'Schedule', rank: 7, importance: 'High' }),
        card({ id: 'd1', identifier: 'CARD-0010', quadrant: 'Someday', rank: 10 }),
      ],
    })
    renderWithProviders(<BacklogSection />)

    expect(await screen.findByRole('heading', { name: 'Backlog' })).toBeInTheDocument()
    expect(screen.getByText('3 outstanding')).toBeInTheDocument()
    expect(screen.getByText('on 1 board')).toBeInTheDocument()
    expect(within(box('DoFirst')).getByText('0')).toBeInTheDocument()
    expect(within(box('Schedule')).getByText('2')).toBeInTheDocument()
    expect(within(box('Clear')).getByText('0')).toBeInTheDocument()
    expect(within(box('Someday')).getByText('1')).toBeInTheDocument()
  })

  it('an empty box shows Nothing here and its hint', async () => {
    serve({ cards: [card({ quadrant: 'Someday' })] })
    renderWithProviders(<BacklogSection />)

    const doFirst = await screen.findByTestId('backlog-box-DoFirst')
    expect(within(doFirst).getByText('important and urgent')).toBeInTheDocument()
    expect(within(doFirst).getByText('Nothing here.')).toBeInTheDocument()
    expect(within(box('Clear')).getByText('urgent, not important')).toBeInTheDocument()
    expect(within(box('Clear')).getByText('Nothing here.')).toBeInTheDocument()
    expect(within(box('Schedule')).getByText('important, not yet urgent')).toBeInTheDocument()
    expect(within(box('Someday')).getByText('neither, yet')).toBeInTheDocument()
    expect(within(box('Someday')).queryByText('Nothing here.')).not.toBeInTheDocument()
  })

  it('caps a box at 12 rows then Show all 14 expands it and Show fewer collapses it', async () => {
    serve({
      cards: Array.from({ length: 14 }, (_, index) => card({
        id: `card-${index + 1}`,
        identifier: `CARD-${String(index + 1).padStart(4, '0')}`,
        title: `Someday ${index + 1}`,
        quadrant: 'Someday',
        rank: 10,
        createdAt: `2026-08-${String(index + 1).padStart(2, '0')}T00:00:00Z`,
      })),
    })
    renderWithProviders(<BacklogSection />)

    const someday = await screen.findByTestId('backlog-box-Someday')
    expect(within(someday).getAllByTestId(/backlog-row-/)).toHaveLength(12)
    const showAll = within(someday).getByRole('button', { name: 'Show all 14' })
    await userEvent.click(showAll)
    expect(within(someday).getAllByTestId(/backlog-row-/)).toHaveLength(14)
    await userEvent.click(within(someday).getByRole('button', { name: 'Show fewer' }))
    expect(within(someday).getAllByTestId(/backlog-row-/)).toHaveLength(12)
  })

  it('shows a board chip only when two boards are in the list', async () => {
    serve({
      cards: [
        card({ id: 'card-1', identifier: 'CARD-0001', boardId: 'board-1', title: 'First card' }),
        card({ id: 'card-2', identifier: 'CARD-0002', boardId: 'board-2', title: 'Second card' }),
      ],
      boards: [board('board-1', 'Antiphon'), board('board-2', 'Gym Stat')],
    })
    const { unmount } = renderWithProviders(<BacklogSection />)
    expect(await screen.findByText('Antiphon')).toBeInTheDocument()
    expect(screen.getByText('Gym Stat')).toBeInTheDocument()
    expect(screen.getByText('on 2 boards')).toBeInTheDocument()
    unmount()

    serve({
      cards: [
        card({ id: 'card-1', identifier: 'CARD-0001', boardId: 'board-1', title: 'First card' }),
        card({ id: 'card-2', identifier: 'CARD-0002', boardId: 'board-1', title: 'Second card' }),
      ],
      boards: [board('board-1', 'Antiphon'), board('board-2', 'Gym Stat')],
    })
    renderWithProviders(<BacklogSection />)
    expect(await screen.findByTestId('backlog-row-CARD-0001')).toBeInTheDocument()
    expect(screen.getByText('on 1 board')).toBeInTheDocument()
    expect(screen.queryByText('Antiphon')).not.toBeInTheDocument()
    expect(screen.queryByText('Gym Stat')).not.toBeInTheDocument()
  })

  it('shows reorder handles on a single-board box and hides them when two boards are present', async () => {
    serve({
      cards: [
        card({ id: 'card-1', identifier: 'CARD-0001', boardId: 'board-1', title: 'Only board' }),
      ],
    })
    const { unmount } = renderWithProviders(<BacklogSection />)
    expect(await screen.findByLabelText('Reorder CARD-0001')).toBeInTheDocument()
    unmount()

    serve({
      cards: [
        card({ id: 'card-1', identifier: 'CARD-0001', boardId: 'board-1', title: 'First card' }),
        card({ id: 'card-2', identifier: 'CARD-0002', boardId: 'board-2', title: 'Second card' }),
      ],
      boards: [board('board-1', 'Antiphon'), board('board-2', 'Gym Stat')],
    })
    renderWithProviders(<BacklogSection />)
    expect((await screen.findAllByText(/reorder on the board/)).length).toBeGreaterThan(0)
    expect(screen.queryByLabelText('Reorder CARD-0001')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Reorder CARD-0002')).not.toBeInTheDocument()
  })

  it('states fleet truncation instead of counting 500 as the backlog', async () => {
    serve({ cards: [card()], truncated: true })
    renderWithProviders(<BacklogSection />)
    expect(await screen.findByText(
      'Showing the 500 most recently updated cards; open the board for the rest.',
    )).toBeInTheDocument()
  })

  it('row click navigates to the card on its board', async () => {
    serve({ cards: [card({ id: 'card-99', boardId: 'board-7', identifier: 'CARD-0099' })] })
    renderWithProviders(<BacklogSection />)
    await userEvent.click(await screen.findByTestId('backlog-row-CARD-0099'))
    await waitFor(() =>
      expect(window.location.pathname + window.location.search).toBe('/boards/board-7?card=card-99'),
    )
  })

  it('an error response is an alert inside the section', async () => {
    serve({ cardsError: true })
    renderWithProviders(<BacklogSection />)
    expect(await screen.findByTestId('backlog-error')).toBeInTheDocument()
    expect(screen.queryByTestId('backlog-box-DoFirst')).not.toBeInTheDocument()
  })

  it('with columns loaded, the kebab lists legal Move to targets for a Backlog card', async () => {
    serve({ cards: [card({ identifier: 'CARD-0001' })] })
    renderWithProviders(<BacklogSection />)

    await userEvent.click(await screen.findByLabelText('Actions for CARD-0001'))
    expect(await screen.findByTestId('move-to-in-progress')).toHaveTextContent('In Progress — spawns an agent')
    expect(screen.getByTestId('move-to-review')).toHaveTextContent('Review')
    expect(screen.getByTestId('move-to-needs-decision')).toHaveTextContent('Needs decision')
    expect(screen.getByTestId('move-to-done')).toHaveTextContent('Done')
    expect(screen.getByTestId('move-to-canceled')).toHaveTextContent('Canceled')
    expect(screen.queryByTestId('move-to-backlog')).not.toBeInTheDocument()
  })

  it('without columns, there is no kebab and the row still opens', async () => {
    serve({
      cards: [card({ id: 'card-99', boardId: 'board-7', identifier: 'CARD-0099' })],
      columns: [],
    })
    renderWithProviders(<BacklogSection />)

    const row = await screen.findByTestId('backlog-row-CARD-0099')
    expect(screen.queryByLabelText('Actions for CARD-0099')).not.toBeInTheDocument()
    await userEvent.click(row)
    await waitFor(() =>
      expect(window.location.pathname + window.location.search).toBe('/boards/board-7?card=card-99'),
    )
  })
})
