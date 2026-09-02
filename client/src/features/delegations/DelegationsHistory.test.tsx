import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskListSummaryDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DelegationsHistory } from './DelegationsHistory'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

function summary(overrides: Partial<AgentTaskSummaryDto> & { id: string }): AgentTaskSummaryDto {
  return {
    rootTaskId: overrides.rootTaskId ?? overrides.id,
    parentTaskId: null,
    depth: 0,
    title: overrides.id,
    kind: 'Worker',
    role: 'Custom',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Succeeded',
    workspace: 'Shared',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scope: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-07T10:00:00Z',
    dispatchedAt: '2026-08-07T10:00:00Z',
    completedAt: '2026-08-07T10:05:00Z',
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 0,
    costPricingVersion: 2,
    worktreePath: null,
    worktreeBranch: null,
    subtreeCostUsd: 0,
    childCount: 0,
    expectedDurationMinutes: 10,
    nextCheckAt: null,
    checkCount: 0,
    recoveredAt: null,
    readAt: null,
    ...overrides,
  }
}

const ROOT_ID = '11111111-1111-1111-1111-111111111111'
const CHILD_ID = '22222222-2222-2222-2222-222222222222'
const FAILED_ID = '33333333-3333-3333-3333-333333333333'
const UNREAD_ID = '44444444-4444-4444-4444-444444444444'
const READ_ID = '55555555-5555-5555-5555-555555555555'
const WORKING_ID = '66666666-6666-6666-6666-666666666666'

const SETTLED: AgentTaskSummaryDto[] = [
  summary({
    id: ROOT_ID,
    title: 'Ship the Postgres 18 upgrade',
    kind: 'Orchestrator',
    role: 'Plan',
    modelLevel: 'Frontier',
    status: 'Succeeded',
    agentName: 'task-11111111',
    completedAt: '2026-08-07T10:20:00Z',
    cardIdentifier: 'CARD-0093',
  }),
  summary({
    id: CHILD_ID,
    rootTaskId: ROOT_ID,
    parentTaskId: ROOT_ID,
    depth: 1,
    title: 'Run the suite',
    role: 'Test',
    modelLevel: 'Low',
    status: 'Succeeded',
    agentName: 'task-22222222',
    createdAt: '2026-08-07T10:02:00Z',
    completedAt: '2026-08-07T10:05:00Z',
    cardIdentifier: 'CARD-0093',
  }),
  summary({
    id: FAILED_ID,
    title: 'Fix the flaky channel test',
    role: 'Debug',
    status: 'Failed',
    createdAt: '2026-08-07T09:00:00Z',
    completedAt: '2026-08-07T09:10:00Z',
  }),
  summary({
    id: UNREAD_ID,
    title: 'Unread deliverable',
    status: 'Succeeded',
    role: 'Docs',
    completedAt: new Date(Date.now() - 60_000).toISOString(),
    readAt: null,
  }),
  summary({
    id: READ_ID,
    title: 'Already read deliverable',
    status: 'Succeeded',
    role: 'Docs',
    completedAt: new Date(Date.now() - 120_000).toISOString(),
    readAt: new Date(Date.now() - 30_000).toISOString(),
  }),
  // Defensive: the server should not send an open row when status=settled.
  summary({
    id: WORKING_ID,
    title: 'Still working',
    status: 'Working',
    completedAt: null,
    dispatchedAt: '2026-08-07T10:00:00Z',
  }),
]

function detailFor(task: AgentTaskSummaryDto): AgentTaskDetailDto {
  return {
    summary: task,
    goal: `Goal for ${task.title}`,
    result: task.status === 'Failed' ? 'Could not reproduce.' : `Report for ${task.title}`,
    resultFilePath: null,
    failureReason: task.status === 'Failed' ? 'Could not reproduce.' : null,
    mergeTargetRef: null,
    events: [{ type: 'Created', modelLevel: task.modelLevel, detail: 'Created.', at: task.createdAt }],
  }
}

function serveTasks(tasks: AgentTaskSummaryDto[] = SETTLED, summaryOverride?: Partial<AgentTaskListSummaryDto>) {
  const listSummary: AgentTaskListSummaryDto = {
    active: tasks.filter((task) => task.status === 'Dispatched' || task.status === 'Working').length,
    blocked: tasks.filter((task) => task.status === 'Blocked').length,
    runs: new Set(tasks.map((task) => task.rootTaskId)).size,
    totalCostUsd: tasks.reduce((sum, task) => sum + task.costUsd, 0),
    byStatus: {},
    ...summaryOverride,
  }
  server.use(
    http.get('/api/agent-tasks', () => HttpResponse.json(tasks)),
    http.get('/api/agent-tasks/summary', () => HttpResponse.json(listSummary)),
    http.get('/api/agent-tasks/:id', ({ params }) => {
      const found = tasks.find((task) => task.id === params.id)
      return found ? HttpResponse.json(detailFor(found)) : new HttpResponse(null, { status: 404 })
    }),
  )
}

