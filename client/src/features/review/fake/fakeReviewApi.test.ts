import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../api/client'
import { createFakeReviewApi, type FakeReviewApi } from './fakeReviewApi'
import { runIdFor } from './seed'

const RUN_612 = runIdFor(612)
const NOW = Date.parse('2026-11-22T12:00:00Z')

describe('fake review API state transitions', () => {
  let actor = 'asmith'
  let api: FakeReviewApi

  beforeEach(() => {
    vi.useFakeTimers()
    actor = 'asmith'
    api = createFakeReviewApi({ now: () => NOW, latencyMs: 0, syncDelayMs: 1000, progressStepMs: 1000, actor: () => actor, random: () => 0 })
  })

  afterEach(() => {
    api.dispose()
    vi.useRealTimers()
  })

  it('a batch response changes status at once and syncs Pending → Synced later, recomputing open counts', async () => {
    const { items } = await api.listFindings(RUN_612)
    const f1 = items.find((f) => f.key === 'F1')!
    const f5 = items.find((f) => f.key === 'F5')!
    const before = await api.getRun(RUN_612)

    await expect(api.respondToRun(RUN_612, [{ findingId: f5.id, action: 'Dismiss', reasonCategory: 'FixLater' }])).rejects.toMatchObject({ status: 422 })

    const changed: string[] = []
    api.subscribe((e) => changed.push(e.runId))
    const result = await api.respondToRun(RUN_612, [
      { findingId: f1.id, action: 'WillFix', text: 'Scoping by merchant id.' },
      { findingId: f5.id, action: 'Dismiss', reasonCategory: 'FixLater', scope: 'Instance' },
    ])
    expect(result.items.map((f) => [f.key, f.status, f.syncState])).toEqual([
      ['F1', 'WillFix', 'Pending'],
      ['F5', 'Dismissed', 'Pending'],
    ])
    const after = await api.getRun(RUN_612)
    expect(after.openFindings).toBe(before.openFindings - 1)
    expect(after.openBlockers).toBe(before.openBlockers)

    await vi.advanceTimersByTimeAsync(1000)
    const synced = (await api.listFindings(RUN_612)).items.filter((f) => f.key === 'F1' || f.key === 'F5')
    expect(synced.map((f) => f.syncState)).toEqual(['Synced', 'Synced'])
    expect(synced[0].events.at(-1)).toMatchObject({ action: 'WillFix', actorLogin: 'asmith', syncState: 'Synced' })
    expect(changed.filter((id) => id === RUN_612).length).toBeGreaterThanOrEqual(3)
  })

  it('another round moves the run to round + 1 with the steer stored; the author cannot decide', async () => {
    const run = await api.getRun(RUN_612)
    const gate = run.gates.find((g) => g.status !== 'Done')!
    const humansBefore = (await api.getKpis()).waitingOnHumans

    const refused = await api.decideGate(gate.id, { decision: 'Approve' }).catch((e: unknown) => e)
    expect(refused).toBeInstanceOf(ApiError)
    expect((refused as ApiError).status).toBe(409)

    actor = 'hmartin'
    const next = await api.decideGate(gate.id, { decision: 'AnotherRound', steerPrompt: 'Recheck F3 against UnitOfWorkMiddleware.' })
    expect(next).toMatchObject({ status: 'Running', stage: 'Intake', round: run.round + 1, botOnlyRounds: 0, waitingOn: 'Bot' })
    expect(next.gates.find((g) => g.id === gate.id)).toMatchObject({ status: 'Done', decision: 'AnotherRound', steerPrompt: 'Recheck F3 against UnitOfWorkMiddleware.' })
    expect(next.nodes.filter((n) => n.iteration === 4).map((n) => [n.nodeId, n.status])).toEqual([['intake', 'Running']])

    await vi.advanceTimersByTimeAsync(2000)
    const progressed = await api.getRun(RUN_612)
    expect(progressed.stage).toBe('Categorise')
    await expect(api.decideGate(gate.id, { decision: 'Close' })).rejects.toMatchObject({ status: 409 })
    expect((await api.getKpis()).waitingOnHumans).toBe(humansBefore - 1)
  })

  it('assign random excludes the author and passers; "not me" returns the item to the team queue', async () => {
    const mine = await api.getQueue('Mine', 'asmith')
    const assignedToMe = mine.items.find((i) => i.gate.assigneeLogin === 'asmith')!
    expect(assignedToMe.run.subject.prNumber).toBe(617)

    const passed = await api.passGate(assignedToMe.gate.id, 'Out / at capacity today')
    expect(passed).toMatchObject({ status: 'Unassigned', assigneeLogin: null, assigneeDisplayName: null })
    expect(passed.passes.map((p) => [p.login, p.reason])).toEqual([['asmith', 'Out / at capacity today']])
    expect((await api.getQueue('Mine', 'asmith')).items.some((i) => i.gate.id === passed.id)).toBe(false)
    expect((await api.getQueue('Teams', 'asmith')).items.some((i) => i.gate.id === passed.id)).toBe(true)

    const picked = await api.assignGate(passed.id)
    expect(picked.assigneeLogin).not.toBe('hmartin') // resolved author of #617
    expect(picked.assigneeLogin).not.toBe('asmith') // passed
    expect(picked.status).toBe('Assigned')
  })
})
