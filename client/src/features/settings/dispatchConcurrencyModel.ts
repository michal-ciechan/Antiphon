import type { DispatchConcurrencyOverrides, DispatchConcurrencyRangeDto } from '../../api/dispatchConcurrency'

/**
 * Pure draft model for the dispatch concurrency editor (CARD-0506 D-3/D-4/D-7).
 *
 * The server's overrides document distinguishes three states per limit: key absent (inherit),
 * key present with `null` (specified, unbounded) and key present with a number. A `LimitDraft`
 * keeps that distinction as `specified` plus `value`, so a Revert can omit a key and "No limit"
 * can send an explicit `null` (`dispatchConcurrencyModel.test.ts` V01/V02).
 */
export interface LimitDraft {
  specified: boolean
  value: number | null
}

export interface RoleLimitDraft {
  maxParallel: LimitDraft
  maxQueued: LimitDraft
}

export interface ConcurrencyDraft {
  /** Carried through untouched; this surface does not edit the mode (D-6). */
  mode?: string
  maxParallel: LimitDraft
  maxQueued: LimitDraft
  roles: Record<string, RoleLimitDraft>
}

export interface DraftDiff {
  path: string
  label: string
  draft: LimitDraft
  saved: LimitDraft
}

/** camelCase JSON path (`maxParallel`, `roles.Code.maxQueued`, `reason`) → first message. */
export type DraftErrors = Record<string, string>

const UNSPECIFIED: LimitDraft = { specified: false, value: null }

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function decodeLimit(container: Record<string, unknown>, key: string): LimitDraft {
  if (!Object.hasOwn(container, key)) return { ...UNSPECIFIED }
  const value = container[key]
  return { specified: true, value: typeof value === 'number' ? value : null }
}

/**
 * Decode a canonical overrides document. Every supported role gets a row, plus any role the
 * document names that the client does not know, so a new server role appears without a client change.
 */
export function draftFromOverrides(overrides: unknown, supportedRoles: readonly string[]): ConcurrencyDraft {
  const doc = isRecord(overrides) ? overrides : {}
  const docRoles = isRecord(doc.roles) ? doc.roles : {}
  const roleNames = [...supportedRoles, ...Object.keys(docRoles).filter((role) => !supportedRoles.includes(role))]
  const roles: Record<string, RoleLimitDraft> = {}
  for (const role of roleNames) {
    const row = isRecord(docRoles[role]) ? docRoles[role] : {}
    roles[role] = { maxParallel: decodeLimit(row, 'maxParallel'), maxQueued: decodeLimit(row, 'maxQueued') }
  }
  const draft: ConcurrencyDraft = {
    maxParallel: decodeLimit(doc, 'maxParallel'),
    maxQueued: decodeLimit(doc, 'maxQueued'),
    roles,
  }
  if (typeof doc.mode === 'string') draft.mode = doc.mode
  return draft
}

/**
 * Encode a draft as the PUT `overrides` body: unspecified fields are omitted, a role with nothing
 * specified is omitted, a specified `null` is emitted, `mode` is carried, `schemaVersion` is never
 * written (the server adds it), and a fully unspecified draft is `{}`, which clears every override.
 */
export function overridesFromDraft(draft: ConcurrencyDraft): DispatchConcurrencyOverrides {
  const out: DispatchConcurrencyOverrides = {}
  if (draft.mode !== undefined) out.mode = draft.mode
  if (draft.maxParallel.specified) out.maxParallel = draft.maxParallel.value
  if (draft.maxQueued.specified) out.maxQueued = draft.maxQueued.value
  const roles: Record<string, Record<string, number | null>> = {}
  for (const [role, row] of Object.entries(draft.roles)) {
    const encoded: Record<string, number | null> = {}
    if (row.maxParallel.specified) encoded.maxParallel = row.maxParallel.value
    if (row.maxQueued.specified) encoded.maxQueued = row.maxQueued.value
    if (Object.keys(encoded).length > 0) roles[role] = encoded
  }
  if (Object.keys(roles).length > 0) out.roles = roles
  return out
}

const REASON_MAX_LENGTH = 400

