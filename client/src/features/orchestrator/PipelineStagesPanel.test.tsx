import { HttpResponse, http } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  AgentTaskDetailDto,
  AgentTaskPipelineBlockedDto,
  AgentTaskPipelineDto,
  AgentTaskPipelineInFlightDto,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
  AgentTaskRole,
  AgentTaskSummaryDto,
} from '../../api/agentTasks'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { PipelineStagesPanel } from './PipelineStagesPanel'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const NOW = Date.parse('2026-02-03T09:14:00Z')
const FLY_ID = 'aaaaaaaa-0000-0000-0000-000000000001'

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

function inFlight(overrides: Partial<AgentTaskPipelineInFlightDto> = {}): AgentTaskPipelineInFlightDto {
  return {
    taskId: FLY_ID,
    shortId: 'aaaaaaaa',
    title: 'Plan CARD-0301 (phone-friendly view)',
    status: 'Working',
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
    taskId: 'bbbbbbbb-0000-0000-0000-000000000002',
    shortId: 'bbbbbbbb',
    title: 'queued behind the checkout',
    card: { id: 'card-239', identifier: 'CARD-0239', title: 'Land queue not restart-safe' },
    createdAt: '2026-02-03T08:50:00Z',
    queueReason: 'sharedCheckoutLease',
    heldBy: [{ taskId: FLY_ID, shortId: 'aaaaaaaa', title: 'CARD-0288 lease holder' }],
    agentKind: 'ClaudeCode',
    modelLevel: 'Medium',
    workspace: 'Shared',
    ...overrides,
  }
}

function blocked(overrides: Partial<AgentTaskPipelineBlockedDto> = {}): AgentTaskPipelineBlockedDto {
  return {
    taskId: 'cccccccc-0000-0000-0000-000000000003',
    shortId: 'cccccccc',
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
    sourcePlanTaskId: 'dddddddd-0000-0000-0000-000000000004',
    sourcePlanShortId: 'dddddddd',
    readySince: '2026-02-03T06:14:00Z',
    deliverablePath: 'docs/superpowers/plans/example.md',
    deliverableRef: 'abc',
    routingPin: null,
    ...overrides,
  }
}

function liveDto(): AgentTaskPipelineDto {
  const filled: Partial<Record<AgentTaskRole, AgentTaskPipelineStageDto>> = {
    Plan: stage({
      role: 'Plan',
      inFlightCount: 1,
      atOrAboveRecommendation: true,
      inFlight: [inFlight()],
    }),
    Code: stage({
      role: 'Code',
      routingPin: {
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
      },
      queued: [queued()],
      ready: [ready()],
    }),
    Deploy: stage({ role: 'Deploy', blocked: [blocked()] }),
  }
  return {
    asOf: '2026-02-03T09:14:00Z',
    recommendationsAreAdvisory: true,
    maxConcurrentTasks: 6,
    inFlightAgainstCap: 1,
    stages: ROLES.map((role) => filled[role] ?? stage({ role, recommendedInFlight: role === 'Custom' ? null : 1 })),
  }
}

function emptyDto(): AgentTaskPipelineDto {
  const live = liveDto()
  return {
    ...live,
    inFlightAgainstCap: 0,
    stages: live.stages.map((item) => ({
      ...item,
      inFlightCount: 0,
      atOrAboveRecommendation: false,
      inFlight: [],
      queued: [],
      blocked: [],
      ready: [],
    })),
  }
}

