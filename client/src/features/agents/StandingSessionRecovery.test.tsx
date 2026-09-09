import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AgentSummaryDto } from '../../api/agents'
import { server } from '../../test/mocks/server'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { StandingSessionRecovery } from './StandingSessionRecovery'

const agent = {
  id: 'standing', name: 'Standing agent', persistentSessionId: 'current', liveSession: null,
  supervision: { continuityHeldAt: '2026-09-09', continuitySessionId: 'current', continuityReason: 'NativeSessionMissing' },
} as AgentSummaryDto

function history() {
  server.use(http.get('/api/agents/standing/sessions', () => HttpResponse.json({ items: [{
    id: 'owned-history', kind: 'ClaudeCode', cwd: 'C:\\standing', status: 'Stopped', eligible: true,
    createdAt: '2026-09-08', ownershipEvidence: 'Stamped', refusalCode: null,
  }], nextBefore: null })))
}

describe('standing conversation recovery', () => {
  it('opening history only reads and keeps ineligible history inspectable', async () => {
    const requests: unknown[] = []
    server.use(http.get('/api/agents/standing/sessions', () => HttpResponse.json({ items: [{
      id: 'busy-history', kind: 'ClaudeCode', cwd: 'C:\\standing', status: 'Starting', eligible: false,
      createdAt: '2026-09-08', ownershipEvidence: 'Stamped', refusalCode: 'standing_resume_target_active',
    }], nextBefore: null })))
    server.use(http.post('/api/agents/standing/start', async ({ request }) => {
      requests.push(await request.json())
      return HttpResponse.json(agent)
    }))
    renderWithProviders(<StandingSessionRecovery agent={agent} />)
    await userEvent.click(screen.getByRole('button', { name: 'Resume previous conversation' }))
    expect(await screen.findByRole('button', { name: 'Select busy-history' })).toBeDisabled()
    expect(screen.getByRole('link', { name: 'Open busy-history' })).toHaveAttribute('href', '/sessions/busy-history')
    expect(requests).toEqual([])
  })

  it.each([
    ['Retry after repair', { retryContinuity: true }],
    ['Start fresh', { fresh: true }],
    ['Resume previous conversation', { resumeSessionId: 'owned-history' }],
  ])('%s sends only the explicit decision after confirmation', async (action, expected) => {
    history()
    const requests: unknown[] = []
    server.use(http.post('/api/agents/standing/start', async ({ request }) => {
      requests.push(await request.json())
      return HttpResponse.json(agent)
    }))
    renderWithProviders(<StandingSessionRecovery agent={agent} />)
    await userEvent.click(screen.getByRole('button', { name: action as string }))
    if (action === 'Resume previous conversation') {
      await userEvent.click(await screen.findByRole('button', { name: 'Select owned-history' }))
    }
    expect(requests).toEqual([])
    if (action === 'Start fresh') expect(screen.getByText(/histories will remain separate/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /Confirm / }))
    await waitFor(() => expect(requests).toEqual([expected]))
    expect(await screen.findByText(/Launch queued/)).toBeInTheDocument()
  })

  it('refusal retains the hold and never retries as fresh', async () => {
    history()
    const requests: unknown[] = []
    server.use(http.post('/api/agents/standing/start', async ({ request }) => {
      requests.push(await request.json())
      return HttpResponse.json({ title: 'Refused', detail: 'Repair configuration', code: 'standing_resume_incompatible' }, { status: 409 })
    }))
    renderWithProviders(<StandingSessionRecovery agent={agent} />)
    await userEvent.click(screen.getByRole('button', { name: 'Retry after repair' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm resume' }))
    expect(await screen.findByText(/Repair configuration/)).toBeInTheDocument()
    expect(requests).toEqual([{ retryContinuity: true }])
    expect(screen.getByText('Conversation recovery needs a decision')).toBeInTheDocument()
  })

  it('unproven ownership does not claim history was deleted', () => {
    renderWithProviders(<StandingSessionRecovery agent={{ ...agent, supervision: {
      ...agent.supervision!, continuityReason: 'OwnershipUnproven',
    } }} />)
    expect(screen.getByText(/cannot prove ownership/)).toBeInTheDocument()
    expect(screen.queryByText(/provider could not find/)).not.toBeInTheDocument()
  })
})
