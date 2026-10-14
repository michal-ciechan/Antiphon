import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { CardDto, CardRelationshipDto } from '../../api/boards'
import { boardKeys } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CardModal } from './CardModal'

const card: CardDto = {
  id: 'card-1', boardId: 'board-1', boardColumnId: 'backlog', identifier: 'CARD-0001',
  title: 'Blocked work', description: '', ownerSessionId: null, currentWorktreeId: null,
  assignedAgentId: null, assignedAgentName: null, agentQueuePosition: null,
  activeWorkflowRunId: null, workflowRunStatus: null, currentWorkflowStageName: null,
  importance: 'Normal', urgency: 'Normal', dueAt: null, urgentSince: null,
  effectiveUrgency: 'Normal', quadrant: 'Someday', rank: 10, labels: [], status: 'Backlog',
  concurrencyToken: 'token-1', createdAt: '2026-10-14T00:00:00Z', updatedAt: '2026-10-14T00:00:00Z',
  startedAt: null, completedAt: null, terminalReason: null, sessions: [], revisionCount: 0,
  archivedAt: null, archivedReason: null, archivedBy: null,
}

const blocker: CardRelationshipDto = {
  id: 'relationship-1', kind: 'BlockedBy', cardId: 'card-1', relatedCardId: 'blocker-49',
  relatedIdentifier: 'CARD-0049', relatedTitle: 'Ship the prerequisite', relatedBoardId: 'board-1',
  relatedStatus: 'InProgress', relatedArchivedAt: null, resolved: false, reason: null,
  createdBy: null, createdAt: '2026-10-14T00:00:00Z',
}

// Exercise the panel through its actual consumer, including refreshed card tokens after writes.
// `fullCard` answers each full-card GET, so a test can change what the server holds between reads.
function renderPanel(value = card, fullCard: () => CardDto | Promise<CardDto> = () => value) {
  server.use(
    http.get('/api/cards/card-1', async () => HttpResponse.json(await fullCard())),
    http.get('/api/cards/card-1/discussion', () => HttpResponse.json([])),
    http.get('/api/schedules', () => HttpResponse.json({ schedules: [] })),
    http.get('/api/agents/definitions', () => HttpResponse.json({ defaultDefinition: 'claude', definitions: [] })),
  )
  return renderWithProviders(<CardModal boardId="board-1" card={value} opened onClose={() => undefined} />)
}

async function panel() {
  return within(await within(screen.getByTestId('card-detail-sidebar')).findByTestId('card-blockers'))
}

