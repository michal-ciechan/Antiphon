import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type {
  AgentTaskPipelineDto,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
} from '../../../api/agentTasks'
import type { AttentionItemDto } from '../../../api/attention'
import type { HomeTaskGroup, HomeTaskItemDto, HomeTaskWorkerDto } from '../../../api/homeTasks'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../../test/utils'
import { server } from '../../../test/mocks/server'
import { normalizeDir } from '../projectGrouping'
import { GROUP_LABEL, GROUP_ORDER } from './homeTasksModel'
import { TasksSection } from './TasksSection'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

vi.mock('./HomeTaskModal', () => ({
  HomeTaskModal: ({ item }: { item: HomeTaskItemDto | null }) =>
    item ? <div data-testid="home-task-modal">{`${item.source}:${item.id}`}</div> : null,
}))

const DIR = 'C:\\src\\antiphon'
const DIR_KEY = normalizeDir(DIR)

function item(overrides: Partial<HomeTaskItemDto> = {}): HomeTaskItemDto {
  return {
    key: 'card:1',
    source: 'Card',
    id: '11111111-0000-0000-0000-000000000001',
    identifier: 'CARD-0001',
    title: 'A card',
    terminalReason: null,
    group: 'Running',
    state: 'InProgress',
    humanReason: null,
    stage: 'Plan',
    workflowRunStatus: null,
    priority: 1,
    boardId: 'board-1',
    worker: null,
    ownerAgentId: null,
    agentKind: null,
    modelLevel: null,
    escalatedFrom: null,
    role: null,
    costUsd: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    readAt: null,
    deliverablePath: null,
    deliverableRef: null,
    workingDirectory: DIR,
    repoPath: null,
    worktreePath: null,
    createdAt: '2026-09-01T10:00:00Z',
    startedAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    ...overrides,
  }
}

function worker(overrides: Partial<HomeTaskWorkerDto> = {}): HomeTaskWorkerDto {
  return {
    taskId: 'aaaaaaaa-0000-0000-0000-000000000006',
    shortId: 'aaaaaaaa',
    role: 'Plan',
    status: 'Dispatched',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    agentId: 'agent-1',
    agentName: 'task-bound',
    agentSessionId: null,
    costUsd: 0.11,
    dispatchedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    ...overrides,
  }
}

function attentionItem(overrides: Partial<AttentionItemDto> = {}): AttentionItemDto {
  return {
    kind: 'DeadSession',
    severity: 'Error',
    taskId: 'aaaaaaaa-0000-0000-0000-000000000006',
    sessionId: null,
    agentId: null,
    messageId: null,
    cardId: null,
    title: 'Dead session',
    headline: 'Dead session',
    evidence: 'session is gone',
    sinceUtc: '2026-09-01T10:00:00Z',
    subtreeCostUsd: null,
    actions: [],
    ...overrides,
  }
}

function emptyPipeline(): AgentTaskPipelineDto {
  return {
    asOf: '2026-09-02T00:00:00Z',
    recommendationsAreAdvisory: true,
    maxConcurrentTasks: 6,
    inFlightAgainstCap: 0,
    stages: [],
  }
}

function stage(overrides: Partial<AgentTaskPipelineStageDto> = {}): AgentTaskPipelineStageDto {
  return {
    role: 'Code',
    recommendedInFlight: 1,
    inFlightCount: 0,
    atOrAboveRecommendation: false,
    inFlight: [],
    queued: [],
    blocked: [],
    ready: [],
    routingPin: null,
    ...overrides,
  }
}

function queuedRow(overrides: Partial<AgentTaskPipelineQueuedDto> = {}): AgentTaskPipelineQueuedDto {
  return {
    taskId: 'bbbbbbbb-0000-0000-0000-000000000007',
    shortId: 'bbbbbbbb',
    title: 'queued work',
    card: null,
    createdAt: '2026-09-01T11:00:00Z',
    queueReason: 'sharedCheckoutLease',
    heldBy: [{ taskId: 'hold-1', shortId: '1a2b3c4d', title: 'in-flight docs pass' }],
    ...overrides,
  }
}

function readyRow(overrides: Partial<AgentTaskPipelineReadyDto> = {}): AgentTaskPipelineReadyDto {
  return {
    card: {
      id: '11111111-0000-0000-0000-000000000001',
      identifier: 'CARD-0001',
      title: 'A card',
    },
    sourcePlanTaskId: 'cccccccc-0000-0000-0000-000000000003',
    sourcePlanShortId: 'cccccccc',
    readySince: '2026-08-26T11:00:00Z',
    deliverablePath: 'docs/superpowers/plans/example.md',
    deliverableRef: 'abc',
    routingPin: null,
    ...overrides,
  }
}

