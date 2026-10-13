import { expect, it } from 'vitest'
import {
  diffDrafts,
  draftFromOverrides,
  isCleared,
  overridesFromDraft,
  validateDraft,
  type ConcurrencyDraft,
  type LimitDraft,
} from './dispatchConcurrencyModel'

// CARD-0506 test design F-1/F-2: literal fixtures, hand-written expected outcomes. Nothing below is
// derived by calling the production decoder/serializer/validator/diff for its expected value.
const ROLES = [
  'Custom', 'Plan', 'Code', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge',
  'Investigate', 'TestDesign', 'Mutation',
] as const
const RANGES = { parallelMin: 1, parallelMax: 512, queuedMin: 0, queuedMax: 4096 }
const UNSET: LimitDraft = { specified: false, value: null }

function set(value: number | null): LimitDraft {
  return { specified: true, value }
}

function blankDraft(): ConcurrencyDraft {
  return {
    maxParallel: { ...UNSET },
    maxQueued: { ...UNSET },
    roles: Object.fromEntries(ROLES.map((role) => [role, { maxParallel: { ...UNSET }, maxQueued: { ...UNSET } }])),
  }
}

// ---- V-1 draftFromOverrides: absent, null and zero stay distinct ----

it('C506 V01 absent-overall', () => {
  const draft = draftFromOverrides({ schemaVersion: 1, roles: { Code: { maxParallel: 3 } } }, ROLES)
  expect(draft.maxParallel).toStrictEqual({ specified: false, value: null })
  expect(draft.maxQueued).toStrictEqual({ specified: false, value: null })
  expect(draft.roles.Plan).toStrictEqual({ maxParallel: { specified: false, value: null }, maxQueued: { specified: false, value: null } })
  expect(draft.mode).toBeUndefined()
})

it('C506 V01 null-overall-queued', () => {
  const draft = draftFromOverrides({ schemaVersion: 1, maxQueued: null }, ROLES)
  expect(draft.maxQueued).toStrictEqual({ specified: true, value: null })
  expect(draft.maxParallel).toStrictEqual({ specified: false, value: null })
})

it('C506 V01 null-role-parallel', () => {
  const draft = draftFromOverrides({ schemaVersion: 1, roles: { Code: { maxParallel: null } } }, ROLES)
  expect(draft.roles.Code.maxParallel).toStrictEqual({ specified: true, value: null })
  expect(draft.roles.Code.maxQueued).toStrictEqual({ specified: false, value: null })
})

it('C506 V01 null-role-queued', () => {
  const draft = draftFromOverrides({ schemaVersion: 1, roles: { Review: { maxQueued: null } } }, ROLES)
  expect(draft.roles.Review.maxQueued.specified).toBe(true)
  expect(draft.roles.Review.maxQueued.value).toBeNull()
  expect(draft.roles.Review.maxParallel).toStrictEqual({ specified: false, value: null })
})

it('C506 V01 zero-queued', () => {
  const draft = draftFromOverrides({ schemaVersion: 1, maxQueued: 0, roles: { Code: { maxQueued: 0 } } }, ROLES)
  expect(draft.maxQueued).toStrictEqual({ specified: true, value: 0 })
  expect(draft.roles.Code.maxQueued).toStrictEqual({ specified: true, value: 0 })
})

it('C506 V01 numeric-role', () => {
  const draft = draftFromOverrides({ schemaVersion: 1, mode: 'SeparateQueues', roles: { Code: { maxParallel: 3 } } }, ROLES)
  expect(draft.roles.Code.maxParallel).toStrictEqual({ specified: true, value: 3 })
  expect(draft.roles.Code.maxQueued).toStrictEqual({ specified: false, value: null })
  expect(draft.mode).toBe('SeparateQueues')
})

// ---- V-2 overridesFromDraft: exact key membership ----

it('C506 V02 omit-overall', () => {
  const draft = blankDraft()
  draft.maxParallel = set(4)
  draft.roles.Code.maxParallel = set(3)
  const out = overridesFromDraft(draft)
  expect(out).toStrictEqual({ maxParallel: 4, roles: { Code: { maxParallel: 3 } } })
  expect(Object.hasOwn(out, 'maxQueued')).toBe(false)
})

