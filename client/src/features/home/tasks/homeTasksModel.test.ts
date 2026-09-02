import { describe, expect, it } from 'vitest'
import type { AgentSummaryDto } from '../../../api/agents'
import type { AttentionItemDto } from '../../../api/attention'
import type { CardStatus } from '../../../api/boards'
import type { AgentTaskStatus } from '../../../api/agentTasks'
import type { HomeTaskGroup, HomeTaskItemDto, HomeTaskWorkerDto } from '../../../api/homeTasks'
import { STATE_COLORS } from '../../board/boardVisuals'
import { STATUS_COLOR } from '../../delegations/taskVisuals'
import { normalizeDir } from '../projectGrouping'
import {
  STATE_COLOR,
  filterByProject,
  groupItems,
  questionFor,
  workerAgent,
} from './homeTasksModel'

const CARD_STATUSES: CardStatus[] = ['Backlog', 'InProgress', 'Review', 'Done', 'NeedsDecision', 'Canceled']
const TASK_STATUSES: AgentTaskStatus[] = [
  'Queued',
  'Dispatched',
  'Working',
  'Blocked',
  'Succeeded',
  'Failed',
  'Canceled',
]

function item(overrides: Partial<HomeTaskItemDto> = {}): HomeTaskItemDto {
  return {
    key: 'card:1',
    source: 'Card',
    id: '11111111-0000-0000-0000-000000000001',
    identifier: 'CARD-0001',
    title: 'A card',
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

function attention(overrides: Partial<AttentionItemDto> = {}): AttentionItemDto {
  return {
    kind: 'BlockedQuestion',
    severity: 'Critical',
    taskId: null,
    sessionId: null,
    agentId: null,
    messageId: null,
    cardId: null,
    title: 'Question',
    headline: 'Blocked',
    evidence: 'Should validation errors block save?',
    sinceUtc: '2026-09-01T10:00:00Z',
    subtreeCostUsd: null,
    actions: ['Reply'],
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
    liveSession: null,
    alwaysOn: false,
    remoteControlEnabled: false,
    supervision: null,
    systemPromptAppend: null,
    modelLevel: 'High',
    working: true,
    ...overrides,
  }
}

describe('filterByProject', () => {
  it('matches a card by repo path with mixed separators and casing', () => {
    const card = item({
      key: 'card:repo',
      workingDirectory: 'C:/src/Antiphon',
      repoPath: null,
      worktreePath: null,
    })
    const keys = [normalizeDir('C:\\src\\antiphon')]
    expect(filterByProject([card], keys).map((row) => row.key)).toEqual(['card:repo'])
  })

  it('matches a task by worktree path', () => {
    const task = item({
      key: 'task:wt',
      source: 'Delegation',
      workingDirectory: 'C:\\src\\Antiphon-wt',
      repoPath: 'C:\\src\\Antiphon',
      worktreePath: 'C:/src/Antiphon-wt',
    })
    const other = item({ key: 'card:other', workingDirectory: 'D:\\other' })
    const keys = [normalizeDir('C:\\src\\antiphon-wt')]
    expect(filterByProject([task, other], keys).map((row) => row.key)).toEqual(['task:wt'])
  })

  it('matches a worktree task against the main repo dirKey via repoPath', () => {
    const task = item({
      key: 'task:wt',
      source: 'Delegation',
      workingDirectory: 'C:\\src\\Antiphon-wt',
      repoPath: 'C:/src/antiphon',
      worktreePath: 'C:\\src\\Antiphon-wt',
    })
    const keys = [normalizeDir('C:\\src\\ANTIPHON')]
    expect(filterByProject([task], keys)).toHaveLength(1)
  })
})

describe('groupItems', () => {
  it('splits in server order and never re-sorts', () => {
    const firstDone = item({ key: 'done-old', group: 'Done', identifier: 'old' })
    const running = item({ key: 'run', group: 'Running' })
    const secondDone = item({ key: 'done-new', group: 'Done', identifier: 'new' })
    const grouped = groupItems([firstDone, running, secondDone])
    expect(grouped.Done.map((row) => row.key)).toEqual(['done-old', 'done-new'])
    expect(grouped.Running.map((row) => row.key)).toEqual(['run'])
  })

  it('caps Done at 12, Review at 8, Next at 8 and reports hidden counts', () => {
    const review = Array.from({ length: 9 }, (_, i) =>
      item({ key: `review-${i}`, group: 'Review' as HomeTaskGroup }),
    )
    const next = Array.from({ length: 9 }, (_, i) =>
      item({ key: `next-${i}`, group: 'Next' as HomeTaskGroup }),
    )
    const done = Array.from({ length: 13 }, (_, i) =>
      item({ key: `done-${i}`, group: 'Done' as HomeTaskGroup }),
    )
    const grouped = groupItems([...review, ...next, ...done])
    expect(grouped.Review).toHaveLength(8)
    expect(grouped.Next).toHaveLength(8)
    expect(grouped.Done).toHaveLength(12)
    expect(grouped.hidden).toEqual({ Review: 1, Next: 1, Done: 1 })
    expect(grouped.Review[0].key).toBe('review-0')
    expect(grouped.Review[7].key).toBe('review-7')
    expect(grouped.Done[11].key).toBe('done-11')
  })
})

describe('questionFor', () => {
  it('reads a card question from CardNeedsDecision by cardId', () => {
    const card = item({ id: 'card-1', source: 'Card' })
    const rows = [
      attention({
        kind: 'CardNeedsDecision',
        cardId: 'card-1',
        evidence: '\n\n  Should validation errors block save?\nMore detail',
      }),
    ]
    expect(questionFor(card, rows)).toBe('Should validation errors block save?')
  })

  it('reads an unbound task question from BlockedQuestion by own id', () => {
    const task = item({ id: 'task-1', source: 'Delegation' })
    const rows = [attention({ kind: 'BlockedQuestion', taskId: 'task-1', evidence: 'Which branch?' })]
    expect(questionFor(task, rows)).toBe('Which branch?')
  })

  it('reads a card question from BlockedQuestion on the bound worker', () => {
    const card = item({ source: 'Card', worker: worker({ taskId: 'worker-1' }) })
    const rows = [attention({ kind: 'BlockedQuestion', taskId: 'worker-1', evidence: 'Ship it?' })]
    expect(questionFor(card, rows)).toBe('Ship it?')
  })

  it('returns null when the feed has no matching row', () => {
    expect(questionFor(item(), [attention({ taskId: 'other' })])).toBeNull()
  })
})

describe('workerAgent', () => {
  it('finds the agent by Worker.AgentId', () => {
    const card = item({ worker: worker({ agentId: 'agent-1' }) })
    expect(workerAgent(card, [agent({ id: 'agent-1', name: 'bound' })])?.name).toBe('bound')
  })

  it('returns null when the worker agent is absent', () => {
    expect(workerAgent(item({ worker: worker({ agentId: 'missing' }) }), [agent()])).toBeNull()
    expect(workerAgent(item({ worker: null }), [agent()])).toBeNull()
  })
})

describe('STATE_COLOR', () => {
  it('covers every card and task status from the source maps', () => {
    for (const status of CARD_STATUSES) {
      expect(STATE_COLOR[status]).toBe(STATE_COLORS[status])
    }
    for (const status of TASK_STATUSES) {
      expect(STATE_COLOR[status]).toBe(STATUS_COLOR[status])
    }
  })
})
