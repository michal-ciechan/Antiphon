import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AttentionDto, AttentionItemDto } from '../../api/attention'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DecisionsPanel } from './DecisionsPanel'

const decision = (overrides: Partial<AttentionItemDto> = {}): AttentionItemDto => ({
  kind: 'CardNeedsDecision', severity: 'Critical', taskId: null, sessionId: null, agentId: null,
  messageId: null, cardId: 'card-1', boardId: 'board-1', title: 'CARD-0010 — Choose release train',
  headline: 'Needs a decision', evidence: 'Should this ship before the migration?\nThis is the whole question.',
  sinceUtc: '2026-08-27T09:00:00Z', subtreeCostUsd: null, actions: ['OpenCard'], ...overrides,
})

function serve(items: AttentionItemDto[]) {
  const cards = items
    .filter((item) => item.cardId && item.boardId)
    .map((item) => ({
      id: item.cardId!, boardId: item.boardId!, boardColumnId: 'column-decision', ownerSessionId: null,
      currentWorktreeId: null, assignedAgentId: null, assignedAgentName: null, agentQueuePosition: null,
      activeWorkflowRunId: null, workflowRunStatus: null, currentWorkflowStageName: null,
      identifier: item.title.split(' ')[0], title: item.title.replace(/^.*?\u2014 /, ''), description: '', priority: 1,
      labels: [], status: 'NeedsDecision', concurrencyToken: 'token-1', createdAt: '2026-01-01T00:00:00Z',
      updatedAt: item.sinceUtc, startedAt: null, completedAt: null, terminalReason: null, sessions: [],
      revisionCount: 1, archivedAt: null, archivedReason: null, archivedBy: null,
    }))
  server.use(
    http.get('/api/attention', () => HttpResponse.json<AttentionDto>({ generatedAt: '2026-08-27T12:00:00Z', runnerConsulted: true, items })),
    http.get('/api/boards', () => HttpResponse.json([{
      id: 'board-1', projectId: 'project-1', projectName: 'Antiphon', name: 'Main board', description: '',
      trackerKind: 'Internal', maxConcurrentSessions: 1, cardCount: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
    }])),
    http.get('/api/cards', () => HttpResponse.json({ cards, truncated: false })),
    http.get('/api/boards/board-1/columns', () => HttpResponse.json([
      {
        id: 'column-backlog', stateKey: 'backlog', name: 'Backlog', columnOrder: 0, cardStatus: 'Backlog', isActive: false, isTerminal: false, maxConcurrentSessions: null, cards: [],
      }, {
        id: 'column-decision', stateKey: 'needs-decision', name: 'Needs decision', columnOrder: 0, cardStatus: 'NeedsDecision', isActive: false, isTerminal: false, maxConcurrentSessions: null,
        cards: [],
      },
    ])),
  )
}

describe('DecisionsPanel', () => {
  it('uses the NeedsDecision card list rather than the board-detail fan-out', async () => {
    let requestedStatus: string | null = null
    let boardDetailRequests = 0
    serve([decision()])
    server.use(
      http.get('/api/cards', ({ request }) => {
        requestedStatus = new URL(request.url).searchParams.get('status')
        return HttpResponse.json({ cards: [], truncated: false })
      }),
      http.get('/api/boards/:id', () => {
        boardDetailRequests++
        return HttpResponse.json({})
      }),
    )

    renderWithProviders(<DecisionsPanel />)

    await waitFor(() => expect(requestedStatus).toBe('NeedsDecision'))
    expect(boardDetailRequests).toBe(0)
  })

  it('groups decision cards by board, oldest first, with the whole question unclamped', async () => {
    serve([decision(), decision({ cardId: 'card-2', title: 'CARD-0011 — Second', sinceUtc: '2026-08-27T10:00:00Z' })])
    renderWithProviders(<DecisionsPanel />)

    expect(await screen.findByText('Antiphon / Main board')).toBeInTheDocument()
    expect(screen.getAllByText(/Should this ship before the migration/)[0]).toHaveStyle({ whiteSpace: 'pre-wrap' })
    const rows = screen.getAllByTestId('decision-row')
    expect(rows[0]).toHaveTextContent('CARD-0010')
  })

  it('Open card goes to the board with the card selected', async () => {
    serve([decision()])
    renderWithProviders(<DecisionsPanel />)
    await userEvent.click(await screen.findByRole('button', { name: 'Open card' }))
    await waitFor(() => expect(window.location.pathname + window.location.search).toBe('/boards/board-1?card=card-1'))
  })

  it('Decide opens the move dialog with the reason required and records it as the move reason', async () => {
    const move = vi.fn()
    serve([decision()])
    server.use(http.patch('/api/cards/card-1', async ({ request }) => {
      move(await request.json())
      return HttpResponse.json({ card: {}, spawnedSessionId: null, spawnSuppressed: false })
    }))
    renderWithProviders(<DecisionsPanel />)
    await userEvent.click(await screen.findByRole('button', { name: 'Decide…' }))
    const reason = await screen.findByPlaceholderText('Record the decision')
    expect(reason).toBeRequired()
    await userEvent.type(reason, 'Use the migration train.')
    await userEvent.click(screen.getByRole('button', { name: 'Decide' }))
    await waitFor(() => expect(move).toHaveBeenCalledWith(expect.objectContaining({ reason: 'Use the migration train.' })))
  })

  it('reads as calm with no decisions waiting', async () => {
    serve([])
    renderWithProviders(<DecisionsPanel />)
    expect(await screen.findByText('No decisions waiting.')).toBeInTheDocument()
  })
})