const shortId = (id: string) => id.replace(/-/g, '').slice(0, 8)

describe('DelegationsHistory', () => {
  it('requests the 7-day settled window by default, then drops since on Show all', async () => {
    const captured: Array<{ since: string | null; status: string | null }> = []
    server.use(
      http.get('/api/agent-tasks', ({ request }) => {
        const params = new URL(request.url).searchParams
        captured.push({ since: params.get('since'), status: params.get('status') })
        return HttpResponse.json(SETTLED)
      }),
      http.get('/api/agent-tasks/summary', () =>
        HttpResponse.json({ active: 0, blocked: 0, runs: 4, totalCostUsd: 0, byStatus: {} }),
      ),
    )
    renderWithProviders(<DelegationsHistory />)

    await screen.findByTestId(`history-row-${shortId(UNREAD_ID)}`)
    expect(captured).toHaveLength(1)
    expect(captured[0].status).toBe('Succeeded,Failed,Canceled')
    expect(captured[0].since).toMatch(/^\d{4}-\d{2}-\d{2}T/)
    const ageMs = Date.now() - Date.parse(captured[0].since!)
    expect(Math.abs(ageMs - 7 * 24 * 60 * 60 * 1000)).toBeLessThanOrEqual(5_000)

    await userEvent.click(screen.getByRole('button', { name: 'Show all' }))
    await waitFor(() => expect(captured.some((row) => row.since === null)).toBe(true))
    const showAll = captured.find((row) => row.since === null)
    expect(showAll?.status).toBe('Succeeded,Failed,Canceled')
  })

  it('lists newest-settled first and hides a Working row in the mock', async () => {
    serveTasks()
    renderWithProviders(<DelegationsHistory />)

    const rows = await screen.findAllByTestId(/^history-row-/)
    expect(rows[0]).toHaveAttribute('data-testid', `history-row-${shortId(UNREAD_ID)}`)
    expect(screen.queryByTestId(`history-row-${shortId(WORKING_ID)}`)).not.toBeInTheDocument()
    expect(screen.queryByText('Still working')).not.toBeInTheDocument()
  })

  it('filters to Failed and restores on All', async () => {
    serveTasks()
    renderWithProviders(<DelegationsHistory />)

    await screen.findByText('Fix the flaky channel test')
    await userEvent.click(screen.getByRole('radio', { name: 'Failed' }))
    expect(screen.getByText('Fix the flaky channel test')).toBeInTheDocument()
    expect(screen.queryByText('Ship the Postgres 18 upgrade')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: 'All' }))
    expect(screen.getByText('Ship the Postgres 18 upgrade')).toBeInTheDocument()
    expect(screen.getByText('Fix the flaky channel test')).toBeInTheDocument()
  })

  it('shows a bound CARD chip and a child row’s root title', async () => {
    serveTasks()
    renderWithProviders(<DelegationsHistory />)

    const child = await screen.findByTestId(`history-row-${shortId(CHILD_ID)}`)
    expect(child).toHaveTextContent('CARD-0093')
    expect(child).toHaveTextContent('↳ Ship the Postgres 18 upgrade')
  })

  it('shows the unread dot only on an unread Succeeded deliverable', async () => {
    serveTasks()
    renderWithProviders(<DelegationsHistory />)

    await screen.findByTestId(`history-unread-${UNREAD_ID}`)
    expect(screen.queryByTestId(`history-unread-${READ_ID}`)).not.toBeInTheDocument()
  })

  it('opens the drawer on a row, with the delegate’s own words', async () => {
    serveTasks()
    renderWithProviders(<DelegationsHistory />)

    await userEvent.click(await screen.findByTestId(`history-row-${shortId(ROOT_ID)}`))
    expect(await screen.findByText('Report for Ship the Postgres 18 upgrade')).toBeInTheDocument()
    expect(screen.getByText('Goal for Ship the Postgres 18 upgrade')).toBeInTheDocument()
  })

  it('virtualizes a long history instead of rendering every row', async () => {
    const ids = Array.from(
      { length: 600 },
      (_, index) => `${String(index + 1).padStart(8, '0')}-1111-1111-1111-111111111111`,
    )
    const tasks = ids.map((id, index) =>
      summary({
        id,
        title: `Settled ${index}`,
        status: 'Succeeded',
        completedAt: new Date(Date.parse('2026-08-07T10:00:00Z') - index * 60_000).toISOString(),
      }),
    )
    serveTasks(tasks)
    renderWithProviders(<DelegationsHistory />)

    await waitFor(() =>
      expect(document.querySelectorAll('[data-testid^="history-row-"]').length).toBeGreaterThan(0),
    )
    expect(document.querySelectorAll('[data-testid^="history-row-"]').length).toBeLessThan(80)
  })
})
