import { describe, expect, it } from 'vitest'
import type { AgentSummaryDto } from '../../../api/agents'
import type { AttentionItemDto, AttentionKind } from '../../../api/attention'
import type { CardStatus } from '../../../api/boards'
import type {
  AgentTaskPipelineDto,
  AgentTaskPipelineHolderDto,
  AgentTaskPipelineInFlightDto,
  AgentTaskPipelineQueueReason,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
  AgentTaskStatus,
  RoutingPinRefDto,
} from '../../../api/agentTasks'
import type { HomeTaskGroup, HomeTaskItemDto, HomeTaskWorkerDto } from '../../../api/homeTasks'
import { STATE_COLORS } from '../../board/boardVisuals'
import { STATUS_COLOR } from '../../delegations/taskVisuals'
import { normalizeDir } from '../projectGrouping'
import {
  LIVENESS_KINDS,
  STATE_COLOR,
  filterByProject,
  formatElapsed,
  groupItems,
  livenessFor,
  pipelineRowFor,
  questionFor,
  queueReasonFor,
  readyLine,
  readinessFor,
  runningSince,
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

const LIVENESS_KIND_LIST: AttentionKind[] = [
  'DeadSession',
  'NeverStarted',
  'BriefUndelivered',
  'ReportUnsettled',
  'UnmarkedWaiting',
  'PastExpectedIdle',
  'ProgressStalled',
  'Overdue',
  'ChecksSpent',
  'UncorrelatedReport',
]

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
    status: 'Working',
    card: null,
    agentName: 'task-bound',
    dispatchedAt: '2026-02-03T07:00:00Z',
    lastActivityAt: '2026-02-03T09:11:00Z',
    ...overrides,
  }
}

