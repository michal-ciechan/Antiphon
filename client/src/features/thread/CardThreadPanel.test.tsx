import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import type { CardThreadDto, CardThreadTaskDto } from '../../api/cardThread'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CardThreadPanel } from './CardThreadPanel'

const card: CardDto = {
  id: 'card-guid-1',
  boardId: 'board-1',
  boardColumnId: 'column-review',
  ownerSessionId: null,
  currentWorktreeId: null,
  assignedAgentId: null,
  assignedAgentName: null,
  agentQueuePosition: null,
  activeWorkflowRunId: null,
  workflowRunStatus: null,
  currentWorkflowStageName: null,
  identifier: 'CARD-0067',
  title: 'Reply durability',
  description: '',
  importance: 'High', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 7,
  labels: [],
  status: 'Review',
  concurrencyToken: 'token-1',
  createdAt: '2026-08-16T08:00:00Z',
  updatedAt: '2026-08-17T10:00:00Z',
  startedAt: null,
  completedAt: null,
  terminalReason: null,
  sessions: [],
  revisionCount: 3,
  archivedAt: null,
  archivedReason: null,
  archivedBy: null,
}

const columns: BoardColumnDto[] = [
  {
    id: 'column-review',
    stateKey: 'review',
    name: 'Review',
    columnOrder: 2,
    cardStatus: 'Review',
    isActive: false,
    isTerminal: false,
    maxConcurrentSessions: null,
    cards: [],
  },
  {
    id: 'column-inprogress',
    stateKey: 'inprogress',
    name: 'In Progress',
    columnOrder: 1,
    cardStatus: 'InProgress',
    isActive: true,
    isTerminal: false,
    maxConcurrentSessions: null,
    cards: [],
  },
]

function threadTask(overrides: Partial<CardThreadTaskDto> = {}): CardThreadTaskDto {
  return {
    id: 't1',
    title: 'CARD-0067 - reply durability - slice 4',
    status: 'Working',
    kind: 'Worker',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    agentName: null,
    agentSessionId: null,
    createdAt: '2026-08-17T08:00:00Z',
    dispatchedAt: '2026-08-17T08:00:05Z',
    completedAt: null,
    nextCheckAt: null,
    checkCount: 0,
    costUsd: 0.5,
    subtreeCostUsd: 1.25,
    matchedOn: 'title',
    latestCheck: null,
    result: null,
    resultFilePath: null,
    failureReason: null,
    ...overrides,
  }
}

function thread(overrides: Partial<CardThreadDto> = {}): CardThreadDto {
  return {
    card,
    identifier: 'CARD-0067',
    repoRoot: 'C:/src/Antiphon',
    reposConsulted: true,
    generatedAt: '2026-08-17T12:00:00Z',
    plans: [
      {
        subject: true,
        plan: {
          relativePath: 'docs/superpowers/specs/2026-08-17-card-0067-reply-route.md',
          fileName: '2026-08-17-card-0067-reply-route.md',
          kind: 'Spec',
          title: 'The reply route, made durable',
          date: '2026-08-17',
          status: 'Proposed',
          cards: ['CARD-0067'],
          mentionedCards: [],
          sizeBytes: 4321,
          modifiedAt: '2026-08-17T09:00:00Z',
        },
      },
      {
        subject: false,
        plan: {
          relativePath: 'docs/superpowers/specs/2026-08-17-mobile-thread.md',
          fileName: '2026-08-17-mobile-thread.md',
          kind: 'Spec',
          title: 'Mobile thread and plan surfacing',
          date: '2026-08-17',
          status: 'Proposed',
          cards: ['CARD-0035'],
          mentionedCards: ['CARD-0067'],
          sizeBytes: 9999,
          modifiedAt: '2026-08-17T10:00:00Z',
        },
      },
    ],
    tasks: [threadTask()],
    commits: [
      {
        sha: 'a'.repeat(40),
        shortSha: 'aaaaaaa',
        author: 'Mike Ciechan',
        date: '2026-08-17T09:30:00Z',
        subject: 'fix(channels): CARD-0067 - the reply route out is durable',
      },
    ],
    ...overrides,
  }
}

