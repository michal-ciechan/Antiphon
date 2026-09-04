import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DelegationTaskModal } from './DelegationTaskModal'

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
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(body)), ...extra)
}

describe('DelegationTaskModal', () => {
  it('opens with a Blocked detail and shows the question plus the answer box', async () => {
    // CARD-0033: a Blocked detail carries the question in `blocked`, and the modal renders the
    // question-first card from it — `result` alone renders nothing when Blocked.
    serve(
      detail(
        { status: 'Blocked', completedAt: null },
        {
          result: 'Should I accept negative inputs?',
          blocked: {
            kind: 'Question',
            round: 1,
            blockedAt: '2026-08-07T10:12:00Z',
            question: 'Should I accept negative inputs?',
            context: null,
            priorRounds: [],
            progress: null,
            canAnswer: true,
            cannotAnswerReason: null,
            mergeTaskId: null,
          },
        },
      ),
    )
    renderWithProviders(<DelegationTaskModal taskId={TASK_ID} onClose={() => {}} />)

    expect(await screen.findByTestId('blocked-question')).toHaveTextContent('Should I accept negative inputs?')
    expect(screen.getByPlaceholderText('e.g. yes, accept negatives')).toBeInTheDocument()
    expect(screen.getByText('Should I accept negative inputs?')).toBeInTheDocument()
  })

  it('fires onClose when Cancel succeeds', async () => {
    let closed = false
    serve(detail({ status: 'Working', completedAt: null }), [
      http.post('/api/agent-tasks/:id/cancel', () =>
        HttpResponse.json(detail({ status: 'Canceled' }).summary),
      ),
    ])
    renderWithProviders(<DelegationTaskModal taskId={TASK_ID} onClose={() => { closed = true }} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(closed).toBe(true))
  })
})