function queuedRow(overrides: Partial<AgentTaskPipelineQueuedDto> = {}): AgentTaskPipelineQueuedDto {
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

function holder(overrides: Partial<AgentTaskPipelineHolderDto> = {}): AgentTaskPipelineHolderDto {
  return {
    taskId: 'aaaaaaaa-0000-0000-0000-000000000006',
    shortId: '1a2b3c4d',
    title: 'in-flight docs pass',
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

describe('livenessFor', () => {
  it('pins the ten progress kinds and excludes BlockedQuestion and CardStalled', () => {
    expect([...LIVENESS_KINDS].sort()).toEqual([...LIVENESS_KIND_LIST].sort())
    expect(LIVENESS_KINDS.size).toBe(10)
    expect(LIVENESS_KINDS.has('BlockedQuestion')).toBe(false)
    expect(LIVENESS_KINDS.has('CardStalled')).toBe(false)
  })

  it('matches a delegation by its own id', () => {
    const task = item({ id: 'task-1', source: 'Delegation' })
    const row = attention({ kind: 'DeadSession', taskId: 'task-1' })
    expect(livenessFor(task, [row])).toEqual(row)
  })

  it('matches a card by the bound worker id', () => {
    const card = item({ source: 'Card', worker: worker({ taskId: 'worker-1' }) })
    const row = attention({ kind: 'Overdue', taskId: 'worker-1' })
    expect(livenessFor(card, [row])).toEqual(row)
  })

  it('does not treat BlockedQuestion or CardStalled as liveness even when task-keyed', () => {
    const task = item({ id: 'task-1', source: 'Delegation' })
    expect(
      livenessFor(task, [attention({ kind: 'BlockedQuestion', taskId: 'task-1' })]),
    ).toBeNull()
    expect(
      livenessFor(task, [attention({ kind: 'CardStalled', taskId: 'task-1' })]),
    ).toBeNull()
  })

  it('returns the first matching liveness row', () => {
    const task = item({ id: 'task-1', source: 'Delegation' })
    const first = attention({ kind: 'ProgressStalled', taskId: 'task-1', title: 'first' })
    const second = attention({ kind: 'Overdue', taskId: 'task-1', title: 'second' })
    expect(livenessFor(task, [first, second])?.title).toBe('first')
  })

  it('returns null when the feed has no matching row', () => {
    expect(livenessFor(item({ worker: worker() }), [attention({ kind: 'Overdue', taskId: 'other' })])).toBeNull()
    expect(livenessFor(item({ source: 'Card', worker: null }), [attention({ kind: 'Overdue' })])).toBeNull()
  })
})

describe('runningSince', () => {
  it('prefers worker.dispatchedAt, then startedAt, then createdAt', () => {
    const withDispatch = item({
      group: 'Running',
      startedAt: '2026-02-03T08:00:00Z',
      createdAt: '2026-02-03T07:00:00Z',
      worker: worker({ dispatchedAt: '2026-02-03T09:00:00Z' }),
    })
    expect(runningSince(withDispatch)).toBe('2026-02-03T09:00:00Z')

    const noDispatch = item({
      group: 'Running',
      startedAt: '2026-02-03T08:00:00Z',
      createdAt: '2026-02-03T07:00:00Z',
      worker: worker({ dispatchedAt: null }),
    })
    expect(runningSince(noDispatch)).toBe('2026-02-03T08:00:00Z')

    const createdOnly = item({
      group: 'Running',
      startedAt: null,
      createdAt: '2026-02-03T07:00:00Z',
      worker: null,
    })
    expect(runningSince(createdOnly)).toBe('2026-02-03T07:00:00Z')
  })

  it('returns null off Running', () => {
    expect(runningSince(item({ group: 'Next', worker: worker() }))).toBeNull()
    expect(runningSince(item({ group: 'Done', worker: worker() }))).toBeNull()
    expect(runningSince(item({ group: 'NeedsHuman', worker: worker({ status: 'Blocked' }) }))).toBeNull()
  })
})

describe('pipelineRowFor', () => {
  it('finds in-flight and queued rows by item id or worker taskId, and ready by card id', () => {
    const inflight = inFlightRow({ taskId: 'worker-1' })
    const queued = queuedRow({ taskId: 'task-queued' })
    const ready = readyRow()
    const pipe = pipeline({
      stages: [
        stage({
          inFlight: [inflight],
          queued: [queued],
          ready: [ready],
        }),
      ],
    })

    expect(pipelineRowFor(item({ worker: worker({ taskId: 'worker-1' }) }), pipe)).toEqual(inflight)
    expect(
      pipelineRowFor(item({ id: 'task-queued', source: 'Delegation', worker: null }), pipe),
    ).toEqual(queued)
    expect(pipelineRowFor(item({ source: 'Card', worker: null }), pipe)).toEqual(ready)
  })
})

describe('queueReasonFor', () => {
  function queuedPipeline(
    reason: AgentTaskPipelineQueueReason,
    extras: Partial<AgentTaskPipelineQueuedDto> = {},
    stageExtras: Partial<AgentTaskPipelineStageDto> = {},
    pipelineExtras: Partial<AgentTaskPipelineDto> = {},
  ) {
    const row = queuedRow({ taskId: 'task-queued', queueReason: reason, ...extras })
    return {
      row,
      pipe: pipeline({
        ...pipelineExtras,
        stages: [stage({ queued: [row], ...stageExtras })],
      }),
      item: item({
        id: 'task-queued',
        source: 'Delegation',
        group: 'Next',
        state: 'Queued',
        worker: null,
      }),
    }
  }

  it('names a shared checkout holder and +N when more', () => {
    const { pipe, item: queued } = queuedPipeline('sharedCheckoutLease', {
      heldBy: [holder(), holder({ taskId: 'other', shortId: 'other001', title: 'second' })],
    })
    const view = queueReasonFor(queued, pipe)
    expect(view?.reason).toBe('sharedCheckoutLease')
    expect(view?.line).toBe(
      'waiting: shared checkout held by task-1a2b3c4d — in-flight docs pass +1',
    )
    expect(view?.holders).toHaveLength(2)
  })

  it('prints the concurrency cap as in-flight of max slots', () => {
    const { pipe, item: queued } = queuedPipeline(
      'concurrencyCap',
      {},
      {},
      { maxConcurrentTasks: 6, inFlightAgainstCap: 6 },
    )
    expect(queueReasonFor(queued, pipe)?.line).toBe('waiting: 6 of 6 task slots in use')
  })

  it('prints a routing pin not-before clock', () => {
    const { pipe, item: queued } = queuedPipeline(
      'routingPinNotBefore',
      {},
      { routingPin: pin({ notBefore: '2026-02-03T14:00:00Z' }) },
    )
    expect(queueReasonFor(queued, pipe)?.line).toBe('waiting: not before 14:00 (routing pin)')
  })

  it('prints awaitingDispatch as the next-tick line', () => {
    const { pipe, item: queued } = queuedPipeline('awaitingDispatch')
    expect(queueReasonFor(queued, pipe)?.line).toBe('queued — next dispatch tick')
  })

  it('names the sibling whose land is in flight', () => {
    const { pipe, item: queued } = queuedPipeline('siblingLandInFlight', {
      heldBy: [holder({ shortId: 'dddddddd', title: 'plan sibling landing' })],
    })
    expect(queueReasonFor(queued, pipe)?.line).toBe(
      'waiting: a sibling is landing (task-dddddddd — plan sibling landing)',
    )
  })

  it('returns null for a card whose worker is not queued', () => {
    const pipe = pipeline({
      stages: [stage({ queued: [queuedRow({ taskId: 'someone-else' })] })],
    })
    expect(queueReasonFor(item({ group: 'Next', worker: worker() }), pipe)).toBeNull()
    expect(queueReasonFor(item({ group: 'Next', worker: null }), pipe)).toBeNull()
  })
})

describe('readinessFor', () => {
  it('finds a ready card by card id', () => {
    const ready = readyRow()
    const pipe = pipeline({ stages: [stage({ ready: [ready] })] })
    const view = readinessFor(item({ source: 'Card', group: 'Next', state: 'Backlog' }), pipe)
    expect(view).toEqual({
      since: ready.readySince,
      deliverablePath: ready.deliverablePath,
      deliverableRef: ready.deliverableRef,
      sourcePlanShortId: ready.sourcePlanShortId,
      sourcePlanTaskId: ready.sourcePlanTaskId,
      targetRole: 'Code',
      sourceRole: undefined,
      handoff: undefined,
    })
  })

  it('never asks a Done or NeedsDecision card', () => {
    const pipe = pipeline({ stages: [stage({ ready: [readyRow()] })] })
    expect(
      readinessFor(item({ source: 'Card', group: 'Done', state: 'Done' }), pipe),
    ).toBeNull()
    expect(
      readinessFor(item({ source: 'Card', group: 'NeedsHuman', state: 'NeedsDecision' }), pipe),
    ).toBeNull()
  })

  it('names the stage the ready row sits on', () => {
    const ready = readyRow({ sourceRole: 'Investigate', handoff: 'root cause confirmed' })
    const pipe = pipeline({ stages: [stage({ role: 'Plan', ready: [ready] })] })
    const view = readinessFor(item({ source: 'Card', group: 'Next', state: 'Backlog' }), pipe)
    expect(view?.targetRole).toBe('Plan')
    expect(view?.sourceRole).toBe('Investigate')
    expect(readyLine(view!, Date.parse('2026-02-03T09:00:00Z'))).toBe(
      'Investigate landed 2d ago — ready for Plan',
    )
  })
})

describe('formatElapsed', () => {
  it('pins 2h14m at the story clock 2026-02-03T09:14:00Z', () => {
    expect(formatElapsed('2026-02-03T07:00:00Z', Date.parse('2026-02-03T09:14:00Z'))).toBe('2h14m')
  })
})
