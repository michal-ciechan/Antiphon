import { DndContext } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { Notifications } from '@mantine/notifications'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
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
    importance: 'Critical', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 4,
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

function renderRow(overrides: Partial<CardDto> = {}, onOpen = vi.fn(), reorderable = false) {
  const row = card(overrides)
  renderWithProviders(
    <DndContext>
      <SortableContext items={[row.id]} strategy={verticalListSortingStrategy}>
        <Notifications />
        <CardRow
          card={row}
          boardId="board-1"
          columns={columns}
          now={NOW}
          onOpen={onOpen}
          reorderable={reorderable}
        />
      </SortableContext>
    </DndContext>,
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
    expect(within(row).getByText('Critical')).toBeInTheDocument()
    expect(within(row).getByText('session')).toBeInTheDocument()
    expect(within(row).getByText('reliability')).toBeInTheDocument()
    expect(within(row).queryByText('never-shown')).not.toBeInTheDocument()
    expect(within(row).getByText('3d')).toBeInTheDocument()
  })

  it('drops the description — it was clamp-2 noise at this density', () => {
    renderRow()
    expect(screen.queryByText(/two post-compaction records/)).not.toBeInTheDocument()
  })

  it('shows a review chip next to the GitHub key when the import needs a human rating', () => {
    renderRow({
      externalIssue: {
        trackerKind: 'GitHubIssues',
        key: '#30',
        url: 'https://github.test/acme/app/issues/30',
        author: 'bob',
        authorIsOperator: false,
        needsHumanReview: true,
      },
    })
    const row = screen.getByRole('article', { name: /CARD-0041/ })
    expect(within(row).getByText('GH #30')).toBeInTheDocument()
    expect(within(row).getByText('review')).toBeInTheDocument()
  })

  it('does not show the review chip once the import has been rated', () => {
    renderRow({
      externalIssue: {
        trackerKind: 'GitHubIssues',
        key: '#30',
        url: 'https://github.test/acme/app/issues/30',
        needsHumanReview: false,
      },
    })
    expect(screen.getByText('GH #30')).toBeInTheDocument()
    expect(screen.queryByText('review')).not.toBeInTheDocument()
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

  it('shows a reorder handle when reorderable and hides it on archived rows', () => {
    renderRow({}, vi.fn(), true)
    expect(screen.getByLabelText('Reorder CARD-0041')).toBeInTheDocument()
  })

  it('hides the reorder handle when the row is not reorderable', () => {
    renderRow()
    expect(screen.queryByLabelText('Reorder CARD-0041')).not.toBeInTheDocument()
  })

  it('hides the reorder handle on an archived card even when reorderable', () => {
    renderRow(ARCHIVED, vi.fn(), true)
    expect(screen.queryByLabelText('Reorder CARD-0041')).not.toBeInTheDocument()
  })

  it('does not open the card when the kebab is used', async () => {
    const { onOpen } = renderRow()
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    expect(onOpen).not.toHaveBeenCalled()
    expect(await screen.findByTestId('move-to-in-progress'))
      .toBeInTheDocument()
  })

  it('offers Move to top and Move to bottom on a live non-terminal card', async () => {
    renderRow()
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    expect(await screen.findByTestId('move-to-top')).toHaveTextContent('Move to top')
    expect(screen.getByTestId('move-to-bottom')).toHaveTextContent('Move to bottom')
  })
})

const ARCHIVED = {
  archivedAt: '2026-08-12T09:00:00Z',
  archivedReason: 'duplicate of CARD-0042',
  archivedBy: 'operator',
} satisfies Partial<CardDto>

describe('an archived card', () => {
  it('renders dimmed and badged — visible, but plainly not part of the live board', () => {
    renderRow(ARCHIVED)
    const row = screen.getByRole('article', { name: /CARD-0041/ })
    expect(within(row).getByText('archived')).toBeInTheDocument()
    expect(row).toHaveStyle({ opacity: '0.55' })
  })

  it('offers Unarchive and NO move targets — the server refuses to move an archived card', async () => {
    renderRow(ARCHIVED)
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))

    expect(await screen.findByTestId('unarchive-card')).toBeInTheDocument()
    expect(screen.queryByTestId('move-to-in-progress')).not.toBeInTheDocument()
    expect(screen.queryByTestId('archive-card')).not.toBeInTheDocument()
    // Copy id survives everywhere: an archived card's identifier is exactly what still gets cited.
    expect(screen.getByTestId('copy-card-id')).toBeInTheDocument()
  })
})