function seed(data: CardThreadDto = thread(), attentionItems: unknown[] = []) {
  server.use(
    http.get('/api/cards/:id/thread', () => HttpResponse.json(data)),
    http.get('/api/attention', () =>
      HttpResponse.json({
        generatedAt: '2026-08-17T12:00:00Z',
        runnerConsulted: true,
        items: attentionItems,
      }),
    ),
  )
}

describe('CardThreadPanel', () => {
  it('renders one scroll in spec order: plans, then tasks, then commits', async () => {
    seed()
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    expect(await screen.findByText('The reply route, made durable')).toBeInTheDocument()
    const text = screen.getByTestId('card-thread').textContent ?? ''
    expect(text.indexOf('The reply route, made durable')).toBeLessThan(
      text.indexOf('CARD-0067 - reply durability - slice 4'),
    )
    expect(text.indexOf('CARD-0067 - reply durability - slice 4')).toBeLessThan(
      text.indexOf('the reply route out is durable'),
    )
  })

  it('subject plans are full rows; mentions fold behind a counted toggle, dimmed, without verbs', async () => {
    seed()
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" boardId="board-1" columns={columns} />)

    // The subject plan is a first-class row with the verbs.
    expect(await screen.findByText('The reply route, made durable')).toBeInTheDocument()
    expect(screen.getByTestId('thread-approve-plan')).toBeInTheDocument()

    // The mention is NOT on the surface — a neighbouring plan on every thread is the failure mode.
    expect(screen.queryByText('Mobile thread and plan surfacing')).not.toBeInTheDocument()
    await userEvent.click(screen.getByTestId('thread-mentions-toggle'))
    const mention = screen.getByTestId('thread-mention-2026-08-17-mobile-thread.md')
    expect(mention).toHaveTextContent('Mobile thread and plan surfacing')
    expect(mention).toHaveTextContent('mentions')
    // No Approve on a plan that is not about this card.
    expect(screen.getAllByTestId('thread-approve-plan')).toHaveLength(1)
  })

  it('the tier badge names the model the task runs on, not the Claude rung of its tier', async () => {
    // A Grok task badged `fable` would name a model nobody was paying for, on the surface an
    // operator reads to decide what a card cost and what to escalate (CARD-0084 S4).
    seed(thread({ tasks: [threadTask({ agentKind: 'Grok', modelLevel: 'High' })] }))
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    const row = await screen.findByTestId('thread-task-t1')
    expect(row).toHaveTextContent('grok-4.6')
    expect(row).not.toHaveTextContent('opus')
  })

  it('a Claude task badge is byte-identical to what it read before the kind was consulted', async () => {
    seed(thread({ tasks: [threadTask({ agentKind: 'ClaudeCode', modelLevel: 'High' })] }))
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    expect(await screen.findByTestId('thread-task-t1')).toHaveTextContent('opus')
  })

  it('a blocked task answers in place — the reply posts to the task endpoint', async () => {
    seed(thread({ tasks: [threadTask({ status: 'Blocked' })] }))
    const replies: unknown[] = []
    server.use(
      http.post('/api/agent-tasks/t1/reply', async ({ request }) => {
        replies.push(await request.json())
        return HttpResponse.json(threadTask({ status: 'Working' }))
      }),
    )
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    await userEvent.click(await screen.findByRole('button', { name: 'Answer it' }))
    await userEvent.type(screen.getByLabelText('Answer the delegate'), 'yes, keep the migration')
    await userEvent.click(screen.getByRole('button', { name: /Send answer/ }))

    await waitFor(() => expect(replies).toEqual([{ message: 'yes, keep the migration' }]))
  })

  it('Approve opens the move confirm: reason prefilled with the plan file, spawn named on an active target', async () => {
    seed()
    const moves: unknown[] = []
    server.use(
      http.patch('/api/cards/card-guid-1', async ({ request }) => {
        moves.push(await request.json())
        return HttpResponse.json({ card, spawnedSessionId: null, spawnSuppressed: false })
      }),
    )
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" boardId="board-1" columns={columns} />)

    await userEvent.click(await screen.findByTestId('thread-approve-plan'))

    // The default target is the active column, so the spawn warning shows immediately — the
    // MoveMenu contract: a move that starts work says so before Move.
    expect(await screen.findByText('This starts work')).toBeInTheDocument()
    expect(screen.getByText(/spawns an agent session on it/)).toBeInTheDocument()
    expect(screen.getByLabelText('Reason')).toHaveValue(
      'plan approved: docs/superpowers/specs/2026-08-17-card-0067-reply-route.md',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Move' }))
    await waitFor(() =>
      expect(moves).toEqual([
        {
          boardColumnId: 'column-inprogress',
          concurrencyToken: 'token-1',
          reason: 'plan approved: docs/superpowers/specs/2026-08-17-card-0067-reply-route.md',
          spawn: true,
        },
      ]),
    )
  })

  it('Hand back opens the DelegateModal prefilled with the identifier and the plan path', async () => {
    seed()
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" boardId="board-1" columns={columns} />)

    await userEvent.click(await screen.findByTestId('thread-hand-back'))

    expect(await screen.findByText('Hand back — CARD-0067')).toBeInTheDocument()
    expect(screen.getByLabelText('Goal')).toHaveValue(
      'CARD-0067 — change requested on plan docs/superpowers/specs/2026-08-17-card-0067-reply-route.md: ',
    )
  })

  it('joins /api/attention by taskId — the stuck badge and headline land on the task row', async () => {
    seed(thread(), [
      {
        kind: 'BlockedQuestion',
        severity: 'Critical',
        taskId: 't1',
        sessionId: null,
        agentId: null,
        messageId: null,
        title: 'CARD-0067 - reply durability - slice 4',
        headline: 'Waiting 22m for an answer.',
        evidence: 'Should negatives be accepted?',
        sinceUtc: '2026-08-17T11:38:00Z',
        subtreeCostUsd: 1.25,
        actions: ['Reply'],
      },
      {
        kind: 'DeadSession',
        severity: 'Error',
        taskId: 'someone-elses-task',
        sessionId: null,
        agentId: null,
        messageId: null,
        title: 'other work',
        headline: 'Session gone.',
        evidence: '',
        sinceUtc: null,
        subtreeCostUsd: null,
        actions: [],
      },
    ])
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    const joined = await screen.findByTestId('thread-task-attention-t1')
    expect(joined).toHaveTextContent('Blocked')
    expect(joined).toHaveTextContent('Waiting 22m for an answer.')
    // Another thread's stuckness stays off this one.
    expect(screen.queryByText('Session gone.')).not.toBeInTheDocument()
  })

  it('a check reading and a digest tail are labelled as different claims', async () => {
    seed(
      thread({
        tasks: [
          threadTask({
            latestCheck: {
              text: 'Slice 4 is committed; the delegate is running the suite.',
              fromInterpreter: true,
              at: '2026-08-17T11:00:00Z',
            },
          }),
        ],
      }),
    )
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    const check = await screen.findByTestId('thread-task-check-t1')
    expect(check).toHaveTextContent('check reading')
    expect(check).toHaveTextContent('Slice 4 is committed; the delegate is running the suite.')
  })

  it('reposConsulted false reads as "nobody could ask git", never as an empty commit list', async () => {
    seed(thread({ reposConsulted: false, repoRoot: null, commits: [], plans: [] }))
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    const honesty = await screen.findByTestId('thread-repos-not-consulted')
    expect(honesty).toHaveTextContent('unknown, not absent')
    expect(screen.queryByText('No commit cites this card.')).not.toBeInTheDocument()
  })

  it('a settled task shows its report first paragraph with a read-all expansion', async () => {
    seed(
      thread({
        tasks: [
          threadTask({
            status: 'Succeeded',
            completedAt: '2026-08-17T11:30:00Z',
            result: 'Landed the route; 6 tests green.\n\nDetail: the produce is claimed before send.',
          }),
        ],
      }),
    )
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    expect(await screen.findByText('Landed the route; 6 tests green.')).toBeInTheDocument()
    expect(screen.queryByText(/the produce is claimed before send/)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'read all' }))
    expect(screen.getByText(/the produce is claimed before send/)).toBeInTheDocument()
  })

  it('the terminal reason closes the scroll when the card is closed', async () => {
    seed(
      thread({
        card: { ...card, status: 'Done', terminalReason: 'shipped in c4df66b' },
        tasks: [],
      }),
    )
    renderWithProviders(<CardThreadPanel identifier="CARD-0067" />)

    expect(await screen.findByTestId('thread-terminal-reason')).toHaveTextContent(
      'shipped in c4df66b',
    )
  })
})
