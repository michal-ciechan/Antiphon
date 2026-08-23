import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AttentionDto, AttentionItemDto, AttentionKind } from '../../api/attention'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AttentionPanel } from './AttentionPanel'

import { vi } from 'vitest'
vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

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

  it('draws a BriefUndelivered row with the waiting-brief badge', async () => {
    serve({
      items: [
        item({
          kind: 'BriefUndelivered',
          severity: 'Warning',
          title: 'Reuse onto a warm Codex',
          taskId: 't1',
          headline: 'Brief still queued Pending after 12m; the session is working.',
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByText('Reuse onto a warm Codex')).toBeInTheDocument()
    expect(screen.getByText('Brief waiting')).toBeInTheDocument()
    expect(screen.getByText(/Brief still queued Pending/)).toBeInTheDocument()
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

  // ---- slice 4: the actions ---------------------------------------------------------------------

  it('sends a parked message now, at the manual endpoint that bypasses parking', async () => {
    // CARD-0055 parks a message after it spends its delivery attempts and every AUTOMATIC path then
    // excludes it — deliberately. The manual send is the only thing left that moves it, and until
    // this slice there was no button anywhere that reached it.
    const sent: string[] = []
    serve({
      items: [
        item({
          kind: 'ParkedMessage',
          severity: 'Critical',
          title: 'Parked message to Family',
          sessionId: 'sess-1',
          messageId: 'msg-1',
          actions: ['SendNow', 'CancelMessage'],
        }),
      ],
    })
    server.use(
      http.post('/api/sessions/sess-1/messages/msg-1/send-now', () => {
        sent.push('send-now')
        return HttpResponse.json({ sessionId: 'sess-1', messages: [], working: false })
      }),
    )

    renderWithProviders(<AttentionPanel />)
    await userEvent.click(await screen.findByRole('button', { name: 'Send now' }))

    await waitFor(() => expect(sent).toEqual(['send-now']))
  })

  it('drops a parked message at the message endpoint, not the task one', async () => {
    const dropped: string[] = []
    serve({
      items: [
        item({
          kind: 'ParkedMessage',
          severity: 'Error',
          title: 'Parked message to Family',
          sessionId: 'sess-1',
          messageId: 'msg-1',
          actions: ['SendNow', 'CancelMessage'],
        }),
      ],
    })
    server.use(
      http.delete('/api/sessions/sess-1/messages/msg-1', () => {
        dropped.push('cancel')
        return HttpResponse.json({ sessionId: 'sess-1', messages: [], working: false })
      }),
    )

    renderWithProviders(<AttentionPanel />)
    await userEvent.click(await screen.findByRole('button', { name: 'Drop message' }))

    await waitFor(() => expect(dropped).toEqual(['cancel']))
  })

  it('answers a blocked delegate in place instead of sending the human to the drawer', async () => {
    const bodies: unknown[] = []
    serve({
      items: [
        item({
          kind: 'BlockedQuestion',
          severity: 'Critical',
          title: 'Which branch?',
          taskId: 'task-1',
          actions: ['Reply', 'Cancel', 'Escalate'],
        }),
      ],
    })
    server.use(
      http.post('/api/agent-tasks/task-1/reply', async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json({ id: 'task-1', status: 'Working' })
      }),
    )

    renderWithProviders(<AttentionPanel />)
    await userEvent.click(await screen.findByRole('button', { name: 'Answer it' }))
    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Answer the delegate' }),
      'land it on master',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Send answer' }))

    await waitFor(() => expect(bodies).toEqual([{ message: 'land it on master' }]))
  })

  it('leads a past-expected row with reading it, not with retrying it', async () => {
    // The ordering is the server's and it matters most here: a task that is merely finishing quietly
    // is the commonest thing on this list, and a Retry in the primary slot would put a second agent
    // on work that was about to report. The first button reads it; nothing here retries by reflex.
    serve({
      items: [
        item({
          kind: 'PastExpectedIdle',
          severity: 'Warning',
          title: 'Migrate the board API',
          taskId: 'task-9',
          actions: ['OpenDrawer', 'Retry', 'Cancel', 'Escalate'],
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    const row = await screen.findByTestId('attention-row-PastExpectedIdle')
    // The row body is itself a button (it navigates); the verbs are the ones without an aria-label.
    const labels = within(row)
      .getAllByRole('button')
      .filter((node) => !node.getAttribute('aria-label'))
      .map((node) => node.textContent)
    expect(labels[0]).toBe('Read it first')
    expect(labels).toContain('Retry')
  })

  it('never kills a session on one click — the confirm names the session', async () => {
    // The 2026-08-16 miss (CARD-0056) was a HEALTHY session the database had written off: the
    // operator's own working conversation. A one-click kill on this row would have ended it
    // mid-sentence, so the dialog has to say which session, and offer leaving it alone.
    const killed: string[] = []
    serve({
      items: [
        item({
          kind: 'SessionDisagreement',
          severity: 'Error',
          title: 'Antiphon-Opus',
          sessionId: 'cefed08a-1111-2222-3333-444444444444',
          evidence: 'Running since 2026-08-14 as pid 4120, in C:\\src\\Antiphon.',
          actions: ['KillSession'],
        }),
      ],
    })
    server.use(
      http.post('/api/sessions/cefed08a-1111-2222-3333-444444444444/kill', () => {
        killed.push('kill')
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderWithProviders(<AttentionPanel />)
    await userEvent.click(await screen.findByRole('button', { name: 'Kill session' }))

    // Nothing has been killed yet, and the dialog names the session it would end.
    expect(killed).toEqual([])
    expect(await screen.findByText('Kill session cefed08a?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Leave it running' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Kill session cefed08a' }))
    await waitFor(() => expect(killed).toEqual(['kill']))
  })

  it('offers no verb it has no subject for', async () => {
    // The server names actions and ids in separate fields. A Retry button on a row with no taskId
    // would fail on click with a URL containing "null" — worse than not offering it.
    serve({
      items: [
        item({
          kind: 'RecentCriticalIncident',
          severity: 'Error',
          title: 'axc',
          agentId: 'agent-1',
          actions: ['OpenAgent', 'Retry'],
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    const row = await screen.findByTestId('attention-row-RecentCriticalIncident')
    expect(within(row).getByRole('button', { name: 'Open agent' })).toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })

  it('shows the spend even when it is zero', async () => {
    // $0 on a NeverStarted row is not noise — it is the row confirming the delegate never ran.
    serve({
      items: [
        item({ kind: 'NeverStarted', severity: 'Error', taskId: 't1', subtreeCostUsd: 0 }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByText('$0')).toBeInTheDocument()
  })

  // ---- slice 5: the interpreter's reading -------------------------------------------------------

  it('renders the check interpreter’s reading verbatim, line breaks and all', async () => {
    // Slice 5 stores the specialist's 3-5 lines on the Check event, so the server can send THAT as
    // the evidence instead of six lines of raw counters. The panel must not reflow it: the reading's
    // shape is how it is read.
    serve({
      items: [
        item({
          kind: 'ChecksSpent',
          severity: 'Warning',
          title: 'Sweep the logs',
          taskId: 't1',
          evidence:
            'The last check read it as:\nSTALLED — three commits, then 40 minutes of nothing.\nThe delegate is idle at the prompt with a finished branch.',
          actions: ['OpenDrawer'],
        }),
      ],
    })

    renderWithProviders(<AttentionPanel />)

    expect(await screen.findByText(/STALLED — three commits/)).toBeInTheDocument()
    expect(screen.getByText(/The last check read it as:/)).toBeInTheDocument()
  })
})
