import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AttentionSummaryDto } from '../../api/attention'
import { renderWithProviders, screen, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DecisionsBadge } from './DecisionsBadge'

function summary(open: number, decisions: number) {
  const seen: string[] = []
  server.use(
    http.get('/api/attention/summary', () => {
      seen.push('summary')
      return HttpResponse.json<AttentionSummaryDto>({
        open,
        decisions,
        generatedAt: '2026-08-27T12:00:00Z',
      })
    }),
    http.get('/api/attention', () => {
      seen.push('full')
      return HttpResponse.json({ generatedAt: '2026-08-27T12:00:00Z', runnerConsulted: true, items: [] })
    }),
  )
  return seen
}

describe('DecisionsBadge', () => {
  it('renders nothing when no card needs a decision, even with 36 other open rows', async () => {
    const seen = summary(36, 0)
    renderWithProviders(<DecisionsBadge />)
    await waitFor(() => expect(screen.queryByText(/Decisions/)).not.toBeInTheDocument())
    expect(seen).toContain('summary')
    expect(seen).not.toContain('full')
  })

  it('counts only decision rows and links to the decisions tab', async () => {
    const seen = summary(3, 2)
    renderWithProviders(<DecisionsBadge />)
    const link = await screen.findByRole('link', { name: /Decisions \(2\)/ })
    expect(link).toHaveAttribute('href', '/orchestrator?tab=decisions')
    expect(seen).toContain('summary')
    expect(seen).not.toContain('full')
  })

  it('requests /attention/summary and never the full /attention projection', async () => {
    const seen = summary(1, 1)
    renderWithProviders(<DecisionsBadge />)
    await screen.findByRole('link', { name: /Decisions \(1\)/ })
    expect(seen).toContain('summary')
    expect(seen).not.toContain('full')
  })
})
