import { Button } from '@mantine/core'
import { HttpResponse, delay, http } from 'msw'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { useQueryClient } from '@tanstack/react-query'
import { Route, Routes } from 'react-router'
import { boardKeys, type BoardDetailDto, useMoveCard } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { BoardPage } from './BoardPage'

beforeAll(() => {
  Element.prototype.scrollIntoView = vi.fn()
})

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
        {
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
          title: 'Drag me',
          description: 'Move through the board',
          priority: 1,
          labels: ['feature', 'backend'],
          status: 'Backlog',
          concurrencyToken: 'token-1',
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
          startedAt: null,
          completedAt: null,
          terminalReason: null,
          sessions: [],
        },
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
  ],
}

function MoveHarness({ targetColumnId = 'column-active' }: { targetColumnId?: string }) {
  const queryClient = useQueryClient()
  const moveCard = useMoveCard('board-1')
  return (
    <Button
      onClick={() => {
        queryClient.setQueryData(boardKeys.detail('board-1'), board)
        moveCard.mutate({
          cardId: 'card-1',
          request: {
            boardColumnId: targetColumnId,
            concurrencyToken: 'token-1',
          },
        })
      }}
    >
      Move
    </Button>
  )
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
    cardCount: overrides.columns?.reduce((total, column) => total + column.cards.length, 0) ?? 1,
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

function renderBoardRoute(initialPath: string) {
  window.history.pushState({}, '', initialPath)
  return renderWithProviders(
    <Routes>
      <Route path="/boards" element={<BoardPage />} />
      <Route path="/boards/:id" element={<BoardPage />} />
    </Routes>,
  )
}

describe('board card movement', () => {
  it('optimistically moves a dragged card and sends the concurrency token', async () => {
    const patchSpy = vi.fn()
    server.use(
      http.patch('/api/cards/card-1', async ({ request }) => {
        patchSpy(await request.json())
        await delay(50)
        return HttpResponse.json({
          ...board.columns[0].cards[0],
          boardColumnId: 'column-active',
          status: 'InProgress',
        })
      }),
    )
    const { queryClient } = renderWithProviders(<MoveHarness />)

    await userEvent.click(screen.getByRole('button', { name: 'Move' }))

    const optimistic = queryClient.getQueryData<BoardDetailDto>(boardKeys.detail('board-1'))
    expect(optimistic?.columns[0].cards).toHaveLength(0)
    expect(optimistic?.columns[1].cards[0].id).toBe('card-1')

    await waitFor(() => expect(patchSpy).toHaveBeenCalledWith({
      boardColumnId: 'column-active',
      concurrencyToken: 'token-1',
    }))
  })

  it('rolls back the optimistic move when the API rejects the drag', async () => {
    server.use(
      http.patch('/api/cards/card-1', () => {
        return HttpResponse.json({ title: 'Conflict' }, { status: 409 })
      }),
    )
    const { queryClient } = renderWithProviders(<MoveHarness />)

    await userEvent.click(screen.getByRole('button', { name: 'Move' }))

    await waitFor(() => {
      const restored = queryClient.getQueryData<BoardDetailDto>(boardKeys.detail('board-1'))
      expect(restored?.columns[0].cards[0].id).toBe('card-1')
      expect(restored?.columns[1].cards).toHaveLength(0)
    })
  })
})

describe('BoardPage board selector', () => {
  it('opens the card workspace from the card query string and clears it on close', async () => {
    server.use(
      http.get('/api/boards', () => HttpResponse.json([boardSummary()])),
      http.get('/api/boards/board-1', () => HttpResponse.json(board)),
      http.get('/api/projects', () => HttpResponse.json([])),
      agentDefinitionsHandler(),
    )

    renderBoardRoute('/boards/board-1?card=card-1')

    const cardPage = await screen.findByTestId('card-detail-page')
    expect(cardPage).toBeInTheDocument()
    expect(within(cardPage).getByRole('heading', { name: 'Drag me' })).toBeInTheDocument()
    expect(screen.getByTestId('card-detail-sidebar')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close card' }))

    await waitFor(() => expect(window.location.search).toBe(''))
  })

  it('stores the selected card in the URL when a board card opens', async () => {
    server.use(
      http.get('/api/boards', () => HttpResponse.json([boardSummary()])),
      http.get('/api/boards/board-1', () => HttpResponse.json(board)),
      http.get('/api/projects', () => HttpResponse.json([])),
      agentDefinitionsHandler(),
    )

    renderBoardRoute('/boards/board-1')

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
        {
          ...board.columns[0],
          id: 'column-2-backlog',
          cards: [],
        },
        {
          ...board.columns[1],
          id: 'column-2-active',
          cards: [
            {
              ...board.columns[0].cards[0],
              id: 'card-2',
              boardId: 'board-2',
              boardColumnId: 'column-2-active',
              identifier: 'CARD-0002',
              title: 'Ship API',
              status: 'InProgress',
              labels: ['api'],
            },
          ],
        },
      ],
    }

    server.use(
      http.get('/api/boards', () =>
        HttpResponse.json([
          {
            id: 'board-1',
            projectId: 'project-1',
            projectName: 'Project One',
            name: 'Delivery',
            description: '',
            trackerKind: 'Internal',
            maxConcurrentSessions: 1,
            cardCount: 3,
            createdAt: '2026-01-01T00:00:00Z',
            updatedAt: '2026-01-02T00:00:00Z',
          },
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
    expect(await screen.findByRole('article', { name: 'CARD-0002 Ship API' })).toBeInTheDocument()
    expect(screen.getByTestId('board-column-backlog')).toBeInTheDocument()
    expect(screen.getByTestId('board-column-in-progress')).toBeInTheDocument()
    expect(screen.getByText('Delivery')).toBeInTheDocument()
    expect(screen.getByText('Support')).toBeInTheDocument()

    await userEvent.click(screen.getByDisplayValue('All'))

    expect(await screen.findByText('Project One / Delivery')).toBeInTheDocument()
  })
})
