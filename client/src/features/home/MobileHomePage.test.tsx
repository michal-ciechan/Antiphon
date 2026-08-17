import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AttentionItemDto } from '../../api/attention'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AWAY_LAST_SEEN_KEY } from './awayDelta'
import { MobileHomePage } from './MobileHomePage'
import { formatClockTime } from './workLineFormat'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
vi.setConfig({ testTimeout: 20_000 })

beforeEach(() => window.localStorage.clear())

function task(overrides: Record<string, unknown> = {}) {
  return {
    id: '11111111-0000-0000-0000-000000000001',
    rootTaskId: '11111111-0000-0000-0000-000000000001',
    parentTaskId: null,
    depth: 0,
    title: 'CARD-0056 - launch leak - slices 3+4',
    kind: 'Worker',
    role: 'Code',
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

function attentionItem(overrides: Partial<AttentionItemDto> = {}): AttentionItemDto {
  return {
    kind: 'BlockedQuestion',
    severity: 'Critical',
    taskId: 't1',
    sessionId: null,
    agentId: null,
    messageId: null,
    title: '#56 launch leak · reconcile pass',
    headline: 'Your call: cap re-adoptions at 3 per uptime?',
    evidence: '',
    sinceUtc: '2026-08-17T09:00:00Z',
    subtreeCostUsd: null,
    actions: ['Reply'],
    ...overrides,
  }
}

function card(overrides: Record<string, unknown> = {}) {
  return {
    id: 'c1',
    boardId: 'b1',
    boardColumnId: 'col1',
    ownerSessionId: null,
    currentWorktreeId: null,
    assignedAgentId: null,
    assignedAgentName: null,
    agentQueuePosition: null,
    activeWorkflowRunId: null,
    workflowRunStatus: null,
    currentWorkflowStageName: null,
    identifier: 'CARD-0067',
    title: 'reply durability',
    description: '',
    priority: 0,
    labels: [],
    status: 'Done',
    concurrencyToken: 'ct',
    createdAt: '2026-08-16T08:00:00Z',
    updatedAt: '2026-08-17T10:00:00Z',
    startedAt: null,
    completedAt: null,
    terminalReason: null,
    sessions: [],
    revisionCount: 1,
    archivedAt: null,
    archivedReason: null,
    archivedBy: null,
    ...overrides,
  }
}

function seed({
  attention = [] as AttentionItemDto[],
  tasks = [] as unknown[],
  cards = [] as unknown[],
} = {}) {
  server.use(
    http.get('/api/attention', () =>
      HttpResponse.json({
        generatedAt: '2026-08-17T10:00:00Z',
        runnerConsulted: true,
        items: attention,
      }),
    ),
    http.get('/api/agent-tasks', () => HttpResponse.json(tasks)),
    http.get('/api/boards', () =>
      HttpResponse.json(
        cards.length === 0
          ? []
          : [{ id: 'b1', projectId: 'p1', projectName: 'antiphon', name: 'main', description: '', trackerKind: 'Internal', maxConcurrentSessions: 1, cardCount: cards.length, createdAt: '2026-08-16T08:00:00Z', updatedAt: '2026-08-17T10:00:00Z' }],
      ),
    ),
    http.get('/api/boards/b1', () =>
      HttpResponse.json({
        id: 'b1',
        projectId: 'p1',
        projectName: 'antiphon',
        name: 'main',
        description: '',
        trackerKind: 'Internal',
        maxConcurrentSessions: 1,
        columns: [
          {
            id: 'col1',
            stateKey: 'done',
            name: 'Done',
            columnOrder: 0,
            cardStatus: 'Done',
            isActive: false,
            isTerminal: true,
            maxConcurrentSessions: null,
            cards,
          },
        ],
        createdAt: '2026-08-16T08:00:00Z',
        updatedAt: '2026-08-17T10:00:00Z',
      }),
    ),
  )
}

describe('MobileHomePage', () => {
  it('a healthy day reads as calm, not empty: no band 1, a forecast, and the live lines', async () => {
    // The spec's bar (§D3): when nothing needs you, the screen still says when to next expect
    // something, so the operator knows whether to wait or to chase.
    seed({ tasks: [task({})] })
    renderWithProviders(<MobileHomePage />)

    const calm = await screen.findByTestId('calm-state')
    expect(calm).toHaveTextContent('Nothing needs you.')
    expect(calm).toHaveTextContent(
      `Next check-in ${formatClockTime('2026-08-17T13:02:00Z')} — you'll see its reading here.`,
    )
    // Band 1 is absent entirely — no heading, no empty-state scaffolding.
    expect(screen.queryByText(/Needs you ·/)).not.toBeInTheDocument()
    // The calm card leads, the live lines follow.
    const text = screen.getByTestId('mobile-home').textContent ?? ''
    expect(text.indexOf('Nothing needs you.')).toBeLessThan(text.indexOf('In motion'))
    expect(screen.getByText('#56 launch leak - slices 3+4 · opus')).toBeInTheDocument()
  })

  it('with nothing running either, the calm card still forecasts where news will land', async () => {
    seed({})
    renderWithProviders(<MobileHomePage />)

    const calm = await screen.findByTestId('calm-state')
    expect(calm).toHaveTextContent('Nothing is running. Whatever finishes next will appear below.')
    expect(screen.getByText('Nothing running.')).toBeInTheDocument()
  })

  it('running work whose checks are all spent is named, not silently unforecast', async () => {
    seed({ tasks: [task({ nextCheckAt: null, checkCount: 3 })] })
    renderWithProviders(<MobileHomePage />)

    const calm = await screen.findByTestId('calm-state')
    expect(calm).toHaveTextContent('No more check-ins scheduled — the live lines below are the signal.')
    expect(screen.getByText('checks spent · $1.12')).toBeInTheDocument()
  })

  it('a blocked question tops the screen with the reply box already open, and the answer posts', async () => {
    seed({ attention: [attentionItem({})], tasks: [task({})] })
    const replies: unknown[] = []
    server.use(
      http.post('/api/agent-tasks/t1/reply', async ({ request }) => {
        replies.push(await request.json())
        return HttpResponse.json(task({ id: 't1', status: 'Working' }))
      }),
    )
    renderWithProviders(<MobileHomePage />)

    expect(await screen.findByText('Needs you · 1')).toBeInTheDocument()
    expect(screen.queryByTestId('calm-state')).not.toBeInTheDocument()
    // Band 1 leads, band 2 follows.
    const text = screen.getByTestId('mobile-home').textContent ?? ''
    expect(text.indexOf('Needs you')).toBeLessThan(text.indexOf('In motion'))

    // The reply box is open in place — no tap spent revealing it (CARD-0033's ask, §D3).
    const box = screen.getByLabelText('Answer the delegate')
    await userEvent.type(box, 'cap at 3')
    await userEvent.click(screen.getByRole('button', { name: /Send answer/ }))
    await waitFor(() => expect(replies).toEqual([{ message: 'cap at 3' }]))
  })

  it('a parked message carries its two queue verbs, and Send now hits the queue endpoint', async () => {
    seed({
      attention: [
        attentionItem({
          kind: 'ParkedMessage',
          taskId: null,
          sessionId: 's1',
          messageId: 'm1',
          title: 'Family chat reply',
          headline: '3 delivery attempts spent.',
          evidence: 'Guest list drafted — 42 names.',
          actions: ['SendNow', 'CancelMessage'],
        }),
      ],
    })
    let sent = 0
    server.use(
      http.post('/api/sessions/s1/messages/m1/send-now', () => {
        sent += 1
        return HttpResponse.json({})
      }),
    )
    renderWithProviders(<MobileHomePage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Send now' }))
    await waitFor(() => expect(sent).toBe(1))
    expect(screen.getByRole('button', { name: 'Drop message' })).toBeInTheDocument()
  })

  it('the away band leads with what finished, quoting the report’s own first line', async () => {
    const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString()
    const lastSeen = minutesAgo(90)
    window.localStorage.setItem(AWAY_LAST_SEEN_KEY, lastSeen)
    seed({
      tasks: [
        task({}),
        task({
          id: 't-settled',
          title: 'CARD-0067 - reply durability',
          status: 'Succeeded',
          completedAt: minutesAgo(30),
          costUsd: 0.5,
          nextCheckAt: null,
          checkCount: 0,
        }),
        task({
          id: 't-old',
          title: 'settled before the window',
          status: 'Succeeded',
          completedAt: minutesAgo(240),
          nextCheckAt: null,
          checkCount: 0,
        }),
      ],
    })
    server.use(
      http.get('/api/agent-tasks/t-settled', () =>
        HttpResponse.json({
          summary: task({ id: 't-settled' }),
          goal: 'make the reply route durable',
          result: 'Landed the route; 6 tests green.\nLong detail follows.',
          resultFilePath: null,
          failureReason: null,
          mergeTargetRef: null,
          events: [],
        }),
      ),
    )
    renderWithProviders(<MobileHomePage />)

    expect(
      await screen.findByText('#67 reply durability — finished — Landed the route; 6 tests green.'),
    ).toBeInTheDocument()
    expect(
      screen.getByText(`While you were away · since ${formatClockTime(lastSeen)}`),
    ).toBeInTheDocument()
    // Settled-before-the-window work stays out; the spend line counts only what settled inside it.
    expect(screen.queryByText(/settled before the window/)).not.toBeInTheDocument()
    expect(screen.getByText('Spent $0.50 on work that settled in this window.')).toBeInTheDocument()
    // Band order: live lines above the delta.
    const text = screen.getByTestId('mobile-home').textContent ?? ''
    expect(text.indexOf('In motion')).toBeLessThan(text.indexOf('While you were away'))
  })

  it('cards that changed state land in the band, linking to their board', async () => {
    const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString()
    window.localStorage.setItem(AWAY_LAST_SEEN_KEY, minutesAgo(90))
    seed({
      cards: [
        card({
          id: 'c-done',
          identifier: 'CARD-0071',
          title: 'guest list flow',
          completedAt: minutesAgo(20),
        }),
      ],
    })
    renderWithProviders(<MobileHomePage />)

    const row = await screen.findByText('#71 guest list flow — done')
    expect(row.closest('a')).toHaveAttribute('href', '/boards/b1')
  })

  it('a first visit reads as the last 24h, says so, and stamps the visit for next time', async () => {
    seed({})
    renderWithProviders(<MobileHomePage />)

    expect(await screen.findByText('While you were away · last 24h')).toBeInTheDocument()
    expect(screen.getByTestId('away-empty')).toHaveTextContent('Nothing finished while you were away.')
    await waitFor(() => {
      const stamped = window.localStorage.getItem(AWAY_LAST_SEEN_KEY)
      expect(stamped).not.toBeNull()
      expect(Number.isNaN(Date.parse(stamped!))).toBe(false)
    })
  })

  it('warning-severity rows stay on the desktop diagnostic tab — the phone gets Critical/Error only', async () => {
    seed({
      attention: [
        attentionItem({ kind: 'ChecksSpent', severity: 'Warning', title: 'Sweep the logs' }),
      ],
    })
    renderWithProviders(<MobileHomePage />)

    expect(await screen.findByTestId('calm-state')).toBeInTheDocument()
    expect(screen.queryByText(/Needs you ·/)).not.toBeInTheDocument()
  })
})
