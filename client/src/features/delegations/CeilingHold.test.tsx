import { HttpResponse, http } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { notifications } from '@mantine/notifications'
import {
  useAgentTask,
  type AgentTaskDetailDto,
  type AgentTaskSummaryDto,
  type CeilingHoldDto,
} from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CeilingHoldCard } from './CeilingHoldCard'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const TASK_ID = '11111111-1111-1111-1111-111111111111'
const EPISODE = '22222222-2222-2222-2222-222222222222'

function summary(status: AgentTaskSummaryDto['status']): AgentTaskSummaryDto {
  return {
    id: TASK_ID,
    rootTaskId: TASK_ID,
    parentTaskId: null,
    depth: 0,
    title: 'Review the long merge',
    kind: 'Worker',
    role: 'Review',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status,
    workspace: 'Worktree',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scope: null,
    agentId: 'agent-9',
    agentName: 'task-11111111',
    agentSessionId: 'session-9',
    attempt: 1,
    createdAt: '2026-11-02T06:00:00Z',
    dispatchedAt: '2026-11-02T06:00:00Z',
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
    worktreeBranch: 'task/11111111',
    childCount: 0,
    expectedDurationMinutes: 60,
    nextCheckAt: null,
    checkCount: 1,
  }
}

function hold({ capReached = false, phaseKind = 'LocalExecution' as string | null } = {}): CeilingHoldDto {
  return {
    episodeId: EPISODE,
    heldAt: '2026-11-02T10:00:00Z',
    attempt: 1,
    sessionStartedAt: '2026-11-02T06:00:00Z',
    initialMinutes: 240,
    initialSource: 'rolePolicy',
    nextCeilingMinutes: capReached ? 720 : 240,
    lastTranscriptKind: 'ToolCall',
    lastTranscriptAgeSeconds: 360,
    phaseKind,
    phaseLimitMinutes: phaseKind ? 90 : null,
    phaseDeadlineAt: phaseKind ? '2026-11-02T11:00:00Z' : null,
    capReached,
    question: 'Task 11111111 reached its 240-minute ceiling. The delegate continues and still consumes capacity.',
  }
}

function holdDetail(options: { capReached?: boolean; phaseKind?: string | null } = {}): AgentTaskDetailDto {
  return {
    summary: summary('Working'),
    goal: 'Review the long merge.',
    result: null,
    resultFilePath: null,
    failureReason: null,
    mergeTargetRef: null,
    events: [],
    ceilingHold: hold(options),
  }
}

/** Every request msw sees, so no POST goes unasserted. */
let requests: { method: string; path: string; body: unknown }[] = []

function record(detail: AgentTaskDetailDto, reply?: () => Response) {
  server.use(
    http.get(`/api/agent-tasks/${TASK_ID}`, () => HttpResponse.json(detail)),
    http.post(`/api/agent-tasks/${TASK_ID}/reply`, () =>
      reply ? reply() : HttpResponse.json({ ...summary('Working') }),
    ),
    http.post(`/api/agent-tasks/${TASK_ID}/cancel`, () => HttpResponse.json({ ...summary('Cancelled') })),
  )
}

const posts = (suffix?: string) =>
  requests.filter((r) => r.method === 'POST' && (suffix === undefined || r.path.endsWith(suffix)))

/** The card as the detail page mounts it: from the detail query, so a refetch is observable. */
function LiveCard() {
  const { data } = useAgentTask(TASK_ID)
  return data ? <CeilingHoldCard detail={data} /> : null
}

beforeEach(() => {
  requests = []
  server.events.on('request:start', async ({ request }) => {
    const clone = request.clone()
    const text = request.method === 'POST' ? await clone.text() : ''
    requests.push({
      method: request.method,
      path: new URL(request.url).pathname,
      body: text ? JSON.parse(text) : undefined,
    })
  })
  vi.mocked(notifications.show).mockClear()
})

afterEach(() => {
  server.events.removeAllListeners()
})

