import { HttpResponse, http } from 'msw'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { AgentTaskDetailDto, AgentTaskSummaryDto, VerificationProfileDto } from '../../api/agentTasks'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { TaskDetailBody } from './TaskDetailBody'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

// CARD-0544 V-8: the drawer mirrors the server's commissioned profile, read-only.
const TASK_ID = 'c5440000-0000-0000-0000-000000000001'
const OWNER_ID = 'c5440000-0000-0000-0000-0000000000aa'
const BASELINE_ID = 'c5440000-0000-0000-0000-0000000000bb'
const BASELINE_SHA = '0123456789abcdef0123456789abcdef01234567'

const writes: string[] = []
const recordWrites = ({ request }: { request: Request }) => {
  if (request.method !== 'GET') writes.push(`${request.method} ${new URL(request.url).pathname}`)
}

beforeEach(() => {
  writes.length = 0
  server.events.on('request:start', recordWrites)
})

afterEach(() => {
  server.events.removeListener('request:start', recordWrites)
})

function summary(overrides: Partial<AgentTaskSummaryDto> = {}): AgentTaskSummaryDto {
  return {
    id: TASK_ID,
    rootTaskId: TASK_ID,
    parentTaskId: null,
    depth: 0,
    title: 'CARD-0544 repair round',
    kind: 'Worker',
    role: 'Code',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Worktree',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    worktreePath: null,
    worktreeBranch: null,
    scope: null,
    agentId: 'agent-1',
    agentName: 'task-c544',
    agentSessionId: 'session-1',
    attempt: 1,
    createdAt: '2026-09-17T09:00:00Z',
    dispatchedAt: '2026-09-17T09:10:00Z',
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

function detail(overrides: Partial<AgentTaskDetailDto> = {}): AgentTaskDetailDto {
  return {
    summary: summary(),
    goal: 'Repair the scope parser.',
    result: null,
    resultFilePath: null,
    failureReason: null,
    mergeTargetRef: null,
    events: [{ type: 'Created', modelLevel: 'High', detail: 'Created.', at: '2026-09-17T09:00:00Z' }],
    ...overrides,
  }
}

function interim(overrides: Partial<VerificationProfileDto> = {}): VerificationProfileDto {
  return {
    version: 1,
    round: 'Interim',
    subjectTaskId: OWNER_ID,
    baselineOutcomeId: BASELINE_ID,
    baselineReviewedSha: BASELINE_SHA,
    selection: { artifactPath: 'docs/plans/c544.md', artifactCommitSha: BASELINE_SHA, section: 'Round selection' },
    finalReviewPending: true,
    ownerRequiresFinalReview: true,
    holdReason: null,
    readinessRecordedAt: '2026-09-17T08:55:00Z',
    ...overrides,
  }
}

it('C544 renders commissioned scope and final obligation', async () => {
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(detail({ verification: interim() }))))
  renderWithProviders(<TaskDetailBody taskId={TASK_ID} onClose={() => {}} />)
  const profile = await screen.findByTestId('task-verification-profile')
  expect(profile).toHaveTextContent('Round: Interim (profile v1)')
  expect(profile).toHaveTextContent('Final review: pending (deferred ordinary work owed)')
  expect(profile).toHaveTextContent('owner requires a Final/Full Review to land')
  expect(profile).toHaveTextContent(`Subject: ${OWNER_ID}`)
  expect(profile).not.toHaveTextContent('Round: Final')
  expect(writes).toEqual([])
})

it('C544 does not display legacy Unknown as Full', async () => {
  // A historical task carries no profile at all; a profiled Final task has no pending obligation.
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(detail({ verification: null }))))
  const legacy = renderWithProviders(<TaskDetailBody taskId={TASK_ID} onClose={() => {}} />)
  expect(await screen.findByText('Repair the scope parser.')).toBeInTheDocument()
  expect(screen.queryByTestId('task-verification-profile')).not.toBeInTheDocument()
  expect(screen.queryByText(/Full/)).not.toBeInTheDocument()
  legacy.unmount()

  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(detail({
    summary: summary({ id: 'c5440000-0000-0000-0000-000000000002' }),
    verification: interim({
      round: 'Final', subjectTaskId: null, baselineOutcomeId: null, baselineReviewedSha: null,
      selection: null, finalReviewPending: false, ownerRequiresFinalReview: false, readinessRecordedAt: null,
    }),
  }))))
  renderWithProviders(<TaskDetailBody taskId="c5440000-0000-0000-0000-000000000002" onClose={() => {}} />)
  const profile = await screen.findByTestId('task-verification-profile')
  expect(profile).toHaveTextContent('Round: Final (profile v1)')
  expect(profile).toHaveTextContent('Final review: none pending')
  expect(profile).not.toHaveTextContent(/scope=Full|Full scope/)
  expect(writes).toEqual([])
})

it('C544 exposes baseline and readiness refusal', async () => {
  const hold = 'verification_backstop_unready: Nightly backstop is not ready for Interim (monitor_stale); request Final instead.'
  server.use(http.get('/api/agent-tasks/:id', () => HttpResponse.json(detail({
    summary: summary({ status: 'Blocked' }),
    failureReason: hold,
    verification: interim({ holdReason: hold }),
  }))))
  renderWithProviders(<TaskDetailBody taskId={TASK_ID} onClose={() => {}} />)
  const profile = await screen.findByTestId('task-verification-profile')
  expect(profile).toHaveTextContent(`Baseline: ${BASELINE_ID} at ${BASELINE_SHA}`)
  expect(profile).toHaveTextContent(`Selection: docs/plans/c544.md@${BASELINE_SHA} Round selection`)
  expect(profile).toHaveTextContent(`Held: ${hold}`)
  expect(profile).toHaveTextContent('Readiness monitor: 2026-09-17T08:55:00Z')
  expect(writes).toEqual([])
})
