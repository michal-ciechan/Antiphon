import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AttentionDto, AttentionItemDto } from '../../api/attention'
import { renderWithProviders, screen, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DecisionsBadge } from './DecisionsBadge'

function attention(items: AttentionItemDto[]) {
  server.use(http.get('/api/attention', () => HttpResponse.json<AttentionDto>({
    generatedAt: '2026-08-27T12:00:00Z', runnerConsulted: true, items,
  })))
}

function item(kind: AttentionItemDto['kind']): AttentionItemDto {
  return {
    kind, severity: kind === 'CardNeedsDecision' ? 'Critical' : 'Error', taskId: null, sessionId: null,
    agentId: null, messageId: null, title: 'Row', headline: 'Headline', evidence: 'Evidence',
    sinceUtc: '2026-08-27T11:00:00Z', subtreeCostUsd: null, actions: [], cardId: kind === 'CardNeedsDecision' ? 'card-1' : null,
    boardId: kind === 'CardNeedsDecision' ? 'board-1' : null,
  }
}

describe('DecisionsBadge', () => {
  it('renders nothing when no card needs a decision, even with 36 parked messages open', async () => {
    attention(Array.from({ length: 36 }, () => item('ParkedMessage')))
    renderWithProviders(<DecisionsBadge />)
    await waitFor(() => expect(screen.queryByText(/Decisions/)).not.toBeInTheDocument())
  })

  it('counts only decision rows and links to the decisions tab', async () => {
    attention([item('CardNeedsDecision'), item('CardNeedsDecision'), item('ParkedMessage')])
    renderWithProviders(<DecisionsBadge />)
    const link = await screen.findByRole('link', { name: /Decisions \(2\)/ })
    expect(link).toHaveAttribute('href', '/orchestrator?tab=decisions')
  })
})
