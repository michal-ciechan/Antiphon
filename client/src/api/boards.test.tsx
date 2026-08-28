import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import {
  boardKeys,
  CARD_LIMITS,
  reopenCard,
  useArchiveCard,
  useBoard,
  useCard,
  useCardRevisions,
  useMoveCard,
  useReopenCard,
  useUnarchiveCard,
  useUpdateCardContent,
  type BoardDetailDto,
  type CardDto,
  type CardRevisionDto,
} from './boards'

const cardStub: CardDto = {
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
  identifier: 'CARD-0019',
  title: 'Cards cannot be corrected',
  description: 'a record you cannot correct is a record that rots',
  priority: 0,
  labels: ['board'],
  status: 'Backlog',
  concurrencyToken: 'token-1',
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
  startedAt: null,
  completedAt: null,
  terminalReason: null,
  sessions: [],
  revisionCount: 3,
  archivedAt: null,
  archivedReason: null,
  archivedBy: null,
}

const boardStub: BoardDetailDto = {
  id: 'board-1',
  projectId: 'project-1',
  projectName: 'Project One',
  name: 'Delivery',
  description: '',
  trackerKind: 'Internal',
  maxConcurrentSessions: 1,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
  columns: [],
}

describe('CARD_LIMITS', () => {
  it('carries the server constants, because nothing serves them', () => {
    // LOCKSTEP with CardService.MaxTitleLength / MaxDescriptionLength / MaxReasonLength. The
    // reason ceiling in particular was raised from 1,000 to 4,000 after the base spec was written.
    expect(CARD_LIMITS).toEqual({ title: 300, description: 20_000, reason: 4_000 })
  })
})

describe('useUpdateCardContent', () => {
  it('PATCHes /cards/{id}/content and invalidates the board and that card\'s revisions', async () => {
    const patchSpy = vi.fn()
    server.use(
      http.patch('/api/cards/card-1/content', async ({ request }) => {
        patchSpy(await request.json())
        return HttpResponse.json({ ...cardStub, title: 'Corrected' })
      }),
    )

    const { result, queryClient } = renderHookWithProviders(() => useUpdateCardContent('board-1'))
    queryClient.setQueryData(boardKeys.detail('board-1'), boardStub)
    queryClient.setQueryData(boardKeys.cardRevisions('card-1'), [])

    result.current.mutate({
      cardId: 'card-1',
      request: {
        concurrencyToken: 'token-1',
        reason: 'the title described the wrong bug',
        title: 'Corrected',
        editedBy: 'operator',
      },
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(patchSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'the title described the wrong bug',
      title: 'Corrected',
      editedBy: 'operator',
    })

    // The revision list is the one invalidation `useMoveCard` never needed — an edit adds a row
    // to a history the modal may already be showing.
    expect(queryClient.getQueryState(boardKeys.cardRevisions('card-1'))?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(boardKeys.detail('board-1'))?.isInvalidated).toBe(true)
  })
})

describe('useArchiveCard / useUnarchiveCard', () => {
  it('POST to /archive and /unarchive — never DELETE', async () => {
    const archiveSpy = vi.fn()
    const unarchiveSpy = vi.fn()
    server.use(
      http.post('/api/cards/card-1/archive', async ({ request }) => {
        archiveSpy(await request.json())
        return HttpResponse.json({ ...cardStub, archivedAt: '2026-08-15T00:00:00Z' })
      }),
      http.post('/api/cards/card-1/unarchive', async ({ request }) => {
        unarchiveSpy(await request.json())
        return HttpResponse.json(cardStub)
      }),
    )

    const archive = renderHookWithProviders(() => useArchiveCard('board-1'))
    archive.result.current.mutate({
      cardId: 'card-1',
      request: { concurrencyToken: 'token-1', reason: 'duplicate of CARD-0042', archivedBy: 'operator' },
    })
    await waitFor(() => expect(archive.result.current.isSuccess).toBe(true))
    expect(archiveSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'duplicate of CARD-0042',
      archivedBy: 'operator',
    })

    const unarchive = renderHookWithProviders(() => useUnarchiveCard('board-1'))
    unarchive.result.current.mutate({
      cardId: 'card-1',
      request: { concurrencyToken: 'token-1', reason: 'archived by mistake', unarchivedBy: 'operator' },
    })
    await waitFor(() => expect(unarchive.result.current.isSuccess).toBe(true))
    expect(unarchiveSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'archived by mistake',
      unarchivedBy: 'operator',
    })
  })
})

