import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DelegationsBoard } from './DelegationsBoard'

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
    status: 'Queued',
    workspace: 'Shared',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scopeGlob: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-07T10:00:00Z',
    dispatchedAt: null,
    completedAt: null,
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
    ...overrides,
  }
}

/** A run with the shape the design is actually for: orchestrator → sub-orchestrator → workers. */
const RUN: AgentTaskSummaryDto[] = [
  summary({
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Ship the Postgres 18 upgrade',
    kind: 'Orchestrator',
    role: 'Plan',
    modelLevel: 'Frontier',
    status: 'Working',
    agentName: 'task-11111111',
    dispatchedAt: '2026-08-07T10:00:00Z',
    costUsd: 0.02,
    subtreeCostUsd: 0.09,
    childCount: 2,
  }),
  summary({
    id: '22222222-2222-2222-2222-222222222222',
    rootTaskId: '11111111-1111-1111-1111-111111111111',
    parentTaskId: '11111111-1111-1111-1111-111111111111',
    title: 'Migrate the schema',
    kind: 'Orchestrator',
    role: 'Code',
    modelLevel: 'Frontier',
    status: 'Working',
    agentName: 'task-22222222',
    createdAt: '2026-08-07T10:01:00Z',
    dispatchedAt: '2026-08-07T10:01:00Z',
    costUsd: 0.04,
    subtreeCostUsd: 0.06,
    childCount: 1,
  }),
  summary({
    id: '33333333-3333-3333-3333-333333333333',
    rootTaskId: '11111111-1111-1111-1111-111111111111',
    parentTaskId: '22222222-2222-2222-2222-222222222222',
    title: 'Run the suite',
    role: 'Test',
    modelLevel: 'Low',
    status: 'Succeeded',
    agentName: 'task-33333333',
    createdAt: '2026-08-07T10:02:00Z',
    dispatchedAt: '2026-08-07T10:02:00Z',
    completedAt: '2026-08-07T10:05:00Z',
    costUsd: 0.02,
    subtreeCostUsd: 0.02,
  }),
  summary({
    id: '44444444-4444-4444-4444-444444444444',
    rootTaskId: '11111111-1111-1111-1111-111111111111',
    parentTaskId: '11111111-1111-1111-1111-111111111111',
    title: 'Update the compose file',
    role: 'Docs',
    modelLevel: 'Medium',
    status: 'Blocked',
    agentName: 'task-44444444',
    createdAt: '2026-08-07T10:03:00Z',
    dispatchedAt: '2026-08-07T10:03:00Z',
    costUsd: 0.01,
    subtreeCostUsd: 0.01,
  }),
  // A second, unrelated run — "only this run" has to have something to filter out.
  summary({
    id: '55555555-5555-5555-5555-555555555555',
    title: 'Fix the flaky channel test',
    role: 'Debug',
    status: 'Failed',
    createdAt: '2026-08-07T09:00:00Z',
    dispatchedAt: '2026-08-07T09:00:00Z',
    completedAt: '2026-08-07T09:10:00Z',
  }),
]

function detailFor(task: AgentTaskSummaryDto): AgentTaskDetailDto {
  return {
    summary: task,
    goal: `Goal for ${task.title}`,
    result: task.status === 'Blocked' ? 'Should I keep the old cmd examples alongside?' : null,
    resultFilePath: null,
    failureReason: task.status === 'Failed' ? 'Could not reproduce.' : null,
    mergeTargetRef: null,
    events: [
      { type: 'Created', modelLevel: task.modelLevel, detail: 'Created.', at: task.createdAt },
    ],
  }
}

function serveTasks(tasks: AgentTaskSummaryDto[] = RUN) {
  server.use(
    http.get('/api/agent-tasks', () => HttpResponse.json(tasks)),
    http.get('/api/agent-tasks/:id', ({ params }) => {
      const found = tasks.find((t) => t.id === params.id)
      return found ? HttpResponse.json(detailFor(found)) : new HttpResponse(null, { status: 404 })
    }),
  )
}

const shortId = (id: string) => id.replace(/-/g, '').slice(0, 8)

