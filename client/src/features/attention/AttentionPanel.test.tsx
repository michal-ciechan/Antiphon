import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AttentionDto, AttentionItemDto, AttentionKind } from '../../api/attention'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AttentionPanel } from './AttentionPanel'

// Mantine tooltips and collapses on a loaded machine overrun the 5s default.
import { vi } from 'vitest'
vi.setConfig({ testTimeout: 20_000 })

function item(overrides: Partial<AttentionItemDto> & { kind: AttentionKind }): AttentionItemDto {
  return {
    severity: 'Warning',
    taskId: null,
    sessionId: null,
    agentId: null,
    messageId: null,
    title: overrides.kind,
    headline: 'something happened',
    evidence: '',
    sinceUtc: '2026-08-17T09:00:00Z',
    subtreeCostUsd: null,
    actions: [],
    ...overrides,
  }
}

function serve(payload: Partial<AttentionDto> & { items: AttentionItemDto[] }) {
  server.use(
    http.get('/api/attention', () =>
      HttpResponse.json<AttentionDto>({
        generatedAt: '2026-08-17T10:00:00Z',
        runnerConsulted: true,
        ...payload,
      }),
    ),
  )
}

describe('AttentionPanel', () => {
  it('reads as reassurance when nothing is stuck', async () => {
    // THE common case, and the one that decides whether anybody keeps opening this tab. A blank
    // panel is indistinguishable from a broken one; this must say plainly that it looked and found
    // nothing, and name the exclusion so a visibly-busy fleet does not look like a missed row.
    serve({ items: [] })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByText('Nothing is stuck.')).toBeInTheDocument()
    expect(screen.getByText(/merely slow is deliberately not listed/)).toBeInTheDocument()
    expect(screen.getByText('0 open')).toBeInTheDocument()
  })

  it('groups rows by severity, most urgent first', async () => {
    serve({
      items: [
        item({ kind: 'BlockedQuestion', severity: 'Critical', title: 'Which branch?', taskId: 't1' }),
        item({ kind: 'DeadSession', severity: 'Error', title: 'Ship the upgrade', taskId: 't2' }),
        item({ kind: 'ChecksSpent', severity: 'Warning', title: 'Sweep the logs', taskId: 't3' }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    await screen.findByText('Which branch?')
    const headings = screen
      .getAllByText(/Needs you now|Broken|Suspect/)
      .map((node) => node.textContent)
    expect(headings).toEqual(['Needs you now', 'Broken', 'Suspect'])
    expect(screen.getByText('3 open')).toBeInTheDocument()
  })

  it('never lists a working session, because the server never sends one', async () => {
    // The product constraint, held at the client end: a task four hours into a thirty-minute
    // estimate whose session is mid-turn is absent from the payload, and the panel's answer to an
    // empty payload has to be "nothing is stuck" — not a placeholder, not an error. A client that
    // invented a row from any other signal would be re-deriving stuckness from a screen.
    serve({ items: [] })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByTestId('attention-empty')).toBeInTheDocument()
    expect(screen.queryByTestId('attention-row-PastExpectedIdle')).not.toBeInTheDocument()
  })

  it('shows the evidence, the age and the spend on the row', async () => {
    serve({
      items: [
        item({
          kind: 'PastExpectedIdle',
          severity: 'Warning',
          title: 'Migrate the board API',
          taskId: 't1',
          headline: 'Idle at the prompt after 3h20m against a 30m estimate.',
          evidence: 'GIT: commits=3 changed=1 untracked=0',
          subtreeCostUsd: 1.25,
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByText(/Idle at the prompt after 3h20m/)).toBeInTheDocument()
    expect(screen.getByText(/commits=3/)).toBeInTheDocument()
    expect(screen.getByText('$1.25')).toBeInTheDocument()
    expect(screen.getByText('Idle past estimate')).toBeInTheDocument()
  })

  it('opens the task drawer on the sibling tab when a task row is clicked', async () => {
    serve({
      items: [
        item({
          kind: 'BlockedQuestion',
          severity: 'Critical',
          title: 'Which branch should this land on?',
          taskId: '11111111-1111-1111-1111-111111111111',
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)
    await userEvent.click(await screen.findByRole('button', { name: /Open Which branch/ }))

    await waitFor(() => {
      const params = new URLSearchParams(window.location.search)
      expect(params.get('tab')).toBe('delegations')
      expect(params.get('task')).toBe('11111111-1111-1111-1111-111111111111')
    })
  })

  it('collapses recent failures and keeps them out of the open count', async () => {
    // Failures are context. Counted with the rest they would make a healthy fleet look busy every
    // day, and the badge would stop meaning "somebody has to do something".
    serve({
      items: [
        item({ kind: 'BlockedQuestion', severity: 'Critical', title: 'Which branch?', taskId: 't1' }),
        item({ kind: 'RecentFailure', severity: 'Warning', title: 'A task that died', taskId: 't2' }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    await screen.findByText('Which branch?')
    expect(screen.getByText('1 open')).toBeInTheDocument()
    expect(screen.getByText('Recent failures')).toBeInTheDocument()
    await userEvent.click(screen.getByText('Recent failures'))
    expect(await screen.findByText('A task that died')).toBeInTheDocument()
  })

  it('says the runner was not consulted rather than implying nothing disagrees', async () => {
    // "Nobody asked" is a strictly weaker claim than "nothing disagrees", and collapsing the two
    // would let a runner that is down read as a clean bill of health.
    serve({ runnerConsulted: false, items: [] })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByText('Runner not consulted')).toBeInTheDocument()
  })

  it('renders a session-only row inert instead of navigating nowhere', async () => {
    serve({
      items: [
        item({
          kind: 'ParkedMessage',
          severity: 'Error',
          title: 'Parked message to session 8f21ac0d',
          sessionId: 's1',
          messageId: 'm1',
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByTestId('attention-row-ParkedMessage')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Open Parked message/ })).not.toBeInTheDocument()
  })
})
