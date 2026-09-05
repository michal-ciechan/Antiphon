import { describe, expect, it, vi } from 'vitest'
import type { AgentSummaryDto } from '../../../api/agents'
import type {
  AgentTaskPipelineDto,
  AgentTaskPipelineInFlightDto,
  AgentTaskPipelineQueueReason,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
  RoutingPinRefDto,
} from '../../../api/agentTasks'
import type { AttentionItemDto } from '../../../api/attention'
import type { HomeTaskItemDto, HomeTaskWorkerDto } from '../../../api/homeTasks'
import { renderWithProviders, screen, userEvent } from '../../../test/utils'
import { TaskCard } from './TaskCard'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

function worker(overrides: Partial<HomeTaskWorkerDto> = {}): HomeTaskWorkerDto {
  return {
    taskId: 'aaaaaaaa-0000-0000-0000-000000000006',
    shortId: 'aaaaaaaa',
    role: 'Plan',
    status: 'Working',
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

function item(overrides: Partial<HomeTaskItemDto> = {}): HomeTaskItemDto {
  return {
    key: 'card:1',
    source: 'Card',
    id: '11111111-0000-0000-0000-000000000001',
    identifier: 'CARD-0002',
    title: 'Tasks section on the home rail',
    terminalReason: null,
    group: 'Running',
    state: 'InProgress',
    humanReason: null,
    stage: 'Plan',
    workflowRunStatus: null,
    importance: 'High', effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 7, urgentSince: null,
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
    workingDirectory: 'C:\\src\\antiphon',
    repoPath: null,
    worktreePath: null,
    createdAt: '2026-09-01T10:00:00Z',
    startedAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    ...overrides,
  }
}

function agent(overrides: Partial<AgentSummaryDto> = {}): AgentSummaryDto {
  return {
    id: 'agent-1',
    name: 'task-bound',
    slug: 'task-bound',
    workingDirectory: 'C:\\src\\antiphon',
    details: '',
    defaultWorkflowTemplateId: null,
    defaultWorkflowTemplateName: null,
    assignmentPolicy: 'AutoPick',
    status: 'Running',
    persistentSessionId: null,
    currentCardId: null,
    boardId: null,
    boardName: null,
    queueLength: 0,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    liveSession: {
      id: 'session-1',
      definitionName: 'test-claude',
      agentKind: 'ClaudeCode',
      status: 'Running',
      cwd: 'C:\\src\\antiphon',
      createdAt: '2026-09-01T00:00:00Z',
      startedAt: '2026-09-01T00:00:00Z',
      lastSeenAt: '2026-09-01T00:00:00Z',
      endedAt: null,
      exitCode: null,
      failureReason: null,
    },
    alwaysOn: false,
    remoteControlEnabled: false,
    supervision: null,
    systemPromptAppend: null,
    modelLevel: 'High',
    working: true,
    ...overrides,
  }
}

describe('TaskCard', () => {
  it('shows the identifier and a Card or Task chip', () => {
    const { rerender } = renderWithProviders(<TaskCard item={item()} onOpen={() => {}} />)
    expect(screen.getByText('CARD-0002')).toBeInTheDocument()
    expect(screen.getByText('Card')).toBeInTheDocument()

    rerender(
      <TaskCard
        item={item({
          source: 'Delegation',
          identifier: '15c3cb72',
          state: 'Working',
          group: 'Running',
          modelLevel: 'Medium',
          agentKind: 'ClaudeCode',
          role: 'Docs',
          costUsd: 0.031,
        })}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByText('15c3cb72')).toBeInTheDocument()
    expect(screen.getByText('Task')).toBeInTheDocument()
  })

  it('prints Needs decision via stateLabel', () => {
    renderWithProviders(
      <TaskCard
        item={item({ state: 'NeedsDecision', group: 'NeedsHuman', humanReason: 'Decision', stage: null })}
        onOpen={() => {}}
      />,
    )
    expect(screen.getAllByText('Needs decision').length).toBeGreaterThan(0)
  })

  it('shows a stage line for cards and tier plus cost for tasks', () => {
    const { rerender } = renderWithProviders(<TaskCard item={item({ stage: 'Plan' })} onOpen={() => {}} />)
    expect(screen.getByText(/stage: plan/i)).toBeInTheDocument()

    rerender(
      <TaskCard
        item={item({
          source: 'Delegation',
          state: 'Working',
          stage: 'Docs',
          modelLevel: 'Medium',
          agentKind: 'ClaudeCode',
          role: 'Docs',
          costUsd: 0.031,
        })}
        onOpen={() => {}}
      />,
    )
    expect(screen.queryByText(/stage:/i)).not.toBeInTheDocument()
    expect(screen.getByTestId('tier-Medium')).toBeInTheDocument()
    expect(screen.getByText('$0.03')).toBeInTheDocument()
  })

  it('renders the question line', () => {
    renderWithProviders(
      <TaskCard
        item={item({ group: 'NeedsHuman', humanReason: 'Decision', state: 'NeedsDecision' })}
        question="Should validation errors block save?"
        onOpen={() => {}}
      />,
    )
    expect(screen.getByText('Should validation errors block save?')).toBeInTheDocument()
  })

  it('marks needs-you with a danger border and a reason badge', () => {
    const { container } = renderWithProviders(
      <TaskCard
        item={item({ group: 'NeedsHuman', humanReason: 'Question', state: 'InProgress', worker: worker({ status: 'Blocked' }) })}
        onOpen={() => {}}
      />,
    )
    const paper = container.querySelector('.mantine-Paper-root') as HTMLElement
    expect(paper.style.borderLeft).toContain('danger')
    expect(screen.getByText('Question')).toBeInTheDocument()
  })

  it('renders the worker line and fires onOpenTask and onSelectAgent', async () => {
    const onOpen = vi.fn()
    const onOpenTask = vi.fn()
    const onSelectAgent = vi.fn()
    renderWithProviders(
      <TaskCard
        item={item({ worker: worker({ agentId: 'agent-1', agentName: 'task-bound', shortId: 'aaaaaaaa' }) })}
        agents={[agent({ id: 'agent-1', name: 'task-bound', working: true })]}
        onOpen={onOpen}
        onOpenTask={onOpenTask}
        onSelectAgent={onSelectAgent}
      />,
    )

    expect(screen.getByText('task-bound')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Open delegation aaaaaaaa' }))
    expect(onOpenTask).toHaveBeenCalledWith('aaaaaaaa-0000-0000-0000-000000000006')
    expect(onOpen).not.toHaveBeenCalled()

    await userEvent.click(screen.getByText('task-bound'))
    expect(onSelectAgent).toHaveBeenCalledWith('agent-1')
  })

  it('shows an unread dot and Read link on a Done task with a deliverable', () => {
    const done = item({
      source: 'Delegation',
      id: 'task-done',
      identifier: 'abcd1234',
      group: 'Done',
      state: 'Succeeded',
      role: 'Plan',
      modelLevel: 'High',
      agentKind: 'ClaudeCode',
      costUsd: 0.2,
      readAt: null,
      deliverablePath: 'docs/plan.md',
      deliverableRef: 'abc',
      completedAt: new Date().toISOString(),
    })
    renderWithProviders(<TaskCard item={done} onOpen={() => {}} />)

    expect(screen.getByTestId('task-unread-task-done')).toBeInTheDocument()
    const read = screen.getByTestId('task-read-task-done')
    expect(read).toHaveAttribute(
      'href',
      `/plans?${new URLSearchParams({ file: 'docs/plan.md', ref: 'abc', task: 'task-done' }).toString()}`,
    )
    expect(read).toHaveTextContent('Read')
  })

  it('offers Spawn on a spawnable card and not Answer', async () => {
    renderWithProviders(
      <TaskCard
        item={item({
          source: 'Card',
          state: 'Backlog',
          group: 'Next',
          stage: null,
          worker: null,
          ownerAgentId: null,
        })}
        onOpen={() => {}}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Task menu CARD-0002' }))
    expect(await screen.findByRole('menuitem', { name: 'Spawn session' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Answer…' })).not.toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Open thread' })).toHaveAttribute('href', '/thread/card-2')
    expect(screen.getByRole('menuitem', { name: 'Open board' })).toHaveAttribute(
      'href',
      '/boards/board-1?card=11111111-0000-0000-0000-000000000001',
    )
  })

  it('offers Answer on a Blocked task and not Spawn', async () => {
    renderWithProviders(
      <TaskCard
        item={item({
          source: 'Delegation',
          identifier: '15c3cb72',
          state: 'Blocked',
          group: 'NeedsHuman',
          humanReason: 'Question',
          modelLevel: 'High',
          agentKind: 'ClaudeCode',
          role: 'Docs',
          boardId: null,
        })}
        onOpen={() => {}}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Task menu 15c3cb72' }))
    expect(await screen.findByRole('menuitem', { name: 'Answer…' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Spawn session' })).not.toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Open delegations' })).toBeInTheDocument()
  })

  it('does not offer Answer on a Working task', async () => {
    renderWithProviders(
      <TaskCard
        item={item({
          source: 'Delegation',
          identifier: '15c3cb72',
          state: 'Working',
          group: 'Running',
          modelLevel: 'High',
          agentKind: 'ClaudeCode',
          role: 'Docs',
          boardId: null,
        })}
        onOpen={() => {}}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Task menu 15c3cb72' }))
    expect(await screen.findByRole('menuitem', { name: 'Open' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Answer…' })).not.toBeInTheDocument()
  })

  it('does not fire onOpen when the kebab is clicked', async () => {
    const onOpen = vi.fn()
    renderWithProviders(<TaskCard item={item()} onOpen={onOpen} />)

    await userEvent.click(screen.getByRole('button', { name: 'Task menu CARD-0002' }))
    expect(await screen.findByRole('menuitem', { name: 'Open' })).toBeInTheDocument()
    expect(onOpen).not.toHaveBeenCalled()
  })

  const NOW = Date.parse('2026-02-03T09:14:00Z')

  function attention(overrides: Partial<AttentionItemDto> = {}): AttentionItemDto {
    return {
      kind: 'Overdue',
      severity: 'Warning',
      taskId: 'aaaaaaaa-0000-0000-0000-000000000006',
      sessionId: null,
      agentId: null,
      messageId: null,
      cardId: null,
      title: 'Overdue',
      headline: 'Overdue',
      evidence: 'past the deadline',
      sinceUtc: '2026-02-03T08:00:00Z',
      subtreeCostUsd: null,
      actions: [],
      ...overrides,
    }
  }

  function pipeline(overrides: Partial<AgentTaskPipelineDto> = {}): AgentTaskPipelineDto {
    return {
      asOf: '2026-02-03T09:00:00Z',
      recommendationsAreAdvisory: true,
      maxConcurrentTasks: 6,
      inFlightAgainstCap: 6,
      stages: [],
      ...overrides,
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

  function inFlightRow(
    overrides: Partial<AgentTaskPipelineInFlightDto> = {},
  ): AgentTaskPipelineInFlightDto {
    return {
      taskId: 'aaaaaaaa-0000-0000-0000-000000000006',
      shortId: 'aaaaaaaa',
      title: 'in flight',
      status: 'Dispatched',
      card: null,
      agentName: 'task-bound',
      dispatchedAt: '2026-02-03T07:00:00Z',
      lastActivityAt: '2026-02-03T09:11:00Z',
      ...overrides,
    }
  }

  function queuedRow(
    overrides: Partial<AgentTaskPipelineQueuedDto> = {},
  ): AgentTaskPipelineQueuedDto {
    return {
      taskId: 'bbbbbbbb-0000-0000-0000-000000000007',
      shortId: 'bbbbbbbb',
      title: 'queued work',
      card: null,
      createdAt: '2026-02-03T08:50:00Z',
      queueReason: 'awaitingDispatch',
      heldBy: [],
      ...overrides,
    }
  }

  function readyRow(overrides: Partial<AgentTaskPipelineReadyDto> = {}): AgentTaskPipelineReadyDto {
    return {
      card: {
        id: '11111111-0000-0000-0000-000000000001',
        identifier: 'CARD-0002',
        title: 'Tasks section on the home rail',
      },
      sourcePlanTaskId: 'cccccccc-0000-0000-0000-000000000003',
      sourcePlanShortId: 'cccccccc',
      readySince: '2026-01-31T11:00:00Z',
      deliverablePath: 'docs/superpowers/plans/example.md',
      deliverableRef: 'abc',
      routingPin: null,
      ...overrides,
    }
  }

  function pin(overrides: Partial<RoutingPinRefDto> = {}): RoutingPinRefDto {
    return {
      id: 'pin-1',
      cardId: null,
      cardIdentifier: null,
      role: 'Code',
      provenance: 'Auto',
      strength: 'Required',
      agentKind: null,
      modelLevel: null,
      notBefore: '2026-02-03T14:00:00Z',
      reason: 'test',
      ...overrides,
    }
  }

  it('renders the liveness visual label, not the raw kind or worker status', () => {
    renderWithProviders(
      <TaskCard
        item={item({ worker: worker({ status: 'Dispatched' }) })}
        liveness={attention({ kind: 'DeadSession' })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('task-liveness-DeadSession')).toHaveTextContent('Dead session')
    expect(screen.queryByText('DeadSession')).not.toBeInTheDocument()
    expect(screen.getByText('Dispatched')).toBeInTheDocument()
  })

  it('keeps the Working spinner beside an Overdue verdict', () => {
    renderWithProviders(
      <TaskCard
        item={item({ worker: worker({ status: 'Working' }) })}
        agents={[agent({ working: true })]}
        liveness={attention({ kind: 'Overdue' })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByText('Working')).toBeInTheDocument()
    expect(screen.getByTestId('task-liveness-Overdue')).toHaveTextContent('Overdue')
  })

  it('prints elapsed on a Running card with a Dispatched worker and on a Running delegation, not Up next or Done', () => {
    const { rerender } = renderWithProviders(
      <TaskCard
        item={item({
          worker: worker({ status: 'Dispatched', dispatchedAt: '2026-02-03T07:00:00Z' }),
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('task-elapsed-11111111-0000-0000-0000-000000000001')).toHaveTextContent(
      '2h14m',
    )

    rerender(
      <TaskCard
        item={item({
          source: 'Delegation',
          id: 'task-run',
          identifier: '15c3cb72',
          group: 'Running',
          state: 'Working',
          worker: null,
          startedAt: '2026-02-03T07:00:00Z',
          createdAt: '2026-02-03T06:00:00Z',
          modelLevel: 'High',
          agentKind: 'ClaudeCode',
          role: 'Docs',
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('task-elapsed-task-run')).toHaveTextContent('2h14m')

    rerender(
      <TaskCard
        item={item({
          group: 'Next',
          state: 'Backlog',
          worker: worker({ status: 'Queued', dispatchedAt: '2026-02-03T07:00:00Z' }),
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.queryByTestId('task-elapsed-11111111-0000-0000-0000-000000000001')).not.toBeInTheDocument()

    rerender(
      <TaskCard
        item={item({
          group: 'Done',
          state: 'Done',
          terminalReason: 'Closed.',
          worker: worker({ status: 'Succeeded', completedAt: '2026-02-03T08:00:00Z' }),
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.queryByTestId('task-elapsed-11111111-0000-0000-0000-000000000001')).not.toBeInTheDocument()
  })

  it('prints active Xm ago only when the pipeline row has lastActivityAt', () => {
    const running = item({
      worker: worker({ status: 'Dispatched', dispatchedAt: '2026-02-03T07:00:00Z' }),
    })
    const { rerender } = renderWithProviders(
      <TaskCard item={running} now={NOW} onOpen={() => {}} />,
    )
    expect(screen.getByTestId(`task-elapsed-${running.id}`)).toHaveTextContent('2h14m')
    expect(screen.getByTestId(`task-elapsed-${running.id}`)).not.toHaveTextContent('active')

    rerender(
      <TaskCard item={running} pipelineRow={inFlightRow()} now={NOW} onOpen={() => {}} />,
    )
    expect(screen.getByTestId(`task-elapsed-${running.id}`)).toHaveTextContent('2h14m · active 3m ago')
  })

  it.each<[AgentTaskPipelineQueueReason, string, Partial<AgentTaskPipelineQueuedDto>, Partial<AgentTaskPipelineStageDto>]>([
    [
      'sharedCheckoutLease',
      'waiting: shared checkout held by task-1a2b3c4d — in-flight docs pass +1',
      {
        heldBy: [
          { taskId: 'hold-1', shortId: '1a2b3c4d', title: 'in-flight docs pass' },
          { taskId: 'hold-2', shortId: 'other001', title: 'second' },
        ],
      },
      {},
    ],
    ['concurrencyCap', 'waiting: 6 of 6 task slots in use', {}, {}],
    ['routingPinNotBefore', 'waiting: not before 14:00 (routing pin)', {}, { routingPin: pin() }],
    ['awaitingDispatch', 'queued — next dispatch tick', {}, {}],
  ])('prints the %s queue-reason line', (reason, line, queuedExtras, stageExtras) => {
    const queued = item({
      source: 'Delegation',
      id: 'bbbbbbbb-0000-0000-0000-000000000007',
      identifier: 'bbbbbbbb',
      group: 'Next',
      state: 'Queued',
      worker: null,
      modelLevel: 'High',
      agentKind: 'ClaudeCode',
      role: 'Code',
    })
    const pipe = pipeline({
      stages: [
        stage({
          queued: [queuedRow({ queueReason: reason, ...queuedExtras })],
          ...stageExtras,
        }),
      ],
    })
    renderWithProviders(<TaskCard item={queued} pipeline={pipe} now={NOW} onOpen={() => {}} />)
    expect(screen.getByTestId(`task-queue-${queued.id}`)).toHaveTextContent(line)
  })

  it('renders the ready line and Read link without firing onOpen', async () => {
    const onOpen = vi.fn()
    const card = item({ group: 'Next', state: 'Backlog', worker: null, stage: 'Plan' })
    const pipe = pipeline({ stages: [stage({ ready: [readyRow()] })] })
    renderWithProviders(
      <TaskCard item={card} pipeline={pipe} now={NOW} onOpen={onOpen} />,
    )

    expect(screen.getByTestId(`task-ready-${card.id}`)).toHaveTextContent(
      'plan landed 2d ago — ready for Code',
    )
    const read = screen.getByTestId(`task-ready-read-${card.id}`)
    expect(read).toHaveAttribute(
      'href',
      `/plans?${new URLSearchParams({
        file: 'docs/superpowers/plans/example.md',
        ref: 'abc',
        task: 'cccccccc-0000-0000-0000-000000000003',
      }).toString()}`,
    )
    await userEvent.click(read)
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('renders pin: fable +2 on a ready row with a multi-candidate pin', () => {
    const card = item({ group: 'Next', state: 'Backlog', worker: null, stage: 'Plan' })
    const pipe = pipeline({
      stages: [
        stage({
          ready: [
            readyRow({
              routingPin: pin({
                agentKind: 'ClaudeCode',
                modelLevel: 'Frontier',
                candidateCount: 3,
              }),
            }),
          ],
        }),
      ],
    })
    renderWithProviders(<TaskCard item={card} pipeline={pipe} now={NOW} onOpen={() => {}} />)
    expect(screen.getByTestId(`task-ready-pin-${card.id}`)).toHaveTextContent('pin: fable +2')
  })

  it('prints terminalReason first line on a Done card only', () => {
    const { rerender } = renderWithProviders(
      <TaskCard
        item={item({
          group: 'Done',
          state: 'Done',
          terminalReason: '\n\n  Fixed and merged to master.\nMore detail',
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('task-terminal-11111111-0000-0000-0000-000000000001')).toHaveTextContent(
      'Fixed and merged to master.',
    )

    rerender(
      <TaskCard
        item={item({
          source: 'Delegation',
          id: 'task-done',
          identifier: 'abcd1234',
          group: 'Done',
          state: 'Succeeded',
          role: 'Plan',
          modelLevel: 'High',
          agentKind: 'ClaudeCode',
          terminalReason: 'Should not show',
          readAt: '2026-02-03T09:00:00Z',
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.queryByTestId('task-terminal-task-done')).not.toBeInTheDocument()
    expect(screen.queryByText('Should not show')).not.toBeInTheDocument()

    rerender(
      <TaskCard
        item={item({
          group: 'Running',
          terminalReason: 'Should not show on Running',
        })}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.queryByText('Should not show on Running')).not.toBeInTheDocument()
  })

  it('leaves a Needs-you item visually unchanged — no elapsed, queue, ready, terminal, or verdict', () => {
    const needsYou = item({
      group: 'NeedsHuman',
      state: 'NeedsDecision',
      humanReason: 'Decision',
      stage: null,
      worker: null,
      terminalReason: 'not for this group',
    })
    const pipe = pipeline({
      stages: [
        stage({
          queued: [queuedRow({ taskId: needsYou.id, queueReason: 'awaitingDispatch' })],
          ready: [readyRow({ card: { id: needsYou.id, identifier: 'CARD-0002', title: needsYou.title } })],
        }),
      ],
    })
    renderWithProviders(
      <TaskCard
        item={needsYou}
        liveness={attention({ kind: 'Overdue', taskId: needsYou.id })}
        pipelineRow={inFlightRow({ taskId: needsYou.id })}
        pipeline={pipe}
        now={NOW}
        onOpen={() => {}}
      />,
    )
    expect(screen.getAllByText('Needs decision').length).toBeGreaterThan(0)
    expect(screen.queryByTestId(`task-elapsed-${needsYou.id}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId(`task-queue-${needsYou.id}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId(`task-ready-${needsYou.id}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId(`task-terminal-${needsYou.id}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId('task-liveness-Overdue')).not.toBeInTheDocument()
    expect(screen.queryByText(/waiting:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/ready for Code/)).not.toBeInTheDocument()
    expect(screen.queryByText(/active /)).not.toBeInTheDocument()
  })
})