describe('reopenCard', () => {
  it('POSTs /cards/{id}/reopen with the reopen request shape', async () => {
    const reopenSpy = vi.fn()
    server.use(
      http.post('/api/cards/card-1/reopen', async ({ request }) => {
        reopenSpy(await request.json())
        return HttpResponse.json({ ...cardStub, status: 'Backlog' })
      }),
    )

    await reopenCard('card-1', {
      concurrencyToken: 'token-1',
      reason: 'The close was wrong.',
      reopenedBy: 'operator',
    })

    expect(reopenSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'The close was wrong.',
      reopenedBy: 'operator',
    })
  })

  it('sends an optional target column when one is chosen', async () => {
    const reopenSpy = vi.fn()
    server.use(
      http.post('/api/cards/card-1/reopen', async ({ request }) => {
        reopenSpy(await request.json())
        return HttpResponse.json({ ...cardStub, status: 'InProgress' })
      }),
    )

    await reopenCard('card-1', {
      concurrencyToken: 'token-1',
      reason: 'Still in progress.',
      boardColumnId: 'column-active',
    })

    expect(reopenSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'Still in progress.',
      boardColumnId: 'column-active',
    })
  })

  it('invalidates the board and that card\'s revisions through the hook', async () => {
    server.use(
      http.post('/api/cards/card-1/reopen', () =>
        HttpResponse.json({ ...cardStub, status: 'Backlog' })),
    )

    const { result, queryClient } = renderHookWithProviders(() => useReopenCard('board-1'))
    queryClient.setQueryData(boardKeys.detail('board-1'), boardStub)
    queryClient.setQueryData(boardKeys.cardRevisions('card-1'), [])

    result.current.mutate({
      cardId: 'card-1',
      request: { concurrencyToken: 'token-1', reason: 'The close was wrong.' },
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(queryClient.getQueryState(boardKeys.cardRevisions('card-1'))?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(boardKeys.detail('board-1'))?.isInvalidated).toBe(true)
  })
})

describe('useCardRevisions', () => {
  const revisions: CardRevisionDto[] = [
    {
      id: 'rev-4', cardId: 'card-1', revisionNumber: 4, kind: 'Unarchive',
      title: null, description: null, priority: null, labels: null,
      fromColumnId: null, toColumnId: null, fromStatus: null, toStatus: null,
      reason: 'archived by mistake', editedBy: 'operator', createdAt: '2026-08-14T00:00:00Z',
      terminalReason: null, completedAt: null,
    },
    {
      id: 'rev-3', cardId: 'card-1', revisionNumber: 3, kind: 'Archive',
      title: null, description: null, priority: null, labels: null,
      fromColumnId: null, toColumnId: null, fromStatus: null, toStatus: null,
      reason: 'duplicate', editedBy: 'operator', createdAt: '2026-08-13T00:00:00Z',
      terminalReason: null, completedAt: null,
    },
    {
      id: 'rev-2', cardId: 'card-1', revisionNumber: 2, kind: 'Move',
      title: null, description: null, priority: null, labels: null,
      fromColumnId: 'column-backlog', toColumnId: 'column-review',
      fromStatus: 'Backlog', toStatus: 'Review',
      reason: 'ready for eyes', editedBy: null, createdAt: '2026-08-12T00:00:00Z',
      terminalReason: null, completedAt: null,
    },
    {
      id: 'rev-1', cardId: 'card-1', revisionNumber: 1, kind: 'ContentEdit',
      title: 'The old title', description: 'the old description', priority: 2, labels: ['old'],
      fromColumnId: null, toColumnId: null, fromStatus: null, toStatus: null,
      reason: 'title named the wrong file', editedBy: 'operator', createdAt: '2026-08-11T00:00:00Z',
      terminalReason: null, completedAt: null,
    },
  ]

  it('GETs the revision list and parses all four kinds off one monotonic sequence', async () => {
    server.use(http.get('/api/cards/card-1/revisions', () => HttpResponse.json(revisions)))

    const { result } = renderHookWithProviders(() => useCardRevisions('card-1'))

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.map((entry) => entry.kind))
      .toEqual(['Unarchive', 'Archive', 'Move', 'ContentEdit'])
    // Newest first, one sequence across every kind — not a per-kind numbering.
    expect(result.current.data?.map((entry) => entry.revisionNumber)).toEqual([4, 3, 2, 1])
    expect(result.current.data?.[2].fromStatus).toBe('Backlog')
    expect(result.current.data?.[3].title).toBe('The old title')
  })

  it('does not fire while disabled — how the History tab pays only once opened', async () => {
    const getSpy = vi.fn()
    server.use(http.get('/api/cards/card-1/revisions', () => {
      getSpy()
      return HttpResponse.json([])
    }))

    const { result } = renderHookWithProviders(() => useCardRevisions('card-1', false))

    expect(result.current.fetchStatus).toBe('idle')
    expect(getSpy).not.toHaveBeenCalled()
  })
})

