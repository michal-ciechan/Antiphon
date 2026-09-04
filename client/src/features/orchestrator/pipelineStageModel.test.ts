import { describe, expect, it } from 'vitest'
import type {
  AgentTaskPipelineBlockedDto,
  AgentTaskPipelineDto,
  AgentTaskPipelineHolderDto,
  AgentTaskPipelineInFlightDto,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
  AgentTaskRole,
  RoutingPinRefDto,
} from '../../api/agentTasks'
import {
  STAGE_LABEL,
  compactAlias,
  compactElapsed,
  compactQueueReason,
  fleetStrip,
  idleLine,
  isPipelineEmpty,
  rightCell,
  rowLabel,
  rowTarget,
  stageCountLine,
  stageCounts,
  stagePinLabel,
  stageRows,
  visibleStages,
} from './pipelineStageModel'

const NOW = Date.parse('2026-02-03T09:14:00Z')

function pipeline(overrides: Partial<AgentTaskPipelineDto> = {}): AgentTaskPipelineDto {
  return {
    asOf: '2026-02-03T09:14:00Z',
    recommendationsAreAdvisory: true,
    maxConcurrentTasks: 6,
    inFlightAgainstCap: 2,
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

function pin(overrides: Partial<RoutingPinRefDto> = {}): RoutingPinRefDto {
  return {
    id: 'pin-1',
    cardId: null,
    cardIdentifier: null,
    role: 'Code',
    provenance: 'Human',
    strength: 'Required',
    agentKind: 'Grok',
    modelLevel: 'Frontier',
    notBefore: null,
    reason: 'execute with grok',
    ...overrides,
  }
}

function holder(overrides: Partial<AgentTaskPipelineHolderDto> = {}): AgentTaskPipelineHolderDto {
  return {
    taskId: 'hold-1',
    shortId: '1a2b3c4d',
    title: 'CARD-0288 lease holder',
    ...overrides,
  }
}

function inFlight(overrides: Partial<AgentTaskPipelineInFlightDto> = {}): AgentTaskPipelineInFlightDto {
  return {
    taskId: 't-fly',
    shortId: 'tfly',
    title: 'Plan CARD-0301 (phone-friendly view. Read the card in full first.)',
    status: 'Dispatched',
    card: { id: 'card-301', identifier: 'CARD-0301', title: 'Phone-friendly pipeline-stage view' },
    agentName: 'task-fly',
    dispatchedAt: '2026-02-03T09:10:00Z',
    lastActivityAt: '2026-02-03T09:12:00Z',
    agentKind: 'Grok',
    modelLevel: 'Frontier',
    workspace: 'Worktree',
    ...overrides,
  }
}

function queued(overrides: Partial<AgentTaskPipelineQueuedDto> = {}): AgentTaskPipelineQueuedDto {
  return {
    taskId: 't-queue',
    shortId: 'tqueue',
    title: 'queued behind the checkout',
    card: { id: 'card-239', identifier: 'CARD-0239', title: 'Land queue not restart-safe' },
    createdAt: '2026-02-03T08:50:00Z',
    queueReason: 'sharedCheckoutLease',
    heldBy: [holder()],
    agentKind: 'ClaudeCode',
    modelLevel: 'Medium',
    workspace: 'Shared',
    ...overrides,
  }
}

function blocked(overrides: Partial<AgentTaskPipelineBlockedDto> = {}): AgentTaskPipelineBlockedDto {
  return {
    taskId: 't-block',
    shortId: 'tblock',
    title: 'blocked deploy',
    card: { id: 'card-32', identifier: 'CARD-0032', title: 'Deploy pipeline: land-to-master' },
    createdAt: '2026-02-03T08:55:00Z',
    agentKind: 'ClaudeCode',
    modelLevel: 'Medium',
    routingExhausted: false,
    ...overrides,
  }
}

function ready(overrides: Partial<AgentTaskPipelineReadyDto> = {}): AgentTaskPipelineReadyDto {
  return {
    card: { id: 'card-31', identifier: 'CARD-0031', title: 'Project status view' },
    sourcePlanTaskId: 'plan-31',
    sourcePlanShortId: 'plan31',
    readySince: '2026-02-03T06:14:00Z',
    deliverablePath: 'docs/superpowers/plans/2026-09-02-card-0031-project-status-view-plan.md',
    deliverableRef: null,
    routingPin: null,
    ...overrides,
  }
}

const ROLES: AgentTaskRole[] = [
  'Custom',
  'Plan',
  'Code',
  'Review',
  'Debug',
  'Coverage',
  'Docs',
  'Commit',
  'Test',
  'Deploy',
  'Merge',
]

function fleet(filled: Partial<Record<AgentTaskRole, AgentTaskPipelineStageDto>>): AgentTaskPipelineDto {
  return pipeline({
    stages: ROLES.map((role) => filled[role] ?? stage({ role, recommendedInFlight: role === 'Custom' ? null : 1 })),
  })
}

describe('STAGE_LABEL', () => {
  it('aliases Code to Execute and Custom to Other', () => {
    expect(STAGE_LABEL.Code).toBe('Execute')
    expect(STAGE_LABEL.Custom).toBe('Other')
  })

  it('keeps every other role as its own name', () => {
    expect(STAGE_LABEL.Plan).toBe('Plan')
    expect(STAGE_LABEL.Deploy).toBe('Deploy')
    expect(STAGE_LABEL.Review).toBe('Review')
  })
})

describe('visibleStages', () => {
  it('hides empty stages and counts them as idle, keeping server order of the rest', () => {
    const dto = fleet({
      Plan: stage({ role: 'Plan', recommendedInFlight: 1, inFlightCount: 1, inFlight: [inFlight()] }),
      Code: stage({ role: 'Code', recommendedInFlight: 1, ready: [ready()] }),
      Deploy: stage({ role: 'Deploy', recommendedInFlight: 1, blocked: [blocked()] }),
    })
    const { shown, idleCount } = visibleStages(dto)
    expect(shown.map((item) => item.role)).toEqual(['Plan', 'Code', 'Deploy'])
    expect(idleCount).toBe(8)
  })

  it('treats a stage with any of the four collections as shown', () => {
    expect(visibleStages(fleet({ Docs: stage({ role: 'Docs', queued: [queued()] }) })).shown).toHaveLength(1)
    expect(visibleStages(fleet({})).shown).toHaveLength(0)
    expect(visibleStages(fleet({})).idleCount).toBe(11)
  })
})

describe('stageRows', () => {
  it('orders in-flight, then blocked, then queued, then ready', () => {
    const current = stage({
      role: 'Code',
      recommendedInFlight: 1,
      inFlightCount: 1,
      inFlight: [inFlight({ taskId: 'fly' })],
      blocked: [blocked({ taskId: 'blk' })],
      queued: [queued({ taskId: 'que' })],
      ready: [ready()],
    })
    expect(stageRows(current, NOW, pipeline()).map((row) => row.kind)).toEqual([
      'inFlight',
      'blocked',
      'queued',
      'ready',
    ])
  })

  it('keeps the server order inside each kind', () => {
    const current = stage({
      inFlight: [
        inFlight({ taskId: 'a', card: { id: 'ca', identifier: 'CARD-0001', title: 'First' } }),
        inFlight({ taskId: 'b', card: { id: 'cb', identifier: 'CARD-0002', title: 'Second' } }),
      ],
    })
    expect(stageRows(current, NOW, pipeline()).map((row) => row.key)).toEqual(['a', 'b'])
  })
})

describe('identifier and title', () => {
  it('uses the card identifier and title when the row is bound', () => {
    const [row] = stageRows(stage({ inFlight: [inFlight()] }), NOW, pipeline())
    expect(row.identifier).toBe('#301')
    expect(row.title).toBe('Phone-friendly pipeline-stage view')
  })

  it('falls back to citationHead for an unbound title that cites a card', () => {
    const [row] = stageRows(
      stage({
        inFlight: [
          inFlight({
            card: null,
            title: 'CARD-0056 - launch leak - slices 3+4',
          }),
        ],
      }),
      NOW,
      pipeline(),
    )
    expect(row.identifier).toBeNull()
    expect(row.title).toBe('#56 launch leak - slices 3+4')
  })

  it('keeps an unbound title with no citation verbatim', () => {
    const [row] = stageRows(
      stage({ inFlight: [inFlight({ card: null, title: 'in-flight docs pass' })] }),
      NOW,
      pipeline(),
    )
    expect(row.identifier).toBeNull()
    expect(row.title).toBe('in-flight docs pass')
  })
})

describe('compactQueueReason', () => {
  const empty = stage({ role: 'Code' })
  const cap = pipeline({ inFlightAgainstCap: 6, maxConcurrentTasks: 6 })

  it('names a lease holder by card citation and +N extra holders', () => {
    expect(
      compactQueueReason(
        queued({
          heldBy: [holder({ title: 'CARD-0288 lease holder' }), holder({ taskId: 'h2', shortId: 'bbbbbbbb', title: 'other' })],
        }),
        empty,
        cap,
      ),
    ).toBe('behind #288 +1')
  })

  it('falls back to ~shortId when the holder title has no card citation', () => {
    expect(
      compactQueueReason(queued({ heldBy: [holder({ title: 'in-flight docs pass', shortId: '1a2b3c4d' })] }), empty, cap),
    ).toBe('behind ~1a2b3c4d')
  })

  it('names a sibling land by the holder citation', () => {
    expect(
      compactQueueReason(
        queued({ queueReason: 'siblingLandInFlight', heldBy: [holder({ title: 'Execute CARD-0288' })] }),
        empty,
        cap,
      ),
    ).toBe('landing #288')
  })

  it('formats routingPinNotBefore as after HH:MM via the injected clock', () => {
    expect(
      compactQueueReason(
        queued({ queueReason: 'routingPinNotBefore', heldBy: [] }),
        stage({ routingPin: pin({ notBefore: '2026-02-03T14:00:00Z' }) }),
        cap,
        () => '14:00',
      ),
    ).toBe('after 14:00')
  })

  it('shows the fleet cap as slots N/N', () => {
    expect(compactQueueReason(queued({ queueReason: 'concurrencyCap', heldBy: [] }), empty, cap)).toBe('slots 6/6')
  })

  it('collapses awaitingDispatch to queued', () => {
    expect(compactQueueReason(queued({ queueReason: 'awaitingDispatch', heldBy: [] }), empty, cap)).toBe('queued')
  })
})

describe('rightCell against a pinned now', () => {
  it('renders <alias> <elapsed> for in-flight', () => {
    expect(rightCell({ kind: 'inFlight', row: inFlight() }, NOW)).toBe('grok-4.6 4m')
  })

  it('renders elapsed alone when agentKind is missing (pre-S1 server)', () => {
    expect(rightCell({ kind: 'inFlight', row: inFlight({ agentKind: undefined, modelLevel: undefined }) }, NOW)).toBe(
      '4m',
    )
  })

  it('renders ready <ago>', () => {
    expect(rightCell({ kind: 'ready', row: ready() }, NOW)).toBe('ready 3h')
    expect(rightCell({ kind: 'ready', row: ready({ readySince: '2026-02-02T09:14:00Z' }) }, NOW)).toBe('ready 1d')
  })

  it('renders blocked, or no route when routing is exhausted', () => {
    expect(rightCell({ kind: 'blocked', row: blocked() }, NOW)).toBe('blocked')
    expect(rightCell({ kind: 'blocked', row: blocked({ routingExhausted: true }) }, NOW)).toBe('no route')
  })
})

describe('compactAlias', () => {
  it('strips only the gpt-5.6- prefix', () => {
    expect(compactAlias('High', 'Codex')).toBe('terra')
    expect(compactAlias('Frontier', 'Codex')).toBe('sol')
    expect(compactAlias('Medium', 'Codex')).toBe('luna')
    expect(compactAlias('Frontier', 'Grok')).toBe('grok-4.6')
    expect(compactAlias('Frontier', 'ClaudeCode')).toBe('fable')
  })
})

describe('compactElapsed', () => {
  it('uses the largest unit only', () => {
    expect(compactElapsed('2026-02-03T09:13:20Z', NOW)).toBe('40s')
    expect(compactElapsed('2026-02-03T09:10:00Z', NOW)).toBe('4m')
    expect(compactElapsed('2026-02-03T06:14:00Z', NOW)).toBe('3h')
    expect(compactElapsed('2026-02-02T09:14:00Z', NOW)).toBe('1d')
  })
})

describe('rowTarget', () => {
  it('opens the drawer for task rows', () => {
    expect(rowTarget({ kind: 'inFlight', taskId: 't-fly' })).toEqual({ drawer: 't-fly' })
    expect(rowTarget({ kind: 'queued', taskId: 't-queue' })).toEqual({ drawer: 't-queue' })
    expect(rowTarget({ kind: 'blocked', taskId: 't-block' })).toEqual({ drawer: 't-block' })
  })

  it('links a ready row to the plan reader', () => {
    expect(rowTarget({ kind: 'ready', row: ready() })).toEqual({
      to: `/plans?${new URLSearchParams({
        file: 'docs/superpowers/plans/2026-09-02-card-0031-project-status-view-plan.md',
        task: 'plan-31',
      }).toString()}`,
    })
  })

  it('includes ref when the ready row carries one', () => {
    const to = rowTarget({ kind: 'ready', row: ready({ deliverableRef: 'abc123' }) })
    expect('to' in to && to.to).toContain('ref=abc123')
  })
})

describe('rowLabel', () => {
  it('names the kind in words so the row is never colour-only', () => {
    expect(rowLabel({ identifier: '#301', title: 'Phone-friendly', kind: 'inFlight' })).toBe('Open #301 — running')
    expect(rowLabel({ identifier: '#239', title: 'Land queue', kind: 'queued' })).toBe('Open #239 — queued')
    expect(rowLabel({ identifier: '#32', title: 'Deploy', kind: 'blocked' })).toBe('Open #32 — blocked')
    expect(rowLabel({ identifier: '#31', title: 'Project status view', kind: 'ready' })).toBe('Open #31 — ready')
  })
})

describe('fleetStrip', () => {
  it('names the cap and the local clock when work is in flight', () => {
    expect(fleetStrip(pipeline({ inFlightAgainstCap: 2, maxConcurrentTasks: 6 }), () => '12:54')).toBe(
      '2 of 6 slots · as of 12:54',
    )
  })

  it('drops the clock on a calm fleet', () => {
    expect(fleetStrip(pipeline({ inFlightAgainstCap: 0, maxConcurrentTasks: 6 }))).toBe('0 of 6 slots')
  })
})

describe('stage header helpers', () => {
  it('uses N/R running when a recommendation exists, N running when it does not', () => {
    expect(
      stageCountLine(stage({ inFlightCount: 1, recommendedInFlight: 1, queued: [queued(), queued()], ready: [ready()] })),
    ).toBe('1/1 running · 2 queued · 1 ready')
    expect(stageCountLine(stage({ role: 'Custom', inFlightCount: 1, recommendedInFlight: null }))).toBe('1 running')
    expect(stageCountLine(stage({ inFlightCount: 0, blocked: [blocked(), blocked(), blocked(), blocked()] }))).toBe(
      '4 blocked',
    )
  })

  it('exposes the raw counts', () => {
    expect(stageCounts(stage({ inFlightCount: 1, recommendedInFlight: 1, queued: [queued()] }))).toEqual({
      inFlight: 1,
      recommended: 1,
      queued: 1,
      blocked: 0,
      ready: 0,
    })
  })

  it('renders pin grok-4.6 from the pin alias, and kind only when level is null', () => {
    expect(stagePinLabel(stage({ routingPin: pin() }))).toBe('pin grok-4.6')
    expect(stagePinLabel(stage({ routingPin: pin({ modelLevel: null, agentKind: 'Grok' }) }))).toBe('pin Grok')
    expect(stagePinLabel(stage({ routingPin: null }))).toBeNull()
  })
})

describe('empty / idle copy', () => {
  it('treats a fleet with no rows as empty', () => {
    expect(isPipelineEmpty(fleet({}))).toBe(true)
    expect(isPipelineEmpty(fleet({ Docs: stage({ role: 'Docs', inFlight: [inFlight()] }) }))).toBe(false)
  })

  it('pluralises the idle footer', () => {
    expect(idleLine(7)).toBe('7 idle stages')
    expect(idleLine(1)).toBe('1 idle stage')
  })
})