describe('DelegationsBoard', () => {
  it('lands each task in the lane that says what it needs', async () => {
    serveTasks()
    renderWithProviders(<DelegationsBoard />)

    const working = await screen.findByTestId('lane-working')
    // Both orchestrators are mid-flight; Dispatched and Working share a lane.
    expect(within(working).getByText('Ship the Postgres 18 upgrade')).toBeInTheDocument()
    expect(within(working).getByText('Migrate the schema')).toBeInTheDocument()

    // A delegate's question must not be filed under Done, or nobody ever answers it.
    expect(within(screen.getByTestId('lane-blocked')).getByText('Update the compose file')).toBeInTheDocument()

    const done = screen.getByTestId('lane-done')
    expect(within(done).getByText('Run the suite')).toBeInTheDocument()
    expect(within(done).getByText('Fix the flaky channel test')).toBeInTheDocument()
  })

  it('shows the tier as its own badge, per task', async () => {
    serveTasks()
    renderWithProviders(<DelegationsBoard />)

    // The chip and the tree row both carry it, hence getAllBy.
    expect((await screen.findAllByTestId('tier-Frontier')).length).toBeGreaterThan(0)
    expect(screen.getAllByTestId('tier-Low').length).toBeGreaterThan(0)
  })

  it('names the model a task actually runs, not the Claude one at that rung', async () => {
    // CARD-0084: the chip on a Grok delegate used to read "fable" — a model nobody was paying for,
    // on the one surface an operator scans to decide what to escalate. The Claude row is the
    // control: its text must not move by a byte.
    serveTasks([
      summary({
        id: '77777777-7777-7777-7777-777777777777',
        title: 'Sweep the log noise',
        agentKind: 'Grok',
        modelLevel: 'Frontier',
        escalatedFrom: 'Medium',
        status: 'Working',
        attempt: 2,
        dispatchedAt: '2026-08-07T10:00:00Z',
      }),
      summary({
        id: '88888888-8888-8888-8888-888888888888',
        title: 'Sweep the same noise on Claude',
        modelLevel: 'Frontier',
        escalatedFrom: 'Medium',
        status: 'Working',
        attempt: 2,
        dispatchedAt: '2026-08-07T10:00:00Z',
      }),
    ])
    renderWithProviders(<DelegationsBoard />)

    const grok = await screen.findByTestId(`task-chip-${shortId('77777777-7777-7777-7777-777777777777')}`)
    expect(within(grok).getByTestId('tier-Frontier')).toHaveTextContent('grok-4.6')
    expect(within(grok).getByText('grok-4.5 →')).toBeInTheDocument()

    const claude = screen.getByTestId(`task-chip-${shortId('88888888-8888-8888-8888-888888888888')}`)
    expect(within(claude).getByTestId('tier-Frontier')).toHaveTextContent('fable')
    expect(within(claude).getByText('sonnet →')).toBeInTheDocument()
  })

  it('carries health and rank at the same time, on separate channels', async () => {
    // The case that proves the two axes are independent: a failing task at the TOP tier must be
    // able to show "in trouble" and "expensive" at once, without either reading as the other.
    serveTasks([
      summary({
        id: '66666666-6666-6666-6666-666666666666',
        title: 'Reproduce the deadlock',
        role: 'Debug',
        modelLevel: 'Frontier',
        status: 'Failed',
        dispatchedAt: '2026-08-07T10:00:00Z',
        completedAt: '2026-08-07T10:30:00Z',
      }),
    ])
    renderWithProviders(<DelegationsBoard />)

    const chip = await screen.findByTestId(`task-chip-${shortId('66666666-6666-6666-6666-666666666666')}`)
    // Read the raw declaration: jsdom will not resolve a var() inside the border shorthand.
    expect(chip.getAttribute('style')).toContain('--mantine-color-danger-6')
    expect(within(chip).getByTestId('tier-Frontier')).toBeInTheDocument()
  })

  it('nests the fan-out under the orchestrator that asked for it', async () => {
    serveTasks()
    renderWithProviders(<DelegationsBoard />)

    // Root open by default; its children are visible without a click.
    await screen.findByTestId(`task-tree-row-${shortId(RUN[0].id)}`)
    expect(screen.getByTestId(`task-tree-row-${shortId(RUN[1].id)}`)).toBeInTheDocument()

    // The grandchild is inside a collapsed sub-orchestrator, so it is NOT shown yet.
    expect(screen.queryByTestId(`task-tree-row-${shortId(RUN[2].id)}`)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Expand Migrate the schema' }))
    expect(screen.getByTestId(`task-tree-row-${shortId(RUN[2].id)}`)).toBeInTheDocument()
  })

  it('accounts for a collapsed subtree on the row that hides it', async () => {
    // Otherwise a run reads as cheaper and smaller than it was — the exact failure mode of
    // collapsing by default.
    serveTasks()
    renderWithProviders(<DelegationsBoard />)

    await screen.findByTestId(`task-tree-row-${shortId(RUN[1].id)}`)
    expect(screen.getByText('2 · $0.06')).toBeInTheDocument()
  })

  it('opens the drawer on a chip, with the delegate’s own words', async () => {
    serveTasks()
    renderWithProviders(<DelegationsBoard />)

    await userEvent.click(await screen.findByTestId(`task-chip-${shortId(RUN[3].id)}`))

    expect(await screen.findByText('Should I keep the old cmd examples alongside?')).toBeInTheDocument()
    expect(screen.getByText('Goal for Update the compose file')).toBeInTheDocument()
  })

  it('filters the lanes to one run when asked', async () => {
    serveTasks()
    renderWithProviders(<DelegationsBoard />)

    await userEvent.click(await screen.findByTestId(`task-chip-${shortId(RUN[4].id)}`))
    await userEvent.click(screen.getByLabelText('Only this run'))

    await waitFor(() =>
      expect(
        within(screen.getByTestId('lane-working')).queryByText('Ship the Postgres 18 upgrade'),
      ).not.toBeInTheDocument(),
    )
    expect(within(screen.getByTestId('lane-done')).getByText('Fix the flaky channel test')).toBeInTheDocument()
    // The tree still shows every run — it is the shape of the fleet, not a filtered view.
    expect(screen.getByTestId(`task-tree-row-${shortId(RUN[0].id)}`)).toBeInTheDocument()
  })

  it('says what an empty board is for instead of showing nothing', async () => {
    serveTasks([])
    renderWithProviders(<DelegationsBoard />)

    expect(await screen.findByText(/No delegated tasks yet/)).toBeInTheDocument()
  })
})
