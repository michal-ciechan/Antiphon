import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { TaskDrawer } from './TaskDrawer'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const TASK_ID = '77777777-7777-7777-7777-777777777777'

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
