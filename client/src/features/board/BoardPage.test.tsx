import { Button } from '@mantine/core'
import { HttpResponse, delay, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { useQueryClient } from '@tanstack/react-query'
import { boardKeys, type BoardDetailDto, useMoveCard } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'

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
          identifier: 'CARD-0001',
          title: 'Drag me',
          description: 'Move through the board',
          priority: 1,
          labels: [],
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