function seed({
  items = [] as HomeTaskItemDto[],
  status = 200,
  attention = [] as AttentionItemDto[],
  attentionStatus = 200,
  pipeline = emptyPipeline() as AgentTaskPipelineDto | null,
  pipelineStatus = 200,
}: {
  items?: HomeTaskItemDto[]
  status?: number
  attention?: AttentionItemDto[]
  attentionStatus?: number
  pipeline?: AgentTaskPipelineDto | null
  pipelineStatus?: number
} = {}) {
  server.use(
    http.get('/api/home/tasks', () =>
      status === 200
        ? HttpResponse.json({ generatedAt: '2026-09-02T00:00:00Z', items })
        : new HttpResponse(null, { status }),
    ),
    http.get('/api/agents', () => HttpResponse.json([])),
    http.get('/api/attention', () =>
      attentionStatus === 200
        ? HttpResponse.json({
            generatedAt: '2026-09-02T00:00:00Z',
            runnerConsulted: true,
            items: attention,
          })
        : new HttpResponse(null, { status: attentionStatus }),
    ),
    http.get('/api/agent-tasks/pipeline', () =>
      pipelineStatus === 200
        ? HttpResponse.json(pipeline)
        : new HttpResponse(null, { status: pipelineStatus }),
    ),
  )
}

function renderSection(dirKeys = [DIR_KEY]) {
  return renderWithProviders(<TasksSection dirKeys={dirKeys} />)
}

