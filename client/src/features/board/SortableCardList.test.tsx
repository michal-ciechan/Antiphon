import { HttpResponse, http } from 'msw'
import { fireEvent } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Notifications } from '@mantine/notifications'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { placementFromReorder, SortableCardList } from './SortableCardList'

const NOW = new Date('2026-08-13T12:00:00Z')

const columns: BoardColumnDto[] = [
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
]

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
    title: 'First',
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

const cards: CardDto[] = [
  card({ id: 'card-1', identifier: 'CARD-0001', title: 'First', concurrencyToken: 't1' }),
  card({
    id: 'card-2',
    identifier: 'CARD-0002',
    title: 'Second',
    concurrencyToken: 't2',
    createdAt: '2026-08-02T00:00:00Z',
  }),
  card({
    id: 'card-3',
    identifier: 'CARD-0003',
    title: 'Third',
    concurrencyToken: 't3',
    createdAt: '2026-08-03T00:00:00Z',
  }),
]

describe('placementFromReorder', () => {
  it('names both neighbours after a one-step move down', () => {
    const placed = placementFromReorder(cards, 0, 1)
    expect(placed.cardId).toBe('card-1')
    expect(placed.after).toBe('CARD-0002')
    expect(placed.before).toBe('CARD-0003')
    expect(placed.orderedIds).toEqual(['card-2', 'card-1', 'card-3'])
  })
})

describe('SortableCardList', () => {
  it('activates the KeyboardSensor on Space — jsdom has no layout, so ArrowDown cannot change over', () => {
    renderWithProviders(
      <>
        <Notifications />
        <SortableCardList
          cards={cards}
          boardId="board-1"
          columns={columns}
          now={NOW}
          onOpen={vi.fn()}
          enabled
        />
      </>,
    )

    const handle = screen.getByLabelText('Reorder CARD-0001')
    handle.focus()
    fireEvent.keyDown(handle, { key: ' ', code: 'Space' })
    expect(handle).toHaveAttribute('aria-pressed', 'true')
  })

  it('PATCHes placement Top from Move to top', async () => {
    const spy = vi.fn()
    server.use(
      http.patch('/api/cards/:id/position', async ({ request, params }) => {
        spy(params.id, await request.json())
        return HttpResponse.json({ ...cards[1], position: 1 })
      }),
    )

    renderWithProviders(
      <>
        <Notifications />
        <SortableCardList
          cards={cards}
          boardId="board-1"
          columns={columns}
          now={NOW}
          onOpen={vi.fn()}
          enabled
        />
      </>,
    )

    await userEvent.click(screen.getAllByLabelText(/Actions for CARD-0002/)[0])
    await userEvent.click(await screen.findByTestId('move-to-top'))
    await waitFor(() => expect(spy).toHaveBeenCalled())
    const [, body] = spy.mock.calls[0] as [string, { placement?: string }]
    expect(body.placement).toBe('Top')
  })
})
