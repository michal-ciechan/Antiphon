import { describe, expect, it } from 'vitest'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { renderWithProviders, screen } from '../../test/utils'
import { WorkLine } from './WorkLine'
import { citationHead, taskWorkLine, workLineTarget } from './workLineFormat'

/** Timezone-free formatter for the pure tests: '2026-08-17T13:02:00Z' → '13:02'. */
const utcTime = (iso: string) => iso.slice(11, 16)

function task(overrides: Partial<AgentTaskSummaryDto> = {}): AgentTaskSummaryDto {
  return {
    id: '11111111-0000-0000-0000-000000000001',
    rootTaskId: '11111111-0000-0000-0000-000000000001',
    parentTaskId: null,
    depth: 0,
    title: 'CARD-0056 - launch leak - slices 3+4',
    kind: 'Worker',
    role: 'Code',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Shared',
    workingDirectory: 'C:\\src\\antiphon',
    repoPath: null,
    worktreePath: null,
    worktreeBranch: null,
    scopeGlob: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-17T10:00:00Z',
    dispatchedAt: '2026-08-17T10:00:05Z',
    completedAt: null,
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 1.12,
    costPricingVersion: 2,
    subtreeCostUsd: 1.12,
    childCount: 0,
    expectedDurationMinutes: 30,
    nextCheckAt: '2026-08-17T13:02:00Z',
    checkCount: 1,
    ...overrides,
  }
}

describe('taskWorkLine', () => {
  it('renders the orchestrator status format: citation, title, tier, check time, spend', () => {
    const parts = taskWorkLine(task({}), { formatTime: utcTime })
    expect(parts.line).toBe('#56 launch leak - slices 3+4 · opus')
    expect(parts.sub).toBe('check 13:02 · $1.12')
  })

  it('a spent check budget reads "checks spent" — the duplication with band 1 is the signal', () => {
    const parts = taskWorkLine(task({ nextCheckAt: null, checkCount: 3 }), { formatTime: utcTime })
    expect(parts.sub).toBe('checks spent · $1.12')
  })

  it('a task never scheduled for checks reads by its lane, not as spent', () => {
    const parts = taskWorkLine(task({ nextCheckAt: null, checkCount: 0 }), { formatTime: utcTime })
    expect(parts.sub).toBe('working · $1.12')
  })

  it('blocked and queued outrank the check clock — the state is the news', () => {
    expect(taskWorkLine(task({ status: 'Blocked' }), { formatTime: utcTime }).sub).toBe(
      'blocked — answer it · $1.12',
    )
    expect(taskWorkLine(task({ status: 'Queued' }), { formatTime: utcTime }).sub).toBe(
      'queued · $1.12',
    )
  })

  it('a Grok task names the model it actually runs, not the Claude rung of its tier', () => {
    // The line an operator scans to decide what to escalate: `fable` on a Grok task named a model
    // nobody was paying for (CARD-0084 S4).
    expect(taskWorkLine(task({ agentKind: 'Grok' }), { formatTime: utcTime }).line).toBe(
      '#56 launch leak - slices 3+4 · grok-4.6',
    )
    // xAI ships two models, so the bottom half of the ladder collapses onto grok-4.5.
    expect(
      taskWorkLine(task({ agentKind: 'Grok', modelLevel: 'Medium' }), { formatTime: utcTime }).line,
    ).toBe('#56 launch leak - slices 3+4 · grok-4.5')
  })

  it('every Claude tier still reads exactly as it did before the kind was consulted', () => {
    const alias = (modelLevel: AgentTaskSummaryDto['modelLevel']) =>
      taskWorkLine(task({ modelLevel, title: 'x' }), { formatTime: utcTime }).line
    expect(alias('Frontier')).toBe('x · fable')
    expect(alias('High')).toBe('x · opus')
    expect(alias('Medium')).toBe('x · sonnet')
    expect(alias('Low')).toBe('x · haiku')
  })

  it('a title without a card citation renders verbatim', () => {
    const parts = taskWorkLine(task({ title: 'tighten the deploy doc', modelLevel: 'Medium' }), {
      formatTime: utcTime,
    })
    expect(parts.line).toBe('tighten the deploy doc · sonnet')
  })
})

describe('citationHead', () => {
  it('strips zero-padding and the separator after the citation', () => {
    expect(citationHead('CARD-0004 - tiny fix')).toBe('#4 tiny fix')
    expect(citationHead('card-0067: reply durability')).toBe('#67 reply durability')
  })

  it('a citation-only title keeps just the number', () => {
    expect(citationHead('CARD-0056')).toBe('#56')
  })
})

describe('WorkLine', () => {
  it('renders as a link to the card thread when the title carries a citation', () => {
    const t = task({})
    renderWithProviders(<WorkLine task={t} />)
    const link = screen.getByRole('link', { name: `Open ${t.title}` })
    expect(link).toHaveAttribute('href', workLineTarget(t))
    expect(workLineTarget(t)).toBe('/thread/card-56')
  })

  it('a title without a citation falls back to the task drawer', () => {
    const t = task({ title: 'tighten the deploy doc' })
    expect(workLineTarget(t)).toBe(`/orchestrator?tab=delegations&task=${t.id}`)
  })
})