describe('CardBlockers', () => {
  it('adds a blocker with the current token and shows it with its status', async () => {
    let added = false
    const post = vi.fn()
    const remove = vi.fn()
    server.use(
      http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: added ? [blocker] : [], blocks: [] })),
      http.post('/api/cards/card-1/relationships', async ({ request }) => {
        post(await request.json())
        added = true
        return HttpResponse.json({ ...blocker, cardConcurrencyToken: 'token-2' }, { status: 201 })
      }),
      http.post('/api/cards/card-1/relationships/relationship-1/remove', async ({ request }) => {
        remove(await request.json())
        added = false
        return HttpResponse.json({ ...card, concurrencyToken: 'token-3' })
      }),
    )
    const { queryClient } = renderPanel()
    server.use(http.get('/api/cards/card-1', () => HttpResponse.json({ ...card, concurrencyToken: added ? 'token-2' : 'token-1' })))
    // Seed inactive neighbours: writes must stale their independent caches too.
    queryClient.setQueryData(boardKeys.detail('board-1'), {})
    queryClient.setQueryData(boardKeys.cards, {})
    queryClient.setQueryData(boardKeys.cardRevisions('card-1'), [])
    const user = userEvent.setup()
    const view = await panel()
    await user.click(await view.findByRole('textbox', { name: 'Blocker identifier' }))
    await user.paste('CARD-0049')
    await user.click(view.getByRole('button', { name: 'Add blocker' }))
    await waitFor(() => expect(post).toHaveBeenCalledWith({ concurrencyToken: 'token-1', relatedCard: 'CARD-0049', kind: 'BlockedBy' }))
    const row = await view.findByTestId('card-blocker-CARD-0049')
    expect(within(row).getByText('InProgress')).toBeInTheDocument()
    expect(view.getByRole('textbox', { name: 'Blocker identifier' })).toHaveValue('')
    expect(queryClient.getQueryState(boardKeys.detail('board-1'))?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(boardKeys.cards)?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(boardKeys.cardRevisions('card-1'))?.isInvalidated).toBe(true)
    await user.click(within(row).getByRole('button', { name: 'Remove blocker CARD-0049' }))
    await waitFor(() => expect(remove).toHaveBeenCalledWith({ concurrencyToken: 'token-2' }))
    await waitFor(() => expect(view.queryByTestId('card-blocker-CARD-0049')).toBeNull())
  })

  it('the next write carries the token from the add response, not a later card read', async () => {
    let added = false
    let cardReads = 0
    let releaseRead: () => void = () => undefined
    const heldRead = new Promise<void>((resolve) => { releaseRead = resolve })
    const remove = vi.fn()
    server.use(
      http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: added ? [blocker] : [], blocks: [] })),
      http.post('/api/cards/card-1/relationships', () => {
        added = true
        return HttpResponse.json({ ...blocker, cardConcurrencyToken: 'token-2' }, { status: 201 })
      }),
      http.post('/api/cards/card-1/relationships/relationship-1/remove', async ({ request }) => {
        remove(await request.json())
        return HttpResponse.json({ ...card, concurrencyToken: 'token-3' })
      }),
    )
    // Every card read after the first is held open, so token-2 can reach the cache only through
    // the add response (CARD-1256 D4).
    renderPanel(card, async () => {
      cardReads += 1
      if (cardReads > 1) await heldRead
      return card
    })
    const user = userEvent.setup()
    const view = await panel()
    await waitFor(() => expect(cardReads).toBe(1))
    await user.click(await view.findByRole('textbox', { name: 'Blocker identifier' }))
    await user.paste('CARD-0049')
    await user.click(view.getByRole('button', { name: 'Add blocker' }))
    const row = await view.findByTestId('card-blocker-CARD-0049')
    await waitFor(() => expect(cardReads).toBe(2))
    await user.click(within(row).getByRole('button', { name: 'Remove blocker CARD-0049' }))
    await waitFor(() => expect(remove).toHaveBeenCalledWith({ concurrencyToken: 'token-2' }))
    releaseRead()
  })

  it('reloads the card after a stale-token refusal so the retry carries the current token', async () => {
    let held = card
    let cardReads = 0
    const posts: unknown[] = []
    server.use(
      http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: [], blocks: [] })),
      http.post('/api/cards/card-1/relationships', async ({ request }) => {
        const body = await request.json() as { concurrencyToken: string }
        posts.push(body)
        if (body.concurrencyToken !== held.concurrencyToken) {
          return HttpResponse.json({ code: 'conflict', detail: "Card 'CARD-0001' was modified by another operation." }, { status: 409 })
        }
        return HttpResponse.json({ ...blocker, cardConcurrencyToken: 'token-3' }, { status: 201 })
      }),
    )
    renderPanel(card, () => {
      cardReads += 1
      return held
    })
    const user = userEvent.setup()
    const view = await panel()
    await waitFor(() => expect(cardReads).toBe(1))
    // Another writer renames the card after this modal read it, so the panel's token-1 is stale.
    held = { ...card, title: 'Blocked work, renamed elsewhere', concurrencyToken: 'token-2' }
    await user.click(await view.findByRole('textbox', { name: 'Blocker identifier' }))
    await user.paste('CARD-0049')
    await user.click(view.getByRole('button', { name: 'Add blocker' }))
    expect(await view.findByText("Card 'CARD-0001' was modified by another operation.")).toBeInTheDocument()
    expect(posts).toEqual([{ concurrencyToken: 'token-1', relatedCard: 'CARD-0049', kind: 'BlockedBy' }])
    // The refusal reloads the card: the other writer's title is the visible proof that it landed.
    expect(await screen.findByText('Blocked work, renamed elsewhere')).toBeInTheDocument()
    expect(cardReads).toBe(2)
    expect(view.getByRole('textbox', { name: 'Blocker identifier' })).toHaveValue('CARD-0049')
    await user.click(view.getByRole('button', { name: 'Add blocker' }))
    await waitFor(() => expect(posts).toEqual([
      { concurrencyToken: 'token-1', relatedCard: 'CARD-0049', kind: 'BlockedBy' },
      { concurrencyToken: 'token-2', relatedCard: 'CARD-0049', kind: 'BlockedBy' },
    ]))
  })

  it('a resolved blocker is struck through and still removable', async () => {
    let removed = false
    const post = vi.fn()
    server.use(
      http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: removed ? [] : [{ ...blocker, relatedStatus: 'Done', resolved: true }], blocks: [] })),
      http.post('/api/cards/card-1/relationships/relationship-1/remove', async ({ request }) => {
        post(await request.json())
        removed = true
        return HttpResponse.json({ ...card, concurrencyToken: 'token-2' })
      }),
    )
    renderPanel()
    const view = await panel()
    const row = await view.findByTestId('card-blocker-CARD-0049')
    expect(row).toHaveAttribute('data-resolved', 'true')
    expect(within(row).getByText('CARD-0049')).toHaveStyle({ textDecoration: 'line-through' })
    expect(within(row).getByText('Done')).toBeInTheDocument()
    await userEvent.click(within(row).getByRole('button', { name: 'Remove blocker CARD-0049' }))
    await waitFor(() => expect(post).toHaveBeenCalledWith({ concurrencyToken: 'token-1' }))
    await waitFor(() => expect(view.queryByTestId('card-blocker-CARD-0049')).toBeNull())
  })

  it('hides add and remove on an archived card', async () => {
    server.use(http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: [blocker], blocks: [] })))
    renderPanel({ ...card, archivedAt: '2026-10-14T01:00:00Z' })
    const view = await panel()
    expect(await view.findByText('CARD-0049')).toBeInTheDocument()
    expect(view.queryByRole('button', { name: 'Add blocker' })).toBeNull()
    expect(view.queryByRole('button', { name: /Remove blocker/ })).toBeNull()
    expect(view.queryByRole('textbox', { name: 'Blocker identifier' })).toBeNull()
  })

  it('renders the inverse Blocks list without removal controls', async () => {
    server.use(http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: [], blocks: [{ ...blocker, relatedIdentifier: 'CARD-0050', relatedTitle: 'Dependent work' }] })))
    renderPanel()
    const view = await panel()
    expect(await view.findByText('CARD-0050')).toBeInTheDocument()
    expect(view.getByText('Blocks')).toBeInTheDocument()
    expect(view.queryByRole('button', { name: /Remove blocker/ })).toBeNull()
  })

  it('shows the server cycle problem and retains the identifier after a refused add', async () => {
    server.use(
      http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: [], blocks: [] })),
      http.post('/api/cards/card-1/relationships', () => HttpResponse.json({ code: 'card_relationship_cycle', detail: 'Cycle: CARD-0001 -> CARD-0049 -> CARD-0001' }, { status: 409 })),
    )
    renderPanel()
    const user = userEvent.setup()
    const view = await panel()
    await user.click(await view.findByRole('textbox', { name: 'Blocker identifier' }))
    await user.paste('CARD-0049')
    await user.click(view.getByRole('button', { name: 'Add blocker' }))
    expect(await view.findByText('Cycle: CARD-0001 -> CARD-0049 -> CARD-0001')).toBeInTheDocument()
    expect(view.getByRole('textbox', { name: 'Blocker identifier' })).toHaveValue('CARD-0049')
    expect(view.queryByTestId('card-blocker-CARD-0049')).toBeNull()
  })

  it('keeps a blocker visible and shows the server problem after a refused remove', async () => {
    server.use(
      http.get('/api/cards/card-1/relationships', () => HttpResponse.json({ blockedBy: [blocker], blocks: [] })),
      http.post('/api/cards/card-1/relationships/relationship-1/remove', () => HttpResponse.json({ detail: 'The card has changed. Reload it before trying again.' }, { status: 409 })),
    )
    renderPanel()
    const view = await panel()
    const row = await view.findByTestId('card-blocker-CARD-0049')
    await userEvent.click(within(row).getByRole('button', { name: 'Remove blocker CARD-0049' }))
    expect(await view.findByText('The card has changed. Reload it before trying again.')).toBeInTheDocument()
    expect(row).toBeInTheDocument()
  })
})
