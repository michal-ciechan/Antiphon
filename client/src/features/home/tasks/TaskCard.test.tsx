import { describe, expect, it, vi } from 'vitest'
import type { AgentSummaryDto } from '../../../api/agents'
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
})
