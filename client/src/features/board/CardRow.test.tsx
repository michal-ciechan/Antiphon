import { describe, expect, it, vi } from 'vitest'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, within } from '../../test/utils'
import { CardRow } from './CardRow'
import { StateNode } from './StateNode'
import { buildBoardShape, EMPTY_FILTER } from './boardShapeModel'

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
    identifier: 'CARD-0041',
    title: 'A compacted session reads Working forever',
    description: 'two post-compaction records escape the rule',
    priority: 0,
    labels: ['session', 'reliability', 'never-shown'],
    status: 'Backlog',
    concurrencyToken: 'token-1',
    createdAt: '2026-08-10T12:00:00Z',
    updatedAt: '2026-08-10T12:00:00Z',
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

function renderRow(overrides: Partial<CardDto> = {}, onOpen = vi.fn()) {
  renderWithProviders(
    <CardRow
      card={card(overrides)}
      boardId="board-1"
      columns={columns}
      now={NOW}
      onOpen={onOpen}
    />,
  )
  return { onOpen }
}

describe('CardRow', () => {
  it('renders the short identifier but keeps the canonical one as its accessible name', () => {
    renderRow()
    const row = screen.getByRole('article', {
      name: 'CARD-0041 A compacted session reads Working forever',
    })
    expect(within(row).getByText('#41')).toBeInTheDocument()
  })

  it('shows priority, the first two labels and the card age', () => {
    renderRow()
    const row = screen.getByRole('article', { name: /CARD-0041/ })
    expect(within(row).getByText('P0')).toBeInTheDocument()
    expect(within(row).getByText('session')).toBeInTheDocument()
    expect(within(row).getByText('reliability')).toBeInTheDocument()
    expect(within(row).queryByText('never-shown')).not.toBeInTheDocument()
    expect(within(row).getByText('3d')).toBeInTheDocument()
  })

  it('drops the description — it was clamp-2 noise at this density', () => {
    renderRow()
    expect(screen.queryByText(/two post-compaction records/)).not.toBeInTheDocument()
  })

  it('shows the live agent when a session is running', () => {
    renderRow({
      assignedAgentName: 'Antiphon-Opus',
      sessions: [{
        id: 's1',
        definitionName: 'claude',
        agentKind: 'ClaudeCode',
        status: 'Running',
        cwd: 'C:/src/Antiphon',
        createdAt: '2026-08-13T00:00:00Z',
        startedAt: '2026-08-13T00:00:00Z',
        lastSeenAt: '2026-08-13T00:00:00Z',
        endedAt: null,
        exitCode: null,
        failureReason: null,
      }],
    })
    expect(screen.getByText('Antiphon-Opus')).toBeInTheDocument()
  })

  it('opens the card on click, and on Enter for the keyboard', async () => {
    const { onOpen } = renderRow()
    await userEvent.click(screen.getByRole('article', { name: /CARD-0041/ }))
    expect(onOpen).toHaveBeenCalledWith('card-1')

    onOpen.mockClear()
    screen.getByRole('article', { name: /CARD-0041/ }).focus()
    await userEvent.keyboard('{Enter}')
    expect(onOpen).toHaveBeenCalledWith('card-1')
  })

  it('does not open the card when the kebab is used', async () => {
    const { onOpen } = renderRow()
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    expect(onOpen).not.toHaveBeenCalled()
    expect(await screen.findByTestId('move-to-in-progress'))
      .toBeInTheDocument()
  })
})

describe('StateNode', () => {
  function nodeFor(cards: CardDto[], overrides: Partial<BoardColumnDto> = {}) {
    const shape = buildBoardShape(
      {
        id: 'board-1',
        projectId: 'p',
        projectName: 'P',
        name: 'B',
        description: '',
        trackerKind: 'Internal',
        maxConcurrentSessions: 1,
        createdAt: '2026-08-01T00:00:00Z',
        updatedAt: '2026-08-01T00:00:00Z',
        columns: [{ ...columns[0], ...overrides, cards }],
      },
      EMPTY_FILTER,
      NOW,
    )
    return shape.states[0]
  }

  it('leads with the count as a numeral and carries the signal line', () => {
    renderWithProviders(
      <StateNode state={nodeFor([card(), card({ id: 'c2', identifier: 'CARD-0042', priority: 2 })])}
        selected={false}
        filtered={false}
        onSelect={vi.fn()}
      />,
    )
    const node = screen.getByTestId('state-node-backlog')
    expect(within(node).getByText('2')).toBeInTheDocument()
    expect(within(node).getByText('1 P0 · oldest #41 · 3d')).toBeInTheDocument()
  })

  it('keeps rendering when the state is empty — absence is the information', () => {
    renderWithProviders(
      <StateNode state={nodeFor([])} selected={false} filtered={false} onSelect={vi.fn()} />,
    )
    const node = screen.getByTestId('state-node-backlog')
    expect(within(node).getByText('0')).toBeInTheDocument()
    expect(within(node).getByText('—')).toBeInTheDocument()
  })

  it('reports n of m under a filter so the shape stays honest', () => {
    renderWithProviders(
      <StateNode
        state={{ ...nodeFor([card()]), totalCount: 31 }}
        selected={false}
        filtered
        onSelect={vi.fn()}
      />,
    )
    const node = screen.getByTestId('state-node-backlog')
    expect(within(node).getByText('1')).toBeInTheDocument()
    expect(within(node).getByText('of 31')).toBeInTheDocument()
    expect(node).toHaveAccessibleName('Backlog, 1 of 31 cards')
  })

  it('is a lens, not a menu — the whole node is one control', async () => {
    const onSelect = vi.fn()
    renderWithProviders(
      <StateNode state={nodeFor([card()])} selected={false} filtered={false} onSelect={onSelect} />,
    )
    await userEvent.click(screen.getByTestId('state-node-backlog'))
    expect(onSelect).toHaveBeenCalledWith('backlog')
  })
})
