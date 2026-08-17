import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AttentionItemDto } from '../../api/attention'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { MobileHomePage } from './MobileHomePage'
import { formatClockTime } from './workLineFormat'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
vi.setConfig({ testTimeout: 20_000 })

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

function seed({
  attention = [] as AttentionItemDto[],
  tasks = [] as unknown[],
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