function summary(overrides: Partial<AgentTaskSummaryDto> = {}): AgentTaskSummaryDto {
  return {
    id: FLY_ID,
    rootTaskId: FLY_ID,
    parentTaskId: null,
    depth: 0,
    title: 'Phone-friendly pipeline-stage view',
    kind: 'Worker',
    role: 'Plan',
    agentKind: 'Grok',
    modelLevel: 'Frontier',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Worktree',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    worktreePath: null,
    worktreeBranch: null,
    scope: null,
    agentId: 'agent-1',
    agentName: 'task-fly',
    agentSessionId: 'session-1',
    attempt: 1,
    createdAt: '2026-02-03T09:00:00Z',
    dispatchedAt: '2026-02-03T09:10:00Z',
    completedAt: null,
    recoveredAt: null,
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 0,
    costPricingVersion: 2,
    subtreeCostUsd: 0,
    childCount: 0,
    expectedDurationMinutes: 30,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
}

function detail(): AgentTaskDetailDto {
  return {
    summary: summary(),
    goal: 'Build the phone stage glance.',
    result: null,
    resultFilePath: null,
    failureReason: null,
    mergeTargetRef: null,
    events: [{ type: 'Created', modelLevel: 'Frontier', detail: 'Created.', at: '2026-02-03T09:00:00Z' }],
  }
}

function servePipeline(body: AgentTaskPipelineDto | 'pending' | 500) {
  if (body === 'pending') {
    server.use(http.get('/api/agent-tasks/pipeline', async () => await new Promise<Response>(() => {})))
    return
  }
  if (body === 500) {
    server.use(http.get('/api/agent-tasks/pipeline', () => new HttpResponse(null, { status: 500 })))
    return
  }
  server.use(
    http.get('/api/agent-tasks/pipeline', () => HttpResponse.json(body)),
    http.get('/api/agent-tasks/:id', ({ params }) => {
      if (params.id === FLY_ID) return HttpResponse.json(detail())
      return new HttpResponse(null, { status: 404 })
    }),
  )
}

describe('PipelineStagesPanel', () => {
  beforeEach(() => {
    vi.spyOn(Date, 'now').mockReturnValue(NOW)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    window.history.pushState({}, '', '/')
  })

  it('renders the strip, shown stages, idle line, and a row of each kind', async () => {
    servePipeline(liveDto())
    renderWithProviders(<PipelineStagesPanel />)

    expect(await screen.findByTestId('pipeline-strip')).toHaveTextContent('1 of 6 slots')
    expect(screen.getByTestId('pipeline-strip')).toHaveTextContent('as of')
    expect(screen.getByTestId('pipeline-stage-Plan')).toHaveTextContent('Plan')
    expect(screen.getByTestId('pipeline-stage-Code')).toHaveTextContent('Execute')
    expect(screen.getByTestId('pipeline-stage-Code')).toHaveTextContent('pin grok-4.6')
    expect(screen.getByTestId('pipeline-stage-Deploy')).toHaveTextContent('Deploy')
    expect(screen.queryByTestId('pipeline-stage-Review')).not.toBeInTheDocument()
    expect(screen.getByTestId('pipeline-idle')).toHaveTextContent('8 idle stages')

    const fly = screen.getByTestId(`pipeline-row-${FLY_ID}`)
    expect(fly).toHaveTextContent('#301')
    expect(fly).toHaveTextContent('Phone-friendly pipeline-stage view')
    expect(fly).toHaveTextContent('grok-4.6 4m')
    expect(fly).toHaveAttribute('aria-label', 'Open #301 — running')

    const queuedRow = screen.getByTestId('pipeline-row-bbbbbbbb-0000-0000-0000-000000000002')
    expect(queuedRow).toHaveTextContent('behind #288')
    expect(queuedRow).toHaveAttribute('aria-label', 'Open #239 — queued')

    const blockedRow = screen.getByTestId('pipeline-row-cccccccc-0000-0000-0000-000000000003')
    expect(blockedRow).toHaveTextContent('blocked')
    expect(blockedRow).toHaveAttribute('aria-label', 'Open #32 — blocked')

    const readyRow = screen.getByTestId('pipeline-row-ready:card-31')
    expect(readyRow).toHaveTextContent('ready 3h')
    expect(readyRow).toHaveAttribute('aria-label', 'Open #31 — ready')
    expect(readyRow).toHaveAttribute(
      'href',
      `/plans?${new URLSearchParams({
        file: 'docs/superpowers/plans/example.md',
        ref: 'abc',
        task: 'dddddddd-0000-0000-0000-000000000004',
      }).toString()}`,
    )
  })

  it('a task row tap writes ?task= and opens the drawer', async () => {
    servePipeline(liveDto())
    renderWithProviders(<PipelineStagesPanel />)
    await screen.findByTestId(`pipeline-row-${FLY_ID}`)

    await userEvent.click(screen.getByTestId(`pipeline-row-${FLY_ID}`))

    await waitFor(() => expect(new URLSearchParams(window.location.search).get('task')).toBe(FLY_ID))
    expect(await screen.findByText('Build the phone stage glance.')).toBeInTheDocument()
  })

  it('a 500 shows the retrying sentence', async () => {
    servePipeline(500)
    renderWithProviders(<PipelineStagesPanel />)
    expect(await screen.findByText("Couldn't load the pipeline — retrying.")).toBeInTheDocument()
  })

  it('an empty fleet shows pipeline-empty', async () => {
    servePipeline(emptyDto())
    renderWithProviders(<PipelineStagesPanel />)
    expect(await screen.findByTestId('pipeline-empty')).toHaveTextContent('Nothing in the pipeline.')
    expect(screen.getByTestId('pipeline-strip')).toHaveTextContent('0 of 6 slots')
    expect(screen.queryByTestId('pipeline-idle')).not.toBeInTheDocument()
  })
})
