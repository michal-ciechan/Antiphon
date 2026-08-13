import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { TaskDrawer } from './TaskDrawer'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
vi.setConfig({ testTimeout: 20_000 })

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
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Failed',
    workspace: 'Shared',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scopeGlob: null,
    agentId: 'agent-9',
    agentName: 'task-77777777',
    agentSessionId: 'session-9',
    attempt: 1,
    createdAt: '2026-08-07T10:00:00Z',
    dispatchedAt: '2026-08-07T10:00:00Z',
    completedAt: '2026-08-07T10:12:00Z',
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
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(body)), ...extra)
}

describe('TaskDrawer', () => {
  it('shows what the task cost and what stopped it', async () => {
    serve(detail())
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByText('Ran out of time without reproducing.')).toBeInTheDocument()
    expect(screen.getByText('task-77777777')).toBeInTheDocument()
    expect(screen.getByText('$0.03')).toBeInTheDocument()
    expect(screen.getByText('12m00')).toBeInTheDocument()
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
    // throw that away and pay for it twice.
    let sent: unknown = undefined
    serve(
      detail({ status: 'Blocked', completedAt: null }, { result: 'Should I accept negative inputs?' }),
      [
        http.post('/api/agent-tasks/:id/reply', async ({ request }) => {
          sent = await request.json()
          return HttpResponse.json(detail({ status: 'Working' }).summary)
        }),
      ],
    )
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    await userEvent.type(
      await screen.findByPlaceholderText('e.g. yes, accept negatives'),
      'yes, accept negatives',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(sent).toEqual({ message: 'yes, accept negatives' }))
  })

  it('links to the delegate’s own transcript and files', async () => {
    serve(detail())
    renderWithProviders(<TaskDrawer taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByRole('link', { name: /Transcript/ })).toHaveAttribute(
      'href',
      '/agents?agent=agent-9',
    )
    expect(screen.getByRole('link', { name: /Files/ })).toHaveAttribute('href', '/agents/agent-9/files')
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
})