function validateLimit(errors: DraftErrors, path: string, limit: LimitDraft, min: number, max: number, allowNull: boolean) {
  if (!limit.specified) return
  const value: unknown = limit.value
  if (value === null) {
    if (!allowNull) errors[path] = `${path} must be an integer from ${min} to ${max}.`
    return
  }
  if (typeof value !== 'number' || !Number.isInteger(value) || value < min || value > max) {
    errors[path] = `${path} must be an integer from ${min} to ${max}.`
  }
}

/**
 * Client-side mirror of the server rules (D-4): specified limits must be integers inside the
 * supplied `ranges`; overall max parallel may not be null, the other three kinds may; the trimmed
 * reason is 1–400 characters. Unspecified values are not validated because they are not sent.
 */
export function validateDraft(draft: ConcurrencyDraft, ranges: DispatchConcurrencyRangeDto, reason: string): DraftErrors {
  const errors: DraftErrors = {}
  validateLimit(errors, 'maxParallel', draft.maxParallel, ranges.parallelMin, ranges.parallelMax, false)
  validateLimit(errors, 'maxQueued', draft.maxQueued, ranges.queuedMin, ranges.queuedMax, true)
  for (const [role, row] of Object.entries(draft.roles)) {
    validateLimit(errors, `roles.${role}.maxParallel`, row.maxParallel, ranges.parallelMin, ranges.parallelMax, true)
    validateLimit(errors, `roles.${role}.maxQueued`, row.maxQueued, ranges.queuedMin, ranges.queuedMax, true)
  }
  const trimmed = reason.trim()
  if (trimmed.length < 1 || trimmed.length > REASON_MAX_LENGTH) {
    errors.reason = `reason must be 1 to ${REASON_MAX_LENGTH} characters after trimming.`
  }
  return errors
}

function sameLimit(a: LimitDraft, b: LimitDraft): boolean {
  if (a.specified !== b.specified) return false
  // A hidden value behind an unspecified switch is not part of the document.
  return !a.specified || a.value === b.value
}

function pushDiff(diffs: DraftDiff[], path: string, label: string, draft: LimitDraft, saved: LimitDraft) {
  if (!sameLimit(draft, saved)) diffs.push({ path, label, draft, saved })
}

/**
 * Fields whose specified bit or specified value differ between the operator's draft and the saved
 * document, for the conflict banner (D-7). Unspecified values never produce a diff.
 */
export function diffDrafts(draft: ConcurrencyDraft, saved: ConcurrencyDraft): DraftDiff[] {
  const diffs: DraftDiff[] = []
  pushDiff(diffs, 'maxParallel', 'Overall max parallel', draft.maxParallel, saved.maxParallel)
  pushDiff(diffs, 'maxQueued', 'Overall max queued', draft.maxQueued, saved.maxQueued)
  const roles = [...Object.keys(draft.roles), ...Object.keys(saved.roles).filter((role) => !(role in draft.roles))]
  for (const role of roles) {
    const left = draft.roles[role] ?? { maxParallel: UNSPECIFIED, maxQueued: UNSPECIFIED }
    const right = saved.roles[role] ?? { maxParallel: UNSPECIFIED, maxQueued: UNSPECIFIED }
    pushDiff(diffs, `roles.${role}.maxParallel`, `${role} max parallel`, left.maxParallel, right.maxParallel)
    pushDiff(diffs, `roles.${role}.maxQueued`, `${role} max queued`, left.maxQueued, right.maxQueued)
  }
  return diffs
}

/** True when the document has no key other than `schemaVersion` (`{}` and `{schemaVersion:1}`). */
export function isCleared(overrides: unknown): boolean {
  if (!isRecord(overrides)) return true
  return Object.keys(overrides).every((key) => key === 'schemaVersion')
}

const SOURCE_LABELS: Record<string, string> = {
  project: 'Project override',
  global: 'Global override',
  default: 'Built-in default',
}

/** Source badge text (D-5); an unknown source string is shown as itself rather than guessed. */
export function sourceLabel(source: string): string {
  return SOURCE_LABELS[source] ?? source
}