describe('CeilingHoldCard', () => {
  it('Continue sends a typed decision without an ordinary reply', async () => {
    record(holdDetail())
    renderWithProviders(<CeilingHoldCard detail={holdDetail()} />)
    const card = screen.getByTestId('ceiling-hold-card')

    await userEvent.click(within(card).getByRole('button', { name: 'Continue' }))

    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].path).toBe(`/api/agent-tasks/${TASK_ID}/reply`)
    expect(posts()[0].body).toEqual({ ceilingAction: 'Continue', episodeId: EPISODE })
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(
        expect.objectContaining({ message: 'Decision recorded; session continues' }),
      ),
    )
    expect(within(card).queryByRole('button', { name: /retry/i })).not.toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: /answer/i })).not.toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: /auto/i })).not.toBeInTheDocument()
  })

  it('Extend validates minutes and reports a stale episode', async () => {
    record(holdDetail(), () =>
      HttpResponse.json({ code: 'ceiling_episode_stale', detail: 'episode is stale' }, { status: 409 }),
    )
    renderWithProviders(<LiveCard />)
    const card = await screen.findByTestId('ceiling-hold-card')
    const box = within(card).getByRole('textbox', { name: /extend by/i })
    const extend = within(card).getByRole('button', { name: 'Extend' })

    for (const invalid of ['0', '721', '1.5']) {
      await userEvent.clear(box)
      await userEvent.type(box, invalid)
      expect(extend).toBeDisabled()
    }
    expect(posts()).toHaveLength(0)
    const detailReads = requests.filter((r) => r.method === 'GET').length

    await userEvent.clear(box)
    await userEvent.type(box, '60')
    expect(extend).toBeEnabled()
    await userEvent.click(extend)

    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body).toEqual({ ceilingAction: 'Extend', episodeId: EPISODE, extendMinutes: 60 })
    expect(await within(card).findByText('episode is stale')).toBeInTheDocument()
    await waitFor(() =>
      expect(requests.filter((r) => r.method === 'GET').length).toBeGreaterThan(detailReads),
    )
  })

  it('Final hold offers only Cancel and operator guidance', async () => {
    record(holdDetail({ capReached: true }))
    renderWithProviders(<CeilingHoldCard detail={holdDetail({ capReached: true })} />)
    const card = screen.getByTestId('ceiling-hold-card')

    expect(within(card).getByRole('button', { name: 'Continue' })).toBeDisabled()
    expect(within(card).getByRole('button', { name: 'Extend' })).toBeDisabled()
    expect(card).toHaveTextContent('-OperatorExtend')
    expect(card).toHaveTextContent(/Cancel/)
    expect(within(card).queryByRole('button', { name: /operator/i })).not.toBeInTheDocument()
    expect(within(card).queryByRole('textbox', { name: /operator/i })).not.toBeInTheDocument()

    await userEvent.click(within(card).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(posts('/cancel')).toHaveLength(1))
    expect(posts('/reply')).toHaveLength(0)
  })

  it.each(['phase', 'no-phase'])('renders %s guidance and sends nothing on render', async (shape) => {
    record(holdDetail({ phaseKind: shape === 'phase' ? 'LocalExecution' : null }))
    renderWithProviders(
      <CeilingHoldCard detail={holdDetail({ phaseKind: shape === 'phase' ? 'LocalExecution' : null })} />,
    )
    const card = screen.getByTestId('ceiling-hold-card')

    if (shape === 'phase') expect(card).toHaveTextContent('Phase LocalExecution: 90-minute limit')
    else expect(card).toHaveTextContent('No phase deadline covers this tail; the ceiling is its only deadline.')
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(posts()).toHaveLength(0)
  })

  it.each(['working-without-hold', 'failed-seat-held'])('renders no hold card for %s', async (shape) => {
    const detail: AgentTaskDetailDto =
      shape === 'working-without-hold'
        ? { ...holdDetail(), ceilingHold: null }
        : {
            ...holdDetail(),
            summary: summary('Failed'),
            ceilingHold: null,
            failedSession: {
              summary: 'Failed, session still Running: seat held',
              activity: 'working',
              taskId: TASK_ID,
              cardId: null,
              sessionId: 'session-9',
              failureSource: 'Ceiling',
              noticeState: 'Told',
            },
          }
    record(detail)
    renderWithProviders(<CeilingHoldCard detail={detail} />)

    expect(screen.queryByTestId('ceiling-hold-card')).toBeNull()
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(posts()).toHaveLength(0)
  })
})
