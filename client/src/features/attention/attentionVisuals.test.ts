import { describe, expect, it } from 'vitest'
import type { AttentionItemDto, AttentionKind } from '../../api/attention'
import {
  ATTENTION_GROUPS,
  ATTENTION_VISUALS,
  ageSeconds,
  groupOf,
  keyOf,
  targetOf,
} from './attentionVisuals'

/**
 * Every kind the server can send has to be drawable. The `Record` type catches a MISSING key at
 * compile time; this file catches the other half — a key that exists but is empty, and a kind that
 * lands in no group — because both render as a blank row on the one screen whose job is to be read
 * in a hurry.
 */
const ALL_KINDS: AttentionKind[] = [
  'BlockedQuestion',
  'ParkedMessage',
  'DeadSession',
  'NeverStarted',
  'BriefUndelivered',
  'UncorrelatedReport',
  'PastExpectedIdle',
  'ChecksSpent',
  'SessionDisagreement',
  'RecentCriticalIncident',
  'RecentFailure',
  'Overdue',
]

function item(overrides: Partial<AttentionItemDto> & { kind: AttentionKind }): AttentionItemDto {
  return {
    severity: 'Warning',
    taskId: null,
    sessionId: null,
    agentId: null,
    messageId: null,
    title: 'a row',
    headline: 'a headline',
    evidence: '',
    sinceUtc: null,
    subtreeCostUsd: null,
    actions: [],
    ...overrides,
  }
}

describe('attentionVisuals', () => {
  it('maps every kind to a label, a colour, an icon and a hint', () => {
    for (const kind of ALL_KINDS) {
      const visual = ATTENTION_VISUALS[kind]
      expect(visual, kind).toBeDefined()
      expect(visual.label.length, kind).toBeGreaterThan(0)
      expect(visual.icon, kind).toBeTypeOf('function')
      expect(visual.hint.length, kind).toBeGreaterThan(0)
    }
  })

  it('keeps kinds off the violet tier axis', () => {
    // Tier is a ladder, health is a scale, and taskVisuals keeps them on disjoint palettes. A kind
    // badge in violet would read as "this row is a Frontier task".
    for (const kind of ALL_KINDS) {
      expect(ATTENTION_VISUALS[kind].color, kind).not.toBe('violet')
    }
  })

  it('lands every kind in a declared group', () => {
    const declared = new Set(ATTENTION_GROUPS.map((group) => group.key))
    for (const kind of ALL_KINDS) {
      expect(declared.has(groupOf(item({ kind }))), kind).toBe(true)
    }
  })

  it('sorts by severity, except that a settled failure is history rather than suspicion', () => {
    expect(groupOf(item({ kind: 'BlockedQuestion', severity: 'Critical' }))).toBe('now')
    expect(groupOf(item({ kind: 'DeadSession', severity: 'Error' }))).toBe('broken')
    expect(groupOf(item({ kind: 'ChecksSpent', severity: 'Warning' }))).toBe('suspect')
    // Same severity as ChecksSpent, different group: "suspect" implies an open problem and a task
    // that already failed is not one.
    expect(groupOf(item({ kind: 'RecentFailure', severity: 'Warning' }))).toBe('failures')
  })

  it('collapses only the failures group by default', () => {
    expect(ATTENTION_GROUPS.filter((group) => group.collapsed).map((g) => g.key)).toEqual(['failures'])
  })

  it('sends a task row to the drawer on the sibling tab and an agent row to its incidents', () => {
    expect(targetOf(item({ kind: 'BlockedQuestion', taskId: 'task-1' }))).toBe(
      '/orchestrator?tab=delegations&task=task-1',
    )
    expect(targetOf(item({ kind: 'RecentCriticalIncident', agentId: 'agent-1' }))).toBe(
      '/agents?agent=agent-1',
    )
    // A parked message on an unclaimed session names nothing that has a screen — a row with no
    // target must render inert rather than navigating somewhere arbitrary.
    expect(targetOf(item({ kind: 'ParkedMessage', sessionId: 'session-1' }))).toBeNull()
  })

  it('ages a row from when the condition began, and says nothing when that is unknown', () => {
    const now = Date.parse('2026-08-17T12:00:00Z')
    expect(ageSeconds(item({ kind: 'DeadSession', sinceUtc: '2026-08-17T09:00:00Z' }), now)).toBe(10800)
    expect(ageSeconds(item({ kind: 'DeadSession' }), now)).toBeNull()
    // A clock skew must not render a negative age; the row is simply new.
    expect(ageSeconds(item({ kind: 'DeadSession', sinceUtc: '2026-08-17T12:05:00Z' }), now)).toBe(0)
  })

  it('keys a row by its subject so a poll does not remount it', () => {
    const first = item({ kind: 'DeadSession', taskId: 'task-1', sessionId: 'session-1' })
    // The headline counts elapsed time and changes on every poll; keying on it would remount every
    // row every fifteen seconds.
    expect(keyOf(first)).toBe(keyOf({ ...first, headline: 'the headline changed' }))
    expect(keyOf(first)).not.toBe(keyOf({ ...first, taskId: 'task-2' }))
  })

  it('separates two incident groups on the same agent', () => {
    // RecentCriticalIncident is grouped per agent AND per incident kind, and the DTO carries the
    // kind only inside the prose headline — so these two rows match on every id field. Without
    // sinceUtc in the key they share one React key and one of them stops updating.
    const bindFailures = item({
      kind: 'RecentCriticalIncident',
      agentId: 'agent-1',
      sinceUtc: '2026-08-16T10:02:23Z',
    })
    const deliveryFailures = { ...bindFailures, sinceUtc: '2026-08-17T04:11:00Z' }
    expect(keyOf(bindFailures)).not.toBe(keyOf(deliveryFailures))
  })
})