describe('useBoard', () => {
  it('asks for archived cards only when told to, under a distinct cache key', async () => {
    const urls: string[] = []
    server.use(http.get('/api/boards/board-1', ({ request }) => {
      urls.push(new URL(request.url).search)
      return HttpResponse.json(boardStub)
    }))

    const plain = renderHookWithProviders(() => useBoard('board-1'))
    await waitFor(() => expect(plain.result.current.isSuccess).toBe(true))
    expect(urls).toEqual([''])
    expect(plain.queryClient.getQueryData(boardKeys.detail('board-1'))).toBeDefined()
    expect(plain.queryClient.getQueryData(boardKeys.detailArchived('board-1'))).toBeUndefined()

    urls.length = 0
    const archived = renderHookWithProviders(() => useBoard('board-1', { includeArchived: true }))
    await waitFor(() => expect(archived.result.current.isSuccess).toBe(true))
    expect(urls).toEqual(['?includeArchived=true'])
    // A sibling key, not a shared one: the two payloads differ.
    expect(archived.queryClient.getQueryData(boardKeys.detailArchived('board-1'))).toBeDefined()
    expect(archived.queryClient.getQueryData(boardKeys.detail('board-1'))).toBeUndefined()
  })

  it('keeps the archived key under the board prefix, so existing invalidations still reach it', () => {
    // Every mutation invalidates `boardKeys.detail(id)`; react-query matches by key PREFIX, so
    // nesting the archived key beneath it is what let slice A add zero invalidation call sites.
    expect(boardKeys.detailArchived('board-1').slice(0, 2)).toEqual([...boardKeys.detail('board-1')])
  })
})

describe('summary and full-card cache boundary', () => {
  it('fetches a full card under its own cache key', async () => {
    const getSpy = vi.fn()
    server.use(http.get('/api/cards/card-1', () => {
      getSpy()
      return HttpResponse.json(cardStub)
    }))

    const { result, queryClient } = renderHookWithProviders(() => useCard('card-1'))
    await waitFor(() => expect(result.current.data?.description).toBe(cardStub.description))

    expect(getSpy).toHaveBeenCalledTimes(1)
    expect(queryClient.getQueryData(boardKeys.card('card-1'))).toEqual(cardStub)
    expect(queryClient.getQueryData(boardKeys.detail('board-1'))).toBeUndefined()
  })

  it('moves a summary card without replacing its preview description', async () => {
    const summary: BoardDetailDto = {
      ...boardStub,
      columns: [
        { id: 'backlog', stateKey: 'backlog', name: 'Backlog', columnOrder: 0, cardStatus: 'Backlog', isActive: false, isTerminal: false, maxConcurrentSessions: null, cards: [{ ...cardStub, description: 'preview…', hasMore: true }] },
        { id: 'review', stateKey: 'review', name: 'Review', columnOrder: 1, cardStatus: 'Review', isActive: false, isTerminal: false, maxConcurrentSessions: null, cards: [] },
      ],
    }
    server.use(http.patch('/api/cards/card-1', () => new Promise((resolve) => {
      setTimeout(() => resolve(HttpResponse.json({ card: cardStub, spawnedSessionId: null, spawnSuppressed: false })), 100)
    })))

    const { result, queryClient } = renderHookWithProviders(() => useMoveCard('board-1'))
    queryClient.setQueryData(boardKeys.detailSummary('board-1'), summary)
    result.current.mutate({ cardId: 'card-1', request: { boardColumnId: 'review', concurrencyToken: 'token-1' } })

    await waitFor(() => {
      const optimistic = queryClient.getQueryData<BoardDetailDto>(boardKeys.detailSummary('board-1'))!
      expect(optimistic.columns[1].cards[0]).toMatchObject({
        description: 'preview…', hasMore: true, boardColumnId: 'review', status: 'Review',
      })
    })
    expect(queryClient.getQueryData(boardKeys.card('card-1'))).toBeUndefined()
  })
})
