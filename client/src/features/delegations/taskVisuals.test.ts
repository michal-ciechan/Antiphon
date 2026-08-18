import { describe, expect, it } from 'vitest'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import {
  LANES,
  STATUS_COLOR,
  TIER_VISUALS,
  buildTaskForest,
  countSubtree,
  elapsedSeconds,
  formatCost,
  formatDuration,
  isLegacyCostEstimate,
  laneOf,
  subtreeIds,
  tierAlias,
  tierTooltip,
  totalTokens,
} from './taskVisuals'

function task(overrides: Partial<AgentTaskSummaryDto> & { id: string }): AgentTaskSummaryDto {
  return {
    rootTaskId: overrides.rootTaskId ?? overrides.id,
    parentTaskId: null,
    depth: 0,
    title: overrides.id,
    kind: 'Worker',
    role: 'Custom',
    agentKind: 'ClaudeCode',
    modelLevel: 'High',
    escalatedFrom: null,
    status: 'Queued',
    workspace: 'Shared',
    workingDirectory: 'C:/src/antiphon',
    repoPath: 'C:/src/antiphon',
    scopeGlob: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-07T10:00:00Z',
    dispatchedAt: null,
    completedAt: null,
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 0,
    costPricingVersion: 2,
    worktreePath: null,
    worktreeBranch: null,
    subtreeCostUsd: 0,
    childCount: 0,
    expectedDurationMinutes: 10,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
}

describe('the tier axis', () => {
  it('never borrows a health colour, so rank cannot read as trouble', () => {
    // A ladder rendered in green/orange/red says "this task is fine / in trouble", which is a
    // different question from "how expensive is the model running it". Grey is the deliberate
    // exception: it means "nothing to say" on both axes, which is why it is the bottom rung.
    const health = new Set(Object.values(STATUS_COLOR).filter((colour) => colour !== 'gray'))
    for (const tier of Object.values(TIER_VISUALS)) {
      expect(health.has(tier.color), `${tier.alias} must not use a health colour`).toBe(false)
    }
  })

  it('separates the rungs by intensity, not by hue', () => {
    // Four shades of one violet reads as rank; four different hues reads as four categories.
    const variants = Object.values(TIER_VISUALS).map((tier) => tier.variant)
    expect(new Set(variants).size).toBeGreaterThan(1)
    expect(new Set(Object.values(TIER_VISUALS).map((t) => t.color)).size).toBeLessThanOrEqual(2)
  })

  it('orders the four tiers by rank, most capable first', () => {
    expect(TIER_VISUALS.Frontier.rank).toBeLessThan(TIER_VISUALS.High.rank)
    expect(TIER_VISUALS.High.rank).toBeLessThan(TIER_VISUALS.Medium.rank)
    expect(TIER_VISUALS.Medium.rank).toBeLessThan(TIER_VISUALS.Low.rank)
  })
})

describe('the tier alias', () => {
  it('names a Claude task exactly what it named before AgentKind existed', () => {
    // The whole kind-aware change is only safe if Claude's four chips are byte-identical: this is
    // the control, and it is also what an omitted kind must fall back to (the server's own
    // ModelLevelAliases.For does the same for anything that is not Grok).
    expect(tierAlias('Frontier', 'ClaudeCode')).toBe('fable')
    expect(tierAlias('High', 'ClaudeCode')).toBe('opus')
    expect(tierAlias('Medium', 'ClaudeCode')).toBe('sonnet')
    expect(tierAlias('Low', 'ClaudeCode')).toBe('haiku')
    expect(tierAlias('Frontier')).toBe('fable')
  })

  it('names a Grok task the model it actually runs, not a Claude one', () => {
    // A chip reading "fable" on a Grok delegate names a model nobody is paying for, on the one
    // surface an operator scans to decide what to escalate.
    expect(tierAlias('Frontier', 'Grok')).toBe('grok-4.6')
    expect(tierAlias('High', 'Grok')).toBe('grok-4.6')
    expect(tierAlias('Medium', 'Grok')).toBe('grok-4.5')
    expect(tierAlias('Low', 'Grok')).toBe('grok-4.5')
  })

  it('lets Grok collapse two rungs to one name without collapsing the rungs', () => {
    // xAI ships two models, so the ladder is shorter than the tier axis — but Frontier and High are
    // still different rungs (different price, different escalation), which is why only the NAME
    // repeats and the violet intensity does not.
    expect(tierAlias('Frontier', 'Grok')).toBe(tierAlias('High', 'Grok'))
    expect(TIER_VISUALS.Frontier.variant).not.toBe(TIER_VISUALS.High.variant)
  })

  it('keeps the vendor word only where the alias does not already carry it', () => {
    expect(tierTooltip('High', 'ClaudeCode')).toBe('High tier — Claude opus')
    expect(tierTooltip('High')).toBe('High tier — Claude opus')
    // "Grok grok-4.6" would be a stutter — the alias already names the family.
    expect(tierTooltip('High', 'Grok')).toBe('High tier — grok-4.6')
  })
})

describe('lanes', () => {
  it('places every status in exactly one lane', () => {
    const statuses = Object.keys(STATUS_COLOR) as Array<keyof typeof STATUS_COLOR>
    for (const status of statuses) {
      const matching = LANES.filter((lane) => lane.statuses.includes(status))
      expect(matching, `${status} should be in one lane`).toHaveLength(1)
    }
  })

  it('treats Dispatched and Working as one thing — a delegate is on it either way', () => {
    expect(laneOf('Dispatched')).toBe('working')
    expect(laneOf('Working')).toBe('working')
  })

  it('keeps Blocked out of Done — a question needs an answer, not filing', () => {
    expect(laneOf('Blocked')).toBe('blocked')
  })
})

describe('buildTaskForest', () => {
  it('nests children under their parent', () => {
    const forest = buildTaskForest([
      task({ id: 'root', kind: 'Orchestrator' }),
      task({ id: 'child', parentTaskId: 'root', rootTaskId: 'root', createdAt: '2026-08-07T10:01:00Z' }),
      task({ id: 'grandchild', parentTaskId: 'child', rootTaskId: 'root', createdAt: '2026-08-07T10:02:00Z' }),
    ])

    expect(forest).toHaveLength(1)
    expect(forest[0].task.id).toBe('root')
    expect(forest[0].children[0].task.id).toBe('child')
    expect(forest[0].children[0].children[0].task.id).toBe('grandchild')
  })

  it('promotes a task whose parent is missing rather than dropping it', () => {
    // A filtered or partial listing must never silently lose work — an orphan is still a task
    // someone is paying for.
    const forest = buildTaskForest([task({ id: 'orphan', parentTaskId: 'not-in-this-set' })])

    expect(forest.map((n) => n.task.id)).toEqual(['orphan'])
  })

  it('puts the newest run first and orders each subtree oldest first', () => {
    const forest = buildTaskForest([
      task({ id: 'older-run', createdAt: '2026-08-07T09:00:00Z' }),
      task({ id: 'newer-run', createdAt: '2026-08-07T11:00:00Z' }),
      task({ id: 'second', parentTaskId: 'newer-run', createdAt: '2026-08-07T11:02:00Z' }),
      task({ id: 'first', parentTaskId: 'newer-run', createdAt: '2026-08-07T11:01:00Z' }),
    ])

    expect(forest.map((n) => n.task.id)).toEqual(['newer-run', 'older-run'])
    expect(forest[0].children.map((n) => n.task.id)).toEqual(['first', 'second'])
  })

  it('counts a whole subtree, including itself', () => {
    const forest = buildTaskForest([
      task({ id: 'root' }),
      task({ id: 'a', parentTaskId: 'root' }),
      task({ id: 'b', parentTaskId: 'a' }),
    ])

    expect(countSubtree(forest[0])).toBe(3)
    expect([...subtreeIds(forest[0])].sort()).toEqual(['a', 'b', 'root'])
  })
})

describe('formatting', () => {
  it('counts a running task up from dispatch, not from creation', () => {
    // Time queued is not time worked; charging a task for the dispatcher's backlog would make the
    // concurrency cap look like a slow delegate.
    const now = Date.parse('2026-08-07T10:05:00Z')
    const running = task({ id: 't', dispatchedAt: '2026-08-07T10:02:00Z' })

    expect(elapsedSeconds(running, now)).toBe(180)
  })

  it('shows a settled task what it took, not how long ago it was', () => {
    const now = Date.parse('2026-08-07T18:00:00Z')
    const done = task({
      id: 't',
      dispatchedAt: '2026-08-07T10:00:00Z',
      completedAt: '2026-08-07T10:04:00Z',
      status: 'Succeeded',
    })

    expect(elapsedSeconds(done, now)).toBe(240)
  })

  it('shows how long a queued task has been waiting', () => {
    const now = Date.parse('2026-08-07T10:00:30Z')
    expect(elapsedSeconds(task({ id: 't' }), now)).toBe(30)
  })

  it.each([
    [45, '45s'],
    [90, '1m30'],
    [3_720, '1h02m'],
  ])('formats %i seconds as %s', (seconds, expected) => {
    expect(formatDuration(seconds)).toBe(expected)
  })

  it('keeps sub-cent spend visible instead of rounding a haiku task to nothing', () => {
    expect(formatCost(0.0031)).toBe('$0.0031')
    expect(formatCost(1.234)).toBe('$1.23')
    expect(formatCost(0)).toBe('$0')
  })

  it('counts cached tokens in the total a human reads as "tokens"', () => {
    // The three input counters are stored apart because they are PRICED apart, but the cache-read
    // term is most of what an agentic session actually read — dropping it under-reports by ~100x.
    const t = task({
      id: 'a',
      tokensIn: 400,
      cacheReadTokens: 2_000_000,
      cacheCreationTokens: 30_000,
      tokensOut: 5_000,
    })
    expect(totalTokens(t)).toBe(2_035_400)
  })

  it('labels a cost written by the pre-fix pricing model rather than passing it off as current', () => {
    // Version 0 rows billed cache reads as fresh input against a stale rate table — ~10x high, and
    // the per-root ceiling still sums them, so a human comparing runs has to be told.
    expect(isLegacyCostEstimate(task({ id: 'a', costUsd: 31.29, costPricingVersion: 0 }))).toBe(true)
    expect(isLegacyCostEstimate(task({ id: 'b', costUsd: 1.65, costPricingVersion: 2 }))).toBe(false)
    expect(isLegacyCostEstimate(task({ id: 'c', costUsd: 0, costPricingVersion: 0 }))).toBe(false)
  })
})
