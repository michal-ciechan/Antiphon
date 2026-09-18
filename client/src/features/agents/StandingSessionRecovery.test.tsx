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
  it('deduplicates pending confirmation and refreshes history and attention after acceptance', async () => {
    let release!: () => void
    const pending = new Promise<void>(resolve => { release = resolve })
    let reads = 0
    let starts = 0
    server.use(http.get('/api/agents/standing/sessions', () => {
      reads++
      return HttpResponse.json({ items: [], nextBefore: null })
    }))
    server.use(http.post('/api/agents/standing/start', async () => {
      starts++
      await pending
      return HttpResponse.json(agent)
    }))
    const { queryClient } = renderWithProviders(<StandingSessionRecovery agent={agent} />)
    queryClient.setQueryData(['attention'], { items: [] })
    await userEvent.click(screen.getByRole('button', { name: 'Retry after repair' }))
    await waitFor(() => expect(reads).toBe(1))
    const confirm = screen.getByRole('button', { name: 'Confirm resume' })
    try {
      await userEvent.dblClick(confirm)
      await waitFor(() => expect(starts).toBe(1))
      expect(confirm).toBeDisabled()
      expect(screen.queryByText(/Launch queued/)).not.toBeInTheDocument()
    } finally { release() }
    expect(await screen.findByText(/Launch queued/)).toBeInTheDocument()
    await waitFor(() => expect(reads).toBe(2))
    expect(queryClient.getQueryState(['attention'])?.isInvalidated).toBe(true)
    expect(starts).toBe(1)
  })

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

  // CARD-0511 V-511-16 / G-511-24. The runner-build hold is not a decision: it names the stale
  // build and the command that fixes it, and offers no recovery button.
  it('runner build hold names the stale build and the rebuild command', () => {
    renderWithProviders(<StandingSessionRecovery agent={{
      ...agent, supervision: {
        runnerBuildHeldAt: '2026-09-13T15:55:00Z',
        runnerBuildHoldEvidence: 'The session runner does not advertise sessionGenerationV1 and was built from 9ebbba7 on 2026-09-13 09:00 (running since 09:00). Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.',
      },
    } as AgentSummaryDto} />)
    expect(screen.getByText('Waiting for a rebuilt session runner')).toBeInTheDocument()
    expect(screen.getByText(/built from 9ebbba7/)).toBeInTheDocument()
    expect(screen.getByText('Rebuild the runner: pwsh -File scripts/restart-session-runner.ps1'))
      .toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retry after repair' })).not.toBeInTheDocument()
  })

  // V-511-16c: the continuity hold must not render the runner notice.
  it('no runner build hold renders no runner notice', () => {
    renderWithProviders(<StandingSessionRecovery agent={agent} />)
    expect(screen.queryByText('Waiting for a rebuilt session runner')).not.toBeInTheDocument()
  })

  it('unproven ownership does not claim history was deleted', () => {
    renderWithProviders(<StandingSessionRecovery agent={{ ...agent, supervision: {
      ...agent.supervision!, continuityReason: 'OwnershipUnproven',
    } }} />)
    expect(screen.getByText(/cannot prove ownership/)).toBeInTheDocument()
    expect(screen.queryByText(/provider could not find/)).not.toBeInTheDocument()
  })
})
