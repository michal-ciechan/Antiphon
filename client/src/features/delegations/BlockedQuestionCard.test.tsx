import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto, BlockedContextDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { BlockedQuestionCard } from './BlockedQuestionCard'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const TASK_ID = '77777777-7777-7777-7777-777777777777'

function blocked(overrides: Partial<BlockedContextDto> = {}): BlockedContextDto {
  return {
    kind: 'Question',
    round: 1,
    blockedAt: '2026-08-07T14:02:00Z',
    question: 'Should I accept negative inputs?',
    context: 'Added Fizz(int).',
    priorRounds: [],
    progress: {
      branch: 'task/77777777',
      commits: ['abc1234 pin the parser'],
      changedFiles: 2,
      untrackedFiles: 0,
      lastCheckDigest: 'still working',
      lastCheckAt: '2026-08-07T13:50:00Z',
      unavailable: null,
    },
    canAnswer: true,
    cannotAnswerReason: null,
    mergeTaskId: null,
    ...overrides,
  }
}

function detail(blockedOverrides: Partial<BlockedContextDto> = {}): AgentTaskDetailDto {
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
    status: 'Blocked',
    workspace: 'Worktree',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scope: null,
    agentId: 'agent-9',
    agentName: 'task-77777777',
    agentSessionId: 'session-9',
    attempt: 1,
    createdAt: '2026-08-07T10:00:00Z',
    dispatchedAt: '2026-08-07T10:00:00Z',
    completedAt: null,
    recoveredAt: null,
    tokensIn: 12_000,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 800,
    costUsd: 1.37,
    costPricingVersion: 2,
    subtreeCostUsd: 1.37,
    worktreePath: 'C:/wt',
    worktreeBranch: 'task/77777777',
    childCount: 0,
    expectedDurationMinutes: 10,
    nextCheckAt: null,
    checkCount: 1,
  }
  return {
    summary,
    goal: 'Work out why Antiphon.Tests hangs on CI.',
    result: 'Added Fizz(int).\n\nShould I accept negative inputs?',
    resultFilePath: null,
    failureReason: null,
    mergeTargetRef: null,
    events: [],
    blocked: blocked(blockedOverrides),
  }
}

describe('BlockedQuestionCard', () => {
  it('reads question, then the box, then goal, then so far', () => {
    renderWithProviders(<BlockedQuestionCard detail={detail()} variant="full" />)

    const question = screen.getByTestId('blocked-question')
    const reply = screen.getByTestId('blocked-reply')
    const goal = screen.getByTestId('blocked-goal')
    const soFar = screen.getByTestId('blocked-so-far')
    expect(question.compareDocumentPosition(reply) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(reply.compareDocumentPosition(goal) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(goal.compareDocumentPosition(soFar) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(question).toHaveTextContent('Should I accept negative inputs?')
    expect(soFar).toHaveTextContent('Before it asked')
    expect(soFar).toHaveTextContent('On disk')
  })

  it('keeps the draft when the round changes', async () => {
    const first = detail()
    const { rerender } = renderWithProviders(<BlockedQuestionCard detail={first} variant="full" />)

    const box = screen.getByRole('textbox', { name: 'Answer the delegate' })
    await userEvent.type(box, 'yes, accept negatives')

    rerender(
      <BlockedQuestionCard
        detail={detail({ round: 2, question: 'A different question now?' })}
        variant="full"
      />,
    )

    expect(box).toHaveValue('yes, accept negatives')
    expect(screen.getByText(/The question changed since you started/)).toBeInTheDocument()
  })

  it('sends on Ctrl+Enter', async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(`/api/agent-tasks/${TASK_ID}/reply`, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json({ id: TASK_ID, status: 'Working' })
      }),
    )
    renderWithProviders(<BlockedQuestionCard detail={detail()} variant="full" />)

    const box = screen.getByRole('textbox', { name: 'Answer the delegate' })
    await userEvent.type(box, 'yes')
    await userEvent.keyboard('{Control>}{Enter}{/Control}')

    await waitFor(() =>
      expect(bodies).toEqual([{ message: 'yes', origin: 'Web', round: 1 }]),
    )
  })

  it('labels a merge conflict as an instruction, not an answer', () => {
    renderWithProviders(
      <BlockedQuestionCard
        detail={detail({
          kind: 'MergeConflict',
          question: 'Rebase onto master conflicted in 2 file(s).',
          context: null,
          mergeTaskId: '88888888-8888-8888-8888-888888888888',
          progress: null,
        })}
        variant="full"
      />,
    )

    expect(screen.getByRole('button', { name: 'Tell the delegate' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Merge task 88888888/ })).toBeInTheDocument()
  })

  it('replaces the box with the ceiling reason for a cost block', () => {
    renderWithProviders(
      <BlockedQuestionCard
        detail={detail({
          kind: 'CostCeiling',
          question: 'Run cost ceiling reached ($5.00).',
          canAnswer: false,
          cannotAnswerReason: 'Run cost ceiling reached ($5.00).',
          context: null,
          progress: null,
        })}
        variant="full"
      />,
    )

    expect(screen.queryByRole('textbox', { name: 'Answer the delegate' })).not.toBeInTheDocument()
    expect(screen.getByText(/Delegation:MaxCostUsdPerRoot/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Escalate' })).toBeInTheDocument()
  })
})
