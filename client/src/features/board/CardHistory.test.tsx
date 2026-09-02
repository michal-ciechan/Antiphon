import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { BoardColumnDto, CardRevisionDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CardHistory } from './CardHistory'

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
    id: 'column-review',
    stateKey: 'review',
    name: 'Ready for eyes',
    columnOrder: 1,
    cardStatus: 'Review',
    isActive: false,
    isTerminal: false,
    maxConcurrentSessions: null,
    cards: [],
  },
]

function revision(overrides: Partial<CardRevisionDto> & { id: string; revisionNumber: number; kind: CardRevisionDto['kind'] }): CardRevisionDto {
  return {
    cardId: 'card-1',
    title: null,
    description: null,
    importance: null, urgency: null, dueAt: null,
    labels: null,
    fromColumnId: null,
    toColumnId: null,
    fromStatus: null,
    toStatus: null,
    reason: null,
    editedBy: null,
    createdAt: '2026-08-13T09:15:00Z',
    terminalReason: null,
    completedAt: null,
    ...overrides,
  }
}

/**
 * The shape the server actually serves: four kinds INTERLEAVED, newest first, off ONE monotonic
 * revisionNumber. A diff-list rendering would show nothing at all for revisions 5, 3 and 2.
 */
const interleaved: CardRevisionDto[] = [
  revision({
    id: 'r5', revisionNumber: 5, kind: 'Unarchive',
    reason: 'archived by mistake', editedBy: 'operator',
    createdAt: '2026-08-15T11:00:00Z',
  }),
  revision({
    id: 'r4', revisionNumber: 4, kind: 'Archive',
    reason: 'duplicate of CARD-0042', editedBy: 'operator',
    createdAt: '2026-08-14T16:30:00Z',
  }),
  revision({
    id: 'r3', revisionNumber: 3, kind: 'Move',
    fromColumnId: 'column-backlog', toColumnId: 'column-review',
    fromStatus: 'Backlog', toStatus: 'Review',
    reason: 'ready for eyes',
    createdAt: '2026-08-13T09:15:00Z',
  }),
  revision({
    id: 'r2', revisionNumber: 2, kind: 'ContentEdit',
    title: 'Cards cannot be edited',
    description: 'the old body, which can run to twenty thousand characters',
    importance: 'Low', urgency: 'Normal', dueAt: null,
    labels: ['stale-label'],
    reason: 'title named the wrong failure', editedBy: 'Antiphon-Opus',
    createdAt: '2026-08-12T08:00:00Z',
  }),
  revision({
    id: 'r1', revisionNumber: 1, kind: 'Move',
    fromColumnId: 'column-deleted-long-ago', toColumnId: 'column-backlog',
    fromStatus: 'InProgress', toStatus: 'Backlog',
    createdAt: '2026-08-11T07:00:00Z',
  }),
]

function renderHistory(payload: CardRevisionDto[], withColumns = columns) {
  server.use(http.get('/api/cards/card-1/revisions', () => HttpResponse.json(payload)))
  return renderWithProviders(<CardHistory cardId="card-1" columns={withColumns} />)
}

describe('CardHistory', () => {
  it('renders every kind, in the order served, off one sequence', async () => {
    renderHistory(interleaved)

    await waitFor(() => expect(screen.getByTestId('card-history')).toBeInTheDocument())
    const rows = screen.getByTestId('card-history').children
    expect([...rows].map((row) => row.getAttribute('data-testid')))
      .toEqual(['revision-5', 'revision-4', 'revision-3', 'revision-2', 'revision-1'])
  })

  it('gives an archive and an unarchive their own rows, with reason and actor', async () => {
    renderHistory(interleaved)

    const archived = await screen.findByTestId('revision-4')
    expect(archived).toHaveTextContent('Archived')
    expect(archived).toHaveTextContent('duplicate of CARD-0042')
    // Never presented as authenticated — the server has no principals.
    expect(archived).toHaveTextContent('by operator (self-reported)')

    expect(screen.getByTestId('revision-5')).toHaveTextContent('Unarchived')
    expect(screen.getByTestId('revision-5')).toHaveTextContent('archived by mistake')
  })

  it('names a move by its columns, and falls back to statuses when a column is gone', async () => {
    renderHistory(interleaved)

    const moved = await screen.findByTestId('revision-3')
    expect(moved).toHaveTextContent('Moved')
    expect(moved).toHaveTextContent('Backlog → Ready for eyes')
    expect(moved).toHaveTextContent('ready for eyes')

    // A column can be deleted out from under old revisions; the status is what survives.
    expect(screen.getByTestId('revision-1')).toHaveTextContent('InProgress → Backlog')
  })

  it('falls back to statuses for every move on the all-boards view, which has no columns', async () => {
    renderHistory(interleaved, [])

    expect(await screen.findByTestId('revision-3')).toHaveTextContent('Backlog → Review')
  })

  it('keeps the superseded text collapsed until asked — it can be 20,000 characters', async () => {
    renderHistory(interleaved)

    const edited = await screen.findByTestId('revision-2')
    expect(edited).toHaveTextContent('Edited')
    expect(edited).toHaveTextContent('title named the wrong failure')
    // Superseded priority and labels are small enough to show inline.
    expect(edited).toHaveTextContent('Low')
    expect(edited).toHaveTextContent('stale-label')

    expect(screen.queryByTestId('superseded-description-2')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Show superseded text' }))

    expect(screen.getByTestId('superseded-title-2')).toHaveTextContent('Cards cannot be edited')
    expect(screen.getByTestId('superseded-description-2'))
      .toHaveTextContent('the old body, which can run to twenty thousand characters')
  })

  it('renders a reopen with the transition and the superseded close', async () => {
    renderHistory([
      revision({
        id: 'r6', revisionNumber: 6, kind: 'Reopen',
        fromColumnId: 'column-done', toColumnId: 'column-backlog',
        fromStatus: 'Done', toStatus: 'Backlog',
        reason: 'The close was wrong.',
        completedAt: '2026-08-16T14:22:00Z',
        terminalReason: 'Shipped in the parent card.',
        createdAt: '2026-08-17T09:00:00Z',
      }),
    ])

    const reopened = await screen.findByTestId('revision-6')
    expect(reopened).toHaveTextContent('Reopened')
    expect(reopened).toHaveTextContent('Done → Backlog')
    expect(reopened).toHaveTextContent('The close was wrong.')
    expect(screen.getByTestId('superseded-close-6'))
      .toHaveTextContent('was closed 2026-08-16 14:22Z: Shipped in the parent card.')
  })

  it('says so when a card has never moved, been edited or archived', async () => {
    renderHistory([])

    expect(await screen.findByTestId('card-history-empty')).toHaveTextContent('No history yet')
  })
})