describe('TasksSection', () => {
  it('renders the five groups in order with counts', async () => {
    seed({
      items: [
        item({
          key: 'need',
          id: 'need',
          identifier: 'CARD-0001',
          title: 'Needs a decision',
          group: 'NeedsHuman',
          state: 'NeedsDecision',
          humanReason: 'Decision',
        }),
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
        }),
        item({
          key: 'review',
          id: 'review',
          identifier: 'CARD-0003',
          title: 'Review card',
          group: 'Review',
          state: 'Review',
          humanReason: 'Review',
        }),
        item({
          key: 'next',
          id: 'next',
          identifier: 'CARD-0004',
          title: 'Backlog card',
          group: 'Next',
          state: 'Backlog',
          stage: null,
        }),
        item({
          key: 'done',
          id: 'done',
          identifier: 'CARD-0005',
          title: 'Finished card',
          group: 'Done',
          state: 'Done',
          stage: null,
          completedAt: '2026-09-01T18:00:00Z',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Needs a decision')

    const labels = GROUP_ORDER.map((group) => {
      const header = screen.getByTestId(`home-tasks-group-${group}`)
      expect(header).toHaveTextContent(GROUP_LABEL[group])
      expect(within(header).getByText('1')).toBeInTheDocument()
      return header
    })
    for (let i = 1; i < labels.length; i++) {
      expect(labels[i - 1].compareDocumentPosition(labels[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
  })

  it('always shows Needs-you and Running, with an empty line when they have no items', async () => {
    seed({
      items: [
        item({
          key: 'review',
          id: 'review',
          identifier: 'CARD-0003',
          title: 'Review card',
          group: 'Review',
          state: 'Review',
          humanReason: 'Review',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Review card')
    expect(screen.getByTestId('home-tasks-group-NeedsHuman')).toHaveTextContent('0')
    expect(screen.getByText('Nothing needs you.')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Running')).toHaveTextContent('0')
    expect(screen.getByText('Nothing running.')).toBeInTheDocument()
  })

  it('omits To review, Up next and Done when those groups are empty', async () => {
    seed({
      items: [
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Running card')
    expect(screen.queryByText('To review')).not.toBeInTheDocument()
    expect(screen.queryByText('Up next')).not.toBeInTheDocument()
    expect(screen.queryByText('Done')).not.toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-NeedsHuman')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Running')).toBeInTheDocument()
  })

  it('shows a +N more link when a capped group is cut', async () => {
    seed({
      items: [
        ...Array.from({ length: 9 }, (_, i) =>
          item({
            key: `review-${i}`,
            id: `review-${i}`,
            identifier: `CARD-${String(i + 1).padStart(4, '0')}`,
            title: `Review item ${i}`,
            group: 'Review' as HomeTaskGroup,
            state: 'Review',
            humanReason: 'Review',
          }),
        ),
        ...Array.from({ length: 13 }, (_, i) =>
          item({
            key: `done-${i}`,
            id: `done-${i}`,
            source: 'Delegation',
            identifier: `done${String(i).padStart(2, '0')}`,
            title: `Done item ${i}`,
            group: 'Done',
            state: 'Succeeded',
            boardId: null,
            stage: 'Docs',
            role: 'Docs',
            modelLevel: 'Medium',
            agentKind: 'ClaudeCode',
            completedAt: '2026-09-01T18:00:00Z',
          }),
        ),
      ],
    })
    renderSection()

    await screen.findByText('Review item 0')
    expect(screen.getByText('Review item 7')).toBeInTheDocument()
    expect(screen.queryByText('Review item 8')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: '+1 more → open board' })).toHaveAttribute(
      'href',
      '/boards/board-1',
    )

    expect(screen.getByText('Done item 0')).toBeInTheDocument()
    expect(screen.getByText('Done item 11')).toBeInTheDocument()
    expect(screen.queryByText('Done item 12')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: '+1 more → open delegations' })).toHaveAttribute(
      'href',
      '/orchestrator?tab=delegations',
    )
  })

  it('filters out items whose directories are not in dirKeys', async () => {
    seed({
      items: [
        item({
          key: 'mine',
          id: 'mine',
          identifier: 'CARD-0002',
          title: 'In this project',
          group: 'Running',
        }),
        item({
          key: 'other',
          id: 'other',
          identifier: 'CARD-0099',
          title: 'Other project card',
          group: 'Running',
          workingDirectory: 'C:\\src\\other',
          repoPath: null,
          worktreePath: null,
        }),
      ],
    })
    renderSection()

    await screen.findByText('In this project')
    expect(screen.queryByText('Other project card')).not.toBeInTheDocument()
  })

  it('shows a dimmed one-liner when the projection fails', async () => {
    seed({ status: 500 })
    renderSection()

    expect(
      await screen.findByText('Tasks are unavailable — the server did not answer for them.'),
    ).toBeInTheDocument()
    expect(screen.queryByTestId('home-tasks-group-NeedsHuman')).not.toBeInTheDocument()
  })

  it('opens the modal routed by source when a card is clicked', async () => {
    const cardId = '11111111-0000-0000-0000-000000000001'
    const taskId = 'aaaaaaaa-0000-0000-0000-000000000004'
    seed({
      items: [
        item({
          key: `card:${cardId}`,
          id: cardId,
          identifier: 'CARD-0002',
          title: 'Tasks section on the home rail',
          group: 'Running',
        }),
        item({
          key: `task:${taskId}`,
          source: 'Delegation',
          id: taskId,
          identifier: '15c3cb72',
          title: 'Rewrite the Windows install section',
          group: 'NeedsHuman',
          state: 'Blocked',
          humanReason: 'Question',
          boardId: null,
          stage: 'Docs',
          role: 'Docs',
          modelLevel: 'Medium',
          agentKind: 'ClaudeCode',
        }),
      ],
    })
    renderSection()

    await userEvent.click(await screen.findByRole('button', { name: 'Open CARD-0002' }))
    expect(screen.getByTestId('home-task-modal')).toHaveTextContent(`Card:${cardId}`)

    await userEvent.click(screen.getByRole('button', { name: 'Open 15c3cb72' }))
    expect(screen.getByTestId('home-task-modal')).toHaveTextContent(`Delegation:${taskId}`)
  })

  it('exposes #home-tasks-done as the ToReadBadge scroll target', async () => {
    seed({
      items: [
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Running card')
    expect(document.getElementById('home-tasks-done')).not.toBeNull()
  })

  it('keeps #home-tasks-done on the Done group when it has items', async () => {
    seed({
      items: [
        item({
          key: 'done',
          id: 'done',
          identifier: 'CARD-0005',
          title: 'Finished card',
          group: 'Done',
          state: 'Done',
          completedAt: '2026-09-01T18:00:00Z',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Finished card')
    await waitFor(() => {
      const target = document.getElementById('home-tasks-done')
      expect(target).not.toBeNull()
      expect(target).toHaveTextContent('Finished card')
    })
  })

  it('keeps a Running item with a Dead session badge under Running', async () => {
    seed({
      items: [
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
          worker: worker(),
        }),
      ],
      attention: [attentionItem({ kind: 'DeadSession' })],
    })
    renderSection()

    await screen.findByText('Running card')
    expect(screen.getByTestId('task-liveness-DeadSession')).toHaveTextContent('Dead session')
    expect(within(screen.getByTestId('home-tasks-group-Running')).getByText('1')).toBeInTheDocument()
    expect(within(screen.getByTestId('home-tasks-group-NeedsHuman')).getByText('0')).toBeInTheDocument()
    expect(screen.queryByText('Needs you')).toBeInTheDocument()
  })

  it('shows the queued-delegation lease line naming the holder', async () => {
    const queuedId = 'bbbbbbbb-0000-0000-0000-000000000007'
    seed({
      items: [
        item({
          key: `task:${queuedId}`,
          source: 'Delegation',
          id: queuedId,
          identifier: 'bbbbbbbb',
          title: 'Queued behind checkout',
          group: 'Next',
          state: 'Queued',
          boardId: null,
          stage: 'Code',
          role: 'Code',
          modelLevel: 'High',
          agentKind: 'ClaudeCode',
        }),
      ],
      pipeline: {
        ...emptyPipeline(),
        stages: [stage({ queued: [queuedRow()] })],
      },
    })
    renderSection()

    await screen.findByText('Queued behind checkout')
    expect(screen.getByTestId(`task-queue-${queuedId}`)).toHaveTextContent(
      'waiting: shared checkout held by task-1a2b3c4d — in-flight docs pass',
    )
  })

  it('shows the ready-card line under Up next', async () => {
    const cardId = '11111111-0000-0000-0000-000000000001'
    seed({
      items: [
        item({
          key: `card:${cardId}`,
          id: cardId,
          identifier: 'CARD-0001',
          title: 'Ready for Code',
          group: 'Next',
          state: 'Backlog',
          stage: 'Plan',
        }),
      ],
      pipeline: {
        ...emptyPipeline(),
        stages: [stage({ ready: [readyRow()] })],
      },
    })
    renderSection()

    await screen.findByText('Ready for Code')
    expect(screen.getByTestId(`task-ready-${cardId}`)).toHaveTextContent('ready for Code')
    expect(screen.getByTestId(`task-ready-read-${cardId}`)).toHaveAttribute(
      'href',
      `/plans?${new URLSearchParams({
        file: 'docs/superpowers/plans/example.md',
        ref: 'abc',
        task: 'cccccccc-0000-0000-0000-000000000003',
      }).toString()}`,
    )
  })

  it('degrades a pipeline 500 into no enrichment and no extra error line', async () => {
    const queuedId = 'bbbbbbbb-0000-0000-0000-000000000007'
    const cardId = '11111111-0000-0000-0000-000000000001'
    seed({
      items: [
        item({
          key: 'need',
          id: 'need',
          identifier: 'CARD-0001',
          title: 'Needs a decision',
          group: 'NeedsHuman',
          state: 'NeedsDecision',
          humanReason: 'Decision',
        }),
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
          worker: worker(),
        }),
        item({
          key: 'review',
          id: 'review',
          identifier: 'CARD-0003',
          title: 'Review card',
          group: 'Review',
          state: 'Review',
          humanReason: 'Review',
        }),
        item({
          key: `task:${queuedId}`,
          source: 'Delegation',
          id: queuedId,
          identifier: 'bbbbbbbb',
          title: 'Queued behind checkout',
          group: 'Next',
          state: 'Queued',
          boardId: null,
          stage: 'Code',
          role: 'Code',
          modelLevel: 'High',
          agentKind: 'ClaudeCode',
        }),
        item({
          key: `card:${cardId}`,
          id: cardId,
          identifier: 'CARD-0033',
          title: 'Ready for Code',
          group: 'Next',
          state: 'Backlog',
          stage: 'Plan',
        }),
        item({
          key: 'done',
          id: 'done',
          identifier: 'CARD-0005',
          title: 'Finished card',
          group: 'Done',
          state: 'Done',
          terminalReason: 'Closed with a verdict.',
          completedAt: '2026-09-01T18:00:00Z',
        }),
      ],
      pipelineStatus: 500,
    })
    renderSection()

    await screen.findByText('Running card')
    expect(screen.getByTestId('home-tasks-group-NeedsHuman')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Running')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Review')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Next')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Done')).toBeInTheDocument()
    expect(screen.queryByTestId(`task-queue-${queuedId}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId(`task-ready-${cardId}`)).not.toBeInTheDocument()
    expect(
      screen.queryByText('Tasks are unavailable — the server did not answer for them.'),
    ).not.toBeInTheDocument()
    expect(screen.getByTestId('task-terminal-done')).toHaveTextContent('Closed with a verdict.')
  })

  it('degrades an attention 500 into no badges and no question line, rail intact', async () => {
    seed({
      items: [
        item({
          key: 'need',
          id: 'need',
          identifier: 'CARD-0001',
          title: 'Needs a decision',
          group: 'NeedsHuman',
          state: 'NeedsDecision',
          humanReason: 'Decision',
        }),
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
          worker: worker(),
        }),
      ],
      attention: [
        attentionItem({ kind: 'DeadSession' }),
        attentionItem({
          kind: 'CardNeedsDecision',
          taskId: null,
          cardId: 'need',
          evidence: 'Should we ship on Friday?',
        }),
      ],
      attentionStatus: 500,
    })
    renderSection()

    await screen.findByText('Running card')
    expect(screen.getByText('Needs a decision')).toBeInTheDocument()
    expect(screen.queryByTestId('task-liveness-DeadSession')).not.toBeInTheDocument()
    expect(screen.queryByText('Should we ship on Friday?')).not.toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-NeedsHuman')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Running')).toBeInTheDocument()
    expect(
      screen.queryByText('Tasks are unavailable — the server did not answer for them.'),
    ).not.toBeInTheDocument()
  })
})