describe('archiving from the card actions menu', () => {
  it('requires a reason and POSTs it with the token to /archive', async () => {
    const archiveSpy = vi.fn()
    server.use(http.post('/api/cards/card-1/archive', async ({ request }) => {
      archiveSpy(await request.json())
      return HttpResponse.json({ ...card(), ...ARCHIVED })
    }))
    renderRow()

    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    await userEvent.click(await screen.findByTestId('archive-card'))

    const submit = await screen.findByRole('button', { name: 'Archive' })
    expect(submit).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/^Reason/), 'duplicate of CARD-0042')
    await userEvent.click(submit)

    // POST, never DELETE — archive is not a delete, and the row has to stay.
    await waitFor(() => expect(archiveSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'duplicate of CARD-0042',
      archivedBy: 'operator',
    }))
  })

  it('shows the server\'s refusal verbatim when a session is still live on the card', async () => {
    server.use(http.post('/api/cards/card-1/archive', () =>
      HttpResponse.json({
        title: 'Conflict',
        detail: "Card 'CARD-0041' has a live owner session; stop it before archiving.",
        status: 409,
      }, { status: 409 })))
    renderRow()

    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    await userEvent.click(await screen.findByTestId('archive-card'))
    await userEvent.type(screen.getByLabelText(/^Reason/), 'no longer wanted')
    await userEvent.click(screen.getByRole('button', { name: 'Archive' }))

    // The server's sentence says what to do about it; a generic "Archive failed" would not.
    expect(await screen.findByText("Card 'CARD-0041' has a live owner session; stop it before archiving."))
      .toBeInTheDocument()
  })

  it('warns before the round trip when the card already has a live session', async () => {
    renderRow({
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

    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    await userEvent.click(await screen.findByTestId('archive-card'))

    expect(await screen.findByText(/Stop the session first/)).toBeInTheDocument()
  })

  it('unarchives with its own reason', async () => {
    const unarchiveSpy = vi.fn()
    server.use(http.post('/api/cards/card-1/unarchive', async ({ request }) => {
      unarchiveSpy(await request.json())
      return HttpResponse.json(card())
    }))
    renderRow(ARCHIVED)

    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    await userEvent.click(await screen.findByTestId('unarchive-card'))
    await userEvent.type(screen.getByLabelText(/^Reason/), 'archived by mistake')
    await userEvent.click(screen.getByRole('button', { name: 'Unarchive' }))

    await waitFor(() => expect(unarchiveSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'archived by mistake',
      unarchivedBy: 'operator',
    }))
  })
})

describe('reopening a closed card', () => {
  it('offers Reopen on a Done card and still refuses every move target', async () => {
    renderRow({ status: 'Done', boardColumnId: 'column-done', completedAt: '2026-08-16T14:22:00Z' })
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))

    expect(await screen.findByTestId('reopen-card')).toBeInTheDocument()
    expect(screen.getByTestId('no-move-target')).toBeInTheDocument()
    expect(screen.queryByTestId('move-to-in-progress')).not.toBeInTheDocument()
  })

  it('does not offer Reopen on a live card', async () => {
    renderRow()
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))

    expect(await screen.findByTestId('archive-card')).toBeInTheDocument()
    expect(screen.queryByTestId('reopen-card')).not.toBeInTheDocument()
  })

  it('does not offer Reopen on an archived closed card — unarchive first', async () => {
    renderRow({
      ...ARCHIVED,
      status: 'Done',
      boardColumnId: 'column-done',
      completedAt: '2026-08-16T14:22:00Z',
    })
    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))

    expect(await screen.findByTestId('unarchive-card')).toBeInTheDocument()
    expect(screen.queryByTestId('reopen-card')).not.toBeInTheDocument()
  })

  it('requires a reason and POSTs it with the token to /reopen', async () => {
    const reopenSpy = vi.fn()
    server.use(http.post('/api/cards/card-1/reopen', async ({ request }) => {
      reopenSpy(await request.json())
      return HttpResponse.json(card({ status: 'Backlog' }))
    }))
    renderRow({ status: 'Done', boardColumnId: 'column-done', completedAt: '2026-08-16T14:22:00Z' })

    await userEvent.click(screen.getByLabelText('Actions for CARD-0041'))
    await userEvent.click(await screen.findByTestId('reopen-card'))

    const submit = await screen.findByRole('button', { name: 'Reopen' })
    expect(submit).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/^Reason/), 'The close was wrong.')
    await userEvent.click(submit)

    await waitFor(() => expect(reopenSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'The close was wrong.',
      reopenedBy: 'operator',
    }))
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
      <StateNode state={nodeFor([card(), card({ id: 'c2', identifier: 'CARD-0042', importance: 'Normal', rank: 10, quadrant: 'Someday' })])}
        selected={false}
        filtered={false}
        onSelect={vi.fn()}
      />,
    )
    const node = screen.getByTestId('state-node-backlog')
    expect(within(node).getByText('2')).toBeInTheDocument()
    expect(within(node).getByText('1 Critical · oldest #41 · 3d')).toBeInTheDocument()
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
