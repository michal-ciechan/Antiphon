import { describe, expect, it } from 'vitest'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent } from '../../test/utils'
import { TaskChip } from './TaskChip'

const task: AgentTaskSummaryDto = {
  id: '77777777-7777-7777-7777-777777777777',
  rootTaskId: '77777777-7777-7777-7777-777777777777',
  parentTaskId: null,
  depth: 0,
  title: 'Recovered task',
  kind: 'Worker',
  role: 'Code',
  agentKind: 'ClaudeCode',
  modelLevel: 'High',
  escalatedFrom: null,
  status: 'Succeeded',
  workspace: 'Shared',
  workingDirectory: 'C:/src/antiphon',
  repoPath: 'C:/src/antiphon',
  worktreePath: null,
  worktreeBranch: null,
  scope: null,
  agentId: null,
  agentName: 'task-77777777',
  agentSessionId: null,
  attempt: 1,
  createdAt: '2026-08-07T10:00:00Z',
  dispatchedAt: '2026-08-07T10:00:00Z',
  completedAt: '2026-08-07T10:12:00Z',
  recoveredAt: '2026-08-07T10:12:00Z',
  tokensIn: 0,
  cacheReadTokens: 0,
  cacheCreationTokens: 0,
  tokensOut: 0,
  costUsd: 0,
  costPricingVersion: 2,
  subtreeCostUsd: 0,
  childCount: 0,
  expectedDurationMinutes: 10,
  nextCheckAt: null,
  checkCount: 0,
}

describe('TaskChip', () => {
  it('chip labels resolved and unscoped tasks', () => {
    const n1 = {
      ...task,
      id: '33333333-3333-3333-3333-333333333331',
      title: 'path twin',
      workingDirectory: 'C:/src/antiphon',
      repoPath: 'C:/src/antiphon',
      scopeSource: 'None' as const,
      projectName: null,
    }
    const resolved = { ...task, projectName: 'Antiphon', scopeSource: 'Task' as const }
    const { rerender } = renderWithProviders(<TaskChip task={resolved} onOpen={() => {}} />)
    expect(screen.getByText('Antiphon')).toBeInTheDocument()
    rerender(<TaskChip task={n1} onOpen={() => {}} />)
    expect(screen.getByText('unscoped')).toBeInTheDocument()
    expect(screen.queryByText('Antiphon')).not.toBeInTheDocument()
  })

  it('shows Any on a Linux host without claiming the task executed', () => {
    renderWithProviders(<TaskChip task={{ ...task, status: 'Queued', requiredPlatform: 'Any', runnerId: 'server2', observedPlatform: 'linux', requirementSource: 'Default', runnerSelectionSource: 'GlobalDefault' }} onOpen={() => {}} />)
    expect(screen.getByTestId('placement')).toHaveTextContent('Any · server2')
    expect(screen.queryByText(/executed/i)).not.toBeInTheDocument()
  })

  it('shows a historical Codex High session model instead of the current tier alias', () => {
    renderWithProviders(
      <TaskChip
        task={{ ...task, agentKind: 'Codex', modelLevel: 'High', effectiveModelId: 'gpt-6-sol' }}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('tier-High')).toHaveTextContent('gpt-6-sol')
  })

  it('shows a historical Codex Medium session model instead of the current tier alias', () => {
    renderWithProviders(
      <TaskChip
        task={{ ...task, agentKind: 'Codex', modelLevel: 'Medium', effectiveModelId: 'gpt-5.6-terra' }}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('tier-Medium')).toHaveTextContent('gpt-5.6-terra')
  })

  it('shows the current tier alias for a new Codex task and when nothing was recorded', () => {
    const { rerender } = renderWithProviders(
      <TaskChip
        task={{ ...task, agentKind: 'Codex', modelLevel: 'High', effectiveModelId: 'gpt-6.1-sol' }}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('tier-High')).toHaveTextContent('gpt-6.1-sol')
    rerender(
      <TaskChip
        task={{ ...task, agentKind: 'Codex', modelLevel: 'Medium', effectiveModelId: null }}
        onOpen={() => {}}
      />,
    )
    expect(screen.getByTestId('tier-Medium')).toHaveTextContent('gpt-6.1-sol')
  })

  it('labels a recovered elapsed time as unobserved', async () => {
    renderWithProviders(<TaskChip task={task} onOpen={() => {}} />)

    const elapsed = screen.getByText('~12m00')
    await userEvent.hover(elapsed)
    expect(await screen.findByText(/recovered from an unbound session - completion was not observed/i)).toBeInTheDocument()
  })
})
