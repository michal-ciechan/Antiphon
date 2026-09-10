import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { TaskDrawer } from './TaskDrawer'
import type { HubConnection } from '@microsoft/signalr'
import { act } from '@testing-library/react'
import { renderHookWithProviders } from '../../test/utils'
import { useSignalRInvalidation } from '../../hooks/useSignalRInvalidation'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const TASK_ID = '77777777-7777-7777-7777-777777777777'

it('invalidates land detail after AgentTaskChanged', () => {
  const callbacks = new Map<string, (payload: object) => void>()
  const connection = { on: (name: string, fn: (payload: object) => void) => callbacks.set(name, fn), off: vi.fn() }
  const view = renderHookWithProviders(() => useSignalRInvalidation({ current: connection as unknown as HubConnection }))
  const key = ['agentTasks', 'detail', TASK_ID]
  view.queryClient.setQueryData(key, { landRequest: { state: 'Held' } })
  act(() => callbacks.get('AgentTaskChanged')!({ taskId: TASK_ID }))
  expect(view.queryClient.getQueryState(key)?.isInvalidated).toBe(true)
  view.unmount()
})

function detail(overrides: Partial<AgentTaskSummaryDto> = {}, extra: Partial<AgentTaskDetailDto> = {}): AgentTaskDetailDto {
  const summary: AgentTaskSummaryDto = {
    id: TASK_ID,
    rootTaskId: TASK_ID,
    parentTaskId: null,
    depth: 0,
    title: 'Find out why the suite hangs',
    kind: 'Worker',
    role: 'Debug',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Failed',
    workspace: 'Shared',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scope: null,
    agentId: 'agent-9',
    agentName: 'task-77777777',
    agentSessionId: 'session-9',
    attempt: 1,
    createdAt: '2026-08-07T10:00:00Z',
    dispatchedAt: '2026-08-07T10:00:00Z',
    completedAt: '2026-08-07T10:12:00Z',
    recoveredAt: null,
    tokensIn: 12_000,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 800,
    costUsd: 0.031,
    costPricingVersion: 2,
    subtreeCostUsd: 0.031,
    worktreePath: null,
    worktreeBranch: null,
    childCount: 0,
    expectedDurationMinutes: 10,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
  return {
    summary,
    goal: 'Work out why Antiphon.Tests hangs on CI.',
    result: null,
    resultFilePath: null,
    failureReason: 'Ran out of time without reproducing.',
    mergeTargetRef: null,
    events: [{ type: 'Created', modelLevel: summary.modelLevel, detail: 'Created.', at: summary.createdAt }],
    ...extra,
  }
}

function serve(body: AgentTaskDetailDto, extra: Parameters<typeof server.use> = []) {
  server.use(
    http.get('/api/agent-tasks/:id', () => HttpResponse.json(body)),
    http.get('/api/model-availability', () =>
      HttpResponse.json({ holds: [], available: ['fable', 'opus', 'grok-4.6'] }),
    ),
    ...extra,
  )
}

describe('TaskDrawer', () => {
  const land = (overrides: Partial<NonNullable<AgentTaskDetailDto['landRequest']>> = {}): NonNullable<AgentTaskDetailDto['landRequest']> => ({
    id: TASK_ID, state: 'Held', requestedAt: '2026-09-09T10:00:00Z', startedAt: null,
    lastEvaluatedAt: '2026-09-09T10:05:00Z', lastProgressAt: '2026-09-09T10:00:00Z', ageSeconds: 300,
    noProgressSeconds: 300, attempt: 0, holdReasonCode: 'repository_or_source_writer', holdDetail: 'Waiting for blocked writer',
    holdingTaskId: 'holder-9', holdingTaskStatus: 'Blocked', heldSince: '2026-09-09T10:00:00Z', holdEpisode: 1,
    reconciliationError: null, notifications: [], ...overrides,
  })

  it('shows a blocked land separately from delegate success', async () => {
    serve(detail({ status: 'Succeeded', workspace: 'Worktree' }, { landRequest: land() }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)
    expect(await screen.findByText('Delegate: Succeeded')).toBeVisible()
    expect(screen.getByText('Land: Held; attempt 0')).toBeVisible()
    expect(screen.getByText(/holder holder-9 \(Blocked\)/)).toBeVisible()
    expect(screen.getByText('Publication: Unconfirmed; cleanup: NotStarted')).toBeVisible()
  })

  it('keeps published evidence during a held cleanup retry', async () => {
    serve(detail({ status: 'Succeeded', workspace: 'Worktree' }, { landRequest: land(), landing: {
      operationId: TASK_ID, phase: 'Complete', mode: 'CleanupRetry', publication: 'Landed', cleanup: 'Refused',
      sourceSha: 'a'.repeat(40), verifiedSha: 'a'.repeat(40), remoteSha: 'a'.repeat(40),
      remoteConfirmedAt: '2026-09-09T09:00:00Z', destinationRef: 'refs/heads/master', reason: 'residue',
    } }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)
    expect(await screen.findByText('Publication: Landed')).toBeVisible()
    expect(screen.getByText('Land: Held; attempt 0')).toBeVisible()
    expect(screen.getByText('Cleanup: Refused')).toBeVisible()
  })

  it('shows unconfirmed receipt and destination errors', async () => {
    serve(detail({ status: 'Succeeded', workspace: 'Worktree' }, { landRequest: land({ notifications: [{
      id: 'note-1', kind: 'Outcome', state: 'DestinationUnavailable', destinationSessionId: null, queueMessageId: null,
      lastErrorCode: 'destination_unavailable', confirmedAt: null, confirmingPromptSequence: null,
    }] }) }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)
    expect(await screen.findByText(/Outcome DestinationUnavailable/)).toHaveTextContent('receipt unconfirmed; destination_unavailable')
  })

  it('opens land task holder caller and queue without mutation', async () => {
    serve(detail({ status: 'Succeeded', workspace: 'Worktree' }, { landRequest: land({ notifications: [{
      id: 'note-1', kind: 'Outcome', state: 'AwaitingReceipt', destinationSessionId: 'caller-1', queueMessageId: 'queue-1',
      lastErrorCode: null, confirmedAt: null, confirmingPromptSequence: null,
    }] }) }), [http.get('/api/sessions/caller-1/messages', () => HttpResponse.json({ sessionId: 'caller-1', working: true,
      messages: [{ id: 'queue-1', body: 'immutable outcome evidence', status: 'Pending', deliveryAttempts: 0 }] }))])
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)
    expect(await screen.findByRole('link', { name: 'Open holding task' })).toHaveAttribute('href', '/orchestrator?tab=delegations&task=holder-9')
    expect(screen.getByRole('button', { name: 'Open caller transcript' })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Inspect queued note' }))
    expect(await screen.findByText('immutable outcome evidence')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Send now' })).not.toBeInTheDocument()
  })

  it.each(['Unconfirmed', 'Landed', 'AlreadyPresent'] as const)('renders %s evidence through a cleanup update', async (publication) => {
    serve(detail({ status: 'Succeeded', workspace: 'Worktree' }, {
      landing: {
        operationId: TASK_ID, phase: 'CleanupStarted', mode: 'CleanupRetry',
        publication, cleanup: 'Refused', sourceSha: 'a'.repeat(40),
        verifiedSha: 'b'.repeat(40), remoteSha: publication === 'Unconfirmed' ? null : 'c'.repeat(40),
        remoteConfirmedAt: publication === 'Unconfirmed' ? null : '2026-09-08T10:00:00Z',
        destinationRef: 'refs/heads/master', reason: 'source_changed',
      },
      events: [{ type: 'LandingCleanup', modelLevel: null, detail: 'cleanup retained',
        at: '2026-09-08T12:00:00Z', landingOperationId: TASK_ID,
        landingPublication: publication, landingCleanup: 'Refused', landingMode: 'CleanupRetry' }],
    }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)
    expect(await screen.findByText(`Publication: ${publication}`)).toBeVisible()
    expect(screen.getByText('Cleanup: Refused')).toBeVisible()
    expect(screen.getByText('Mode: CleanupRetry')).toBeVisible()
  })

  it('shows publication and cleanup independently from legacy timeline prose', async () => {
    serve(detail({ status: 'Succeeded', workspace: 'Worktree' }, {
      landing: {
        operationId: TASK_ID, phase: 'CleanupStarted', mode: 'CleanupRetry',
        publication: 'AlreadyPresent', cleanup: 'Refused', sourceSha: 'a'.repeat(40),
        verifiedSha: 'b'.repeat(40), remoteSha: 'c'.repeat(40),
        remoteConfirmedAt: '2026-09-08T10:00:00Z', destinationRef: 'refs/heads/master',
        reason: 'ignored_content_preserved',
      },
      events: [{ type: 'Landed', modelLevel: null, detail: 'legacy pushed and cleaned', at: '2026-08-01T00:00:00Z' }],
    }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)
    expect(await screen.findByText('Publication: AlreadyPresent')).toBeVisible()
    expect(screen.getByText('Cleanup: Refused')).toBeVisible()
    expect(screen.getByText('Mode: CleanupRetry')).toBeVisible()
    expect(screen.getByText('ignored_content_preserved')).toBeVisible()
    expect(screen.getByText('c'.repeat(40))).toBeVisible()
  })

  it('shows the distilled section and flags a lost distillation', async () => {
    serve(
      detail(
        { status: 'Succeeded' },
        {
          result: 'The full report with every identifier.',
          distilledResult: '- Landed CARD-0330.\n- next: review',
        },
      ),
      [
        http.post('/api/agent-tasks/:id/distillation/feedback', async ({ request }) => {
          const body = (await request.json()) as { verdict: string }
          expect(body.verdict).toBe('Lost')
          return new HttpResponse(null, { status: 204 })
        }),
      ],
    )

    const user = userEvent.setup()
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByTestId('task-distilled')).toHaveTextContent('CARD-0330')
    await user.click(screen.getByTestId('distill-lost'))
    await waitFor(() => expect(screen.getByTestId('distill-lost')).toBeEnabled())
  })

  it('shows next and handoff under the deliverable', async () => {
    serve(
      detail(
        { role: 'Investigate', status: 'Succeeded' },
        {
          deliverablePath: 'docs/investigations/example.md',
          nextStage: 'Plan',
          nextHandoff: 'root cause confirmed - fix belongs in the probe',
          result: 'Found it.',
          failureReason: null,
        },
      ),
    )
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByTestId('task-deliverable')).toHaveTextContent(
      'docs/investigations/example.md',
    )
    expect(screen.getByTestId('task-next-stage')).toHaveTextContent('next: plan')
    expect(screen.getByTestId('task-next-handoff')).toHaveTextContent(
      'handoff: root cause confirmed - fix belongs in the probe',
    )
  })

  it('shows what the task cost and what stopped it', async () => {
    serve(detail())
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByText('Ran out of time without reproducing.')).toBeInTheDocument()
    expect(screen.getByText('task-77777777')).toBeInTheDocument()
    expect(screen.getByText('$0.03')).toBeInTheDocument()
    expect(screen.getByText('12m00')).toBeInTheDocument()
  })

  it('labels a recovered elapsed time as unobserved', async () => {
    serve(detail({ recoveredAt: '2026-08-07T10:12:00Z' }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    const elapsed = await screen.findByText('~12m00')
    await userEvent.hover(elapsed)
    expect(await screen.findByText(/recovered from an unbound session - completion was not observed/i)).toBeInTheDocument()
  })

  it('retries at the same tier', async () => {
    let posted: string | null = null
    serve(detail(), [
      http.post('/api/agent-tasks/:id/retry', ({ params }) => {
        posted = String(params.id)
        return HttpResponse.json(detail().summary)
      }),
    ])
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(posted).toBe(TASK_ID))
  })

  it('escalates without naming a tier — the ladder is the server’s decision', async () => {
    let body: unknown = undefined
    serve(detail(), [
      http.post('/api/agent-tasks/:id/escalate', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(detail({ modelLevel: 'Frontier', escalatedFrom: 'High' }).summary)
      }),
    ])
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Escalate' }))

    await waitFor(() => expect(body).toEqual({ modelLevel: null }))
  })

  it('does not offer an escalation from the top of the ladder', async () => {
    serve(detail({ modelLevel: 'Frontier' }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByRole('button', { name: 'Escalate' })).toBeDisabled()
  })

  it('does not offer a retry for a task that has not run yet', async () => {
    serve(detail({ status: 'Queued', dispatchedAt: null, completedAt: null }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByRole('button', { name: 'Retry' })).toBeDisabled()
  })

  it('cancels a running task and closes', async () => {
    let posted = false
    let closed = false
    serve(detail({ status: 'Working', completedAt: null }), [
      http.post('/api/agent-tasks/:id/cancel', () => {
        posted = true
        return HttpResponse.json(detail({ status: 'Canceled' }).summary)
      }),
    ])
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => { closed = true }} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(posted).toBe(true))
    await waitFor(() => expect(closed).toBe(true))
  })

  it('answers a blocked delegate instead of taking the work back', async () => {
    // The whole point of Blocked: the delegate keeps its context and carries on. A retry would
    // throw that away and pay for it twice. The card sits after the badges, before metrics, and a
    // Blocked task must never wear the "Failed" alert.
    let sent: unknown = undefined
    serve(
      detail(
        { status: 'Blocked', completedAt: null, subtreeCostUsd: 1.37 },
        {
          result: 'Findings.\n\nShould I accept negative inputs?',
          failureReason: null,
          blocked: {
            kind: 'Question',
            round: 1,
            blockedAt: '2026-08-07T10:12:00Z',
            question: 'Should I accept negative inputs?',
            context: 'Findings.',
            priorRounds: [],
            progress: null,
            canAnswer: true,
            cannotAnswerReason: null,
            mergeTaskId: null,
          },
        },
      ),
      [
        http.post('/api/agent-tasks/:id/reply', async ({ request }) => {
          sent = await request.json()
          return HttpResponse.json(detail({ status: 'Working' }).summary)
        }),
      ],
    )
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    const card = await screen.findByTestId('blocked-question-card')
    expect(card).toBeInTheDocument()
    expect(screen.queryByText('Failed')).not.toBeInTheDocument()
    expect(screen.getByTestId('blocked-question')).toHaveTextContent('Should I accept negative inputs?')

    await userEvent.type(
      await screen.findByPlaceholderText('e.g. yes, accept negatives'),
      'yes, accept negatives',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Send answer' }))

    await waitFor(() =>
      expect(sent).toEqual({ message: 'yes, accept negatives', origin: 'Web', round: 1 }),
    )
  })

  it('keeps the transcript but hides the dead files link once a task settles', async () => {
    serve(detail())
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByRole('link', { name: /Transcript/ })).toHaveAttribute(
      'href',
      '/agents?agent=agent-9',
    )
    expect(screen.queryAllByRole('link', { name: /Files/ })).toHaveLength(0)
  })

  it('links to files while its delegate is still running', async () => {
    serve(detail({ status: 'Working', completedAt: null }))
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByRole('link', { name: /Files/ })).toHaveAttribute('href', '/agents/agent-9/files')
  })

  it('points at the spill file when the report was too big to forward', async () => {
    serve(
      detail({ status: 'Succeeded' }, {
        result: 'Summary of a very long report.',
        resultFilePath: 'C:/src/antiphon/.antiphon/task-77777777.md',
        failureReason: null,
      }),
    )
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByText('C:/src/antiphon/.antiphon/task-77777777.md')).toBeInTheDocument()
  })

  it('shows a Reroute control on a routing-exhausted Blocked task and posts kind/level', async () => {
    const posts: Array<{ url: string; body: unknown }> = []
    serve(
      detail(
        { status: 'Blocked', complexity: 'Hard', completedAt: null },
        { failureReason: 'routing exhausted: Hard chain — fable held' },
      ),
      [
        http.post('/api/agent-tasks/:id/reroute', async ({ request }) => {
          posts.push({ url: request.url, body: await request.json() })
          return HttpResponse.json({ id: TASK_ID, status: 'Queued' })
        }),
      ],
    )
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByTestId('task-reroute')).toBeInTheDocument()
    expect(screen.getByText('Hard')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reroute' }))
    await waitFor(() => expect(posts.length).toBe(1))
    expect(posts[0].body).toEqual({ agentKind: 'Grok', modelLevel: 'Frontier' })
  })
})