it('C506 V02 omit-role', () => {
  const draft = blankDraft()
  draft.maxParallel = set(4)
  draft.maxQueued = set(12)
  draft.roles.Code.maxParallel = set(3)
  const out = overridesFromDraft(draft)
  expect(out).toStrictEqual({ maxParallel: 4, maxQueued: 12, roles: { Code: { maxParallel: 3 } } })
  expect(Object.hasOwn(out.roles as object, 'Review')).toBe(false)
})

it('C506 V02 explicit-null', () => {
  const draft = blankDraft()
  draft.roles.Review.maxQueued = set(null)
  const out = overridesFromDraft(draft)
  expect(out).toStrictEqual({ roles: { Review: { maxQueued: null } } })
  const review = (out.roles as Record<string, Record<string, unknown>>).Review
  expect(Object.hasOwn(review, 'maxQueued')).toBe(true)
  expect(review.maxQueued).toBeNull()
})

it('C506 V02 preserve-mode', () => {
  const draft = blankDraft()
  draft.mode = 'SeparateQueues'
  draft.maxParallel = set(4)
  const out = overridesFromDraft(draft)
  expect(out).toStrictEqual({ mode: 'SeparateQueues', maxParallel: 4 })
  expect(out.mode).toBe('SeparateQueues')
  expect(isCleared(out)).toBe(false)
})

it('C506 V02 omit-schema', () => {
  const draft = blankDraft()
  draft.maxParallel = set(4)
  const out = overridesFromDraft(draft)
  expect(out).toStrictEqual({ maxParallel: 4 })
  expect(Object.hasOwn(out, 'schemaVersion')).toBe(false)
})

it('C506 V02 empty-document', () => {
  const out = overridesFromDraft(blankDraft())
  expect(out).toStrictEqual({})
  expect(isCleared(out)).toBe(true)
})

// ---- V-3 validateDraft: integer bounds from ranges, nullable fields, reason ----

type FieldName = 'overall-parallel' | 'overall-queued' | 'role-parallel' | 'role-queued'
const FIELD_PATH: Record<FieldName, string> = {
  'overall-parallel': 'maxParallel',
  'overall-queued': 'maxQueued',
  'role-parallel': 'roles.Code.maxParallel',
  'role-queued': 'roles.Code.maxQueued',
}
const PARALLEL_VALUES: Record<string, unknown> = {
  null: null, below: 0, min: 1, max: 512, above: 513, fraction: 1.5, string: '1', blank: '', nan: NaN, infinity: Infinity,
}
const QUEUED_VALUES: Record<string, unknown> = {
  null: null, below: -1, min: 0, max: 4096, above: 4097, fraction: 0.5, string: '0', blank: '', nan: NaN, infinity: Infinity,
}
// Hand-written verdicts: only min/max pass everywhere; null passes only on the three nullable kinds.
const ACCEPTED: Record<FieldName, readonly string[]> = {
  'overall-parallel': ['min', 'max'],
  'overall-queued': ['null', 'min', 'max'],
  'role-parallel': ['null', 'min', 'max'],
  'role-queued': ['null', 'min', 'max'],
}

function setField(draft: ConcurrencyDraft, field: FieldName, limit: LimitDraft) {
  if (field === 'overall-parallel') draft.maxParallel = limit
  else if (field === 'overall-queued') draft.maxQueued = limit
  else if (field === 'role-parallel') draft.roles.Code.maxParallel = limit
  else draft.roles.Code.maxQueued = limit
}

const FIELD_CASES = (['overall-parallel', 'overall-queued', 'role-parallel', 'role-queued'] as FieldName[]).flatMap((field) =>
  Object.entries(field.endsWith('parallel') ? PARALLEL_VALUES : QUEUED_VALUES).map(([value, raw]) => ({
    field,
    value,
    raw,
    expected: ACCEPTED[field].includes(value) ? 'accept' : FIELD_PATH[field],
  })),
)

it.each(FIELD_CASES)('C506 V03 $field $value', ({ field, raw, expected }) => {
  const draft = blankDraft()
  // Typed illegal values (string, blank, NaN, Infinity, fraction) enter through a deliberate cast.
  setField(draft, field, { specified: true, value: raw as unknown as number | null })
  const errors = validateDraft(draft, RANGES, 'ok')
  if (expected === 'accept') {
    expect(errors).toStrictEqual({})
  } else {
    expect(Object.keys(errors)).toStrictEqual([expected])
    expect(errors[expected]).toEqual(expect.any(String))
    expect(errors[expected].length).toBeGreaterThan(0)
  }
})

const REASON_CASES = [
  { variant: 'empty', reason: '', expected: 'reason' },
  { variant: 'whitespace', reason: '   ', expected: 'reason' },
  { variant: 'one', reason: 'x', expected: 'accept' },
  { variant: 'trimmed', reason: '  fine  ', expected: 'accept' },
  { variant: '400', reason: 'a'.repeat(400), expected: 'accept' },
  { variant: '401', reason: 'a'.repeat(401), expected: 'reason' },
]

it.each(REASON_CASES)('C506 V03 reason $variant', ({ reason, expected }) => {
  const draft = blankDraft()
  draft.maxParallel = set(4)
  const errors = validateDraft(draft, RANGES, reason)
  if (expected === 'accept') {
    expect(errors).toStrictEqual({})
  } else {
    expect(Object.keys(errors)).toStrictEqual(['reason'])
    expect(errors.reason.length).toBeGreaterThan(0)
  }
})

const NARROW_RANGES = { parallelMin: 2, parallelMax: 512, queuedMin: 0, queuedMax: 7 }
const RANGE_CASES = [
  { variant: 'parallel-1-reject', field: 'overall-parallel' as FieldName, raw: 1, expected: 'maxParallel' },
  { variant: 'parallel-2-accept', field: 'overall-parallel' as FieldName, raw: 2, expected: 'accept' },
  { variant: 'queued-7-accept', field: 'overall-queued' as FieldName, raw: 7, expected: 'accept' },
  { variant: 'queued-8-reject', field: 'overall-queued' as FieldName, raw: 8, expected: 'maxQueued' },
]

it.each(RANGE_CASES)('C506 V03 $variant', ({ field, raw, expected }) => {
  const draft = blankDraft()
  setField(draft, field, set(raw))
  const errors = validateDraft(draft, NARROW_RANGES, 'ok')
  if (expected === 'accept') {
    expect(errors).toStrictEqual({})
  } else {
    expect(Object.keys(errors)).toStrictEqual([expected])
  }
})

it('C506 V03 unspecified-invalid', () => {
  const draft = blankDraft()
  draft.roles.Code.maxParallel = { specified: false, value: 999 }
  draft.maxQueued = { specified: false, value: -5 }
  expect(validateDraft(draft, RANGES, 'ok')).toStrictEqual({})
  expect(overridesFromDraft(draft)).toStrictEqual({})
})

// ---- V-4 diffDrafts: presence and value, never hidden unspecified values ----

it('C506 V04 presence-only', () => {
  const draft = blankDraft()
  const saved = blankDraft()
  saved.roles.Code.maxQueued = set(null)
  const diff = diffDrafts(draft, saved)
  expect(diff).toHaveLength(1)
  expect(diff[0].path).toBe('roles.Code.maxQueued')
  expect(diff[0].draft).toStrictEqual({ specified: false, value: null })
  expect(diff[0].saved).toStrictEqual({ specified: true, value: null })
})

it('C506 V04 value-change', () => {
  const draft = blankDraft()
  draft.maxParallel = set(4)
  draft.roles.Code.maxParallel = set(1)
  const saved = blankDraft()
  saved.maxParallel = set(4)
  saved.roles.Code.maxParallel = set(3)
  const diff = diffDrafts(draft, saved)
  expect(diff).toHaveLength(1)
  expect(diff[0]).toStrictEqual({
    path: 'roles.Code.maxParallel',
    label: 'Code max parallel',
    draft: { specified: true, value: 1 },
    saved: { specified: true, value: 3 },
  })
})

it('C506 V04 equal', () => {
  const draft = blankDraft()
  draft.maxParallel = set(4)
  draft.roles.Code.maxParallel = set(3)
  draft.roles.Review.maxQueued = set(null)
  const saved = blankDraft()
  saved.maxParallel = set(4)
  saved.roles.Code.maxParallel = set(3)
  saved.roles.Review.maxQueued = set(null)
  expect(diffDrafts(draft, saved)).toStrictEqual([])
})

it('C506 V04 hidden-unspecified', () => {
  const draft = blankDraft()
  draft.roles.Code.maxQueued = { specified: false, value: 5 }
  const saved = blankDraft()
  saved.roles.Code.maxQueued = { specified: false, value: 9 }
  expect(diffDrafts(draft, saved)).toStrictEqual([])
})
