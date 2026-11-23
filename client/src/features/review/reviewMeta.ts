import type {
  DismissScope,
  FindingAction,
  FindingStatus,
  GateKind,
  NodeKind,
  NodeStatus,
  QueueItemStatus,
  ReasonCategory,
  ReviewRunStatus,
  ReviewStage,
  Severity,
  SlaState,
  WaitingOn,
} from '../../api/reviewEngine'

/** Labels and colours for the contract enums. Colours are Mantine palette names (theme.ts adds success/warning/danger/active). */

/**
 * Wraps an enum -> display record so that a value this client does not know (a newer server) reads
 * as `fallback(value)` instead of `undefined`. Every page looks these up by key, and one unexpected
 * status must render as a plain grey label, not crash the page. Key listing is unchanged.
 */
export function tolerant<K extends string, V>(known: Record<K, V>, fallback: (value: string) => V): Record<K, V> {
  return new Proxy(known, {
    get: (target, key, receiver) =>
      typeof key === 'string' && !Object.prototype.hasOwnProperty.call(target, key) ? fallback(key) : Reflect.get(target, key, receiver),
  })
}

const plain = (value: string) => ({ label: value, color: 'gray' })

export const RUN_STATUS_META: Record<ReviewRunStatus, { label: string; color: string; hint: string }> = tolerant({
  Queued: { label: 'Queued', color: 'gray', hint: 'Picked up; waiting for an executor lease' },
  Running: { label: 'Running', color: 'active', hint: 'Antiphon is working: intake through the reviewers' },
  AwaitingFixes: { label: 'Awaiting fixes', color: 'warning', hint: 'Findings posted; parked on await-fixes with no seat held' },
  AtGate: { label: 'At gate', color: 'pink', hint: 'Parked on a human gate; a queue item is open' },
  Approved: { label: 'Approved', color: 'success', hint: 'antiphon/approval is green' },
  AwaitingMerge: { label: 'Awaiting merge', color: 'teal', hint: 'Approved; merge is left to the author' },
  Merged: { label: 'Merged', color: 'violet', hint: 'Run closed by the merge' },
  Closed: { label: 'Closed', color: 'gray', hint: 'Run closed without a merge' },
  Failed: { label: 'Failed', color: 'danger', hint: "A step failed; its summary is the reason. Refresh rechecks the same head" },
}, (value) => ({ ...plain(value), hint: `Status ${value} (not known to this client)` }))

export const STAGE_META: Record<ReviewStage, { label: string; color: string }> = tolerant({
  Intake: { label: 'Intake', color: 'gray' },
  Categorise: { label: 'Categorise', color: 'cyan' },
  Select: { label: 'Select', color: 'teal' },
  Review: { label: 'Review', color: 'active' },
  Collate: { label: 'Collate', color: 'indigo' },
  Verify: { label: 'Verify', color: 'indigo' },
  Publish: { label: 'Publish', color: 'indigo' },
  AwaitingFixes: { label: 'Awaiting fixes', color: 'warning' },
  HumanGate: { label: 'Human gate', color: 'pink' },
  Approval: { label: 'Approval', color: 'success' },
  BranchMaintenance: { label: 'Branch maintenance', color: 'teal' },
  Done: { label: 'Done', color: 'violet' },
}, plain)

export const WAITING_ON_META: Record<WaitingOn, { label: string; color: string }> = tolerant({
  Author: { label: 'Author', color: 'warning' },
  Humans: { label: 'Humans', color: 'pink' },
  Bot: { label: 'Antiphon', color: 'active' },
}, plain)

export const SEVERITY_META: Record<Severity, { label: string; color: string; rank: number }> = tolerant({
  Nit: { label: 'nit', color: 'gray', rank: 0 },
  Minor: { label: 'minor', color: 'yellow', rank: 1 },
  Major: { label: 'major', color: 'orange', rank: 2 },
  Blocker: { label: 'blocker', color: 'danger', rank: 3 },
}, (value) => ({ ...plain(value), rank: -1 }))

export const FINDING_STATUS_META: Record<FindingStatus, { label: string; color: string }> = tolerant({
  Open: { label: 'open', color: 'gray' },
  WillFix: { label: 'will fix', color: 'active' },
  Fixed: { label: 'fixed', color: 'success' },
  Disputed: { label: 'disputed', color: 'danger' },
  Dismissed: { label: 'dismissed', color: 'gray' },
  Deferred: { label: 'deferred', color: 'warning' },
  Outdated: { label: 'outdated', color: 'dark' },
  Accepted: { label: 'accepted', color: 'success' },
}, plain)

/** What a human can answer. `Reply` is the free-text-only answer; `NotABlocker` is offered on blockers only. */
export const ACTION_META: Record<FindingAction, { label: string; color: string; verb: string; needsText: boolean }> = tolerant({
  WillFix: { label: 'Will fix', color: 'active', verb: 'Will fix in the next push.', needsText: false },
  Fixed: { label: 'Fixed', color: 'success', verb: 'Fixed.', needsText: false },
  Dismiss: { label: 'Dismiss', color: 'gray', verb: 'Dismissed', needsText: false },
  Dispute: { label: 'Dispute', color: 'danger', verb: 'Disputed:', needsText: true },
  Question: { label: 'Question', color: 'cyan', verb: 'Question:', needsText: true },
  Defer: { label: 'Defer', color: 'warning', verb: 'Deferred to a follow-up.', needsText: false },
  NotABlocker: { label: 'Not a blocker', color: 'orange', verb: 'Not a blocker:', needsText: true },
  Reply: { label: 'Reply', color: 'gray', verb: '', needsText: true },
}, (value) => ({ ...plain(value), verb: `${value}:`, needsText: false }))

export const COMPOSER_ACTIONS: FindingAction[] = ['WillFix', 'Fixed', 'Dismiss', 'Dispute', 'Question', 'Defer', 'NotABlocker']

export const REASON_META: Record<ReasonCategory, { label: string }> = tolerant({
  FalsePositive: { label: 'False positive' },
  ByDesign: { label: 'By design' },
  OutOfScope: { label: 'Out of scope' },
  AcceptedRisk: { label: 'Accepted risk' },
  FixLater: { label: 'Fix later' },
  Duplicate: { label: 'Duplicate' },
  StylePreference: { label: 'Style preference' },
}, (value) => ({ label: value }))

export const SCOPE_META: Record<DismissScope, { label: string; hint: string }> = tolerant({
  Instance: { label: 'This finding', hint: 'Only this finding' },
  Path: { label: 'This path', hint: "Suppress the check for this file's folder" },
  Repo: { label: 'This repo', hint: "A learn PR to the repo's .antiphon/review/" },
  Team: { label: 'Suggest to team', hint: "A proposal for the owning team's layer (needs two repos)" },
}, (value) => ({ label: value, hint: value }))

export const NODE_STATUS_META: Record<NodeStatus, { label: string; hex: string }> = tolerant({
  Pending: { label: 'pending', hex: '#5c5f66' },
  Running: { label: 'running', hex: '#3b7bf5' },
  Succeeded: { label: 'done', hex: '#33c96b' },
  Failed: { label: 'failed', hex: '#e93636' },
  Skipped: { label: 'skipped', hex: '#5c5f66' },
  Waiting: { label: 'parked', hex: '#ffa92e' },
  NotReached: { label: 'not reached', hex: '#373a40' },
  FailedContinued: { label: 'failed, continued', hex: '#e8590c' },
}, (value) => ({ label: value, hex: '#5c5f66' }))

export const NODE_KIND_META: Record<NodeKind, { label: string; color: string }> = tolerant({
  Code: { label: 'code', color: 'gray' },
  Decision: { label: 'decision', color: 'cyan' },
  Completion: { label: 'completion', color: 'teal' },
  Agent: { label: 'agent', color: 'grape' },
  FanOut: { label: 'fan-out', color: 'indigo' },
  Join: { label: 'join', color: 'indigo' },
  Gate: { label: 'gate', color: 'pink' },
  Wait: { label: 'wait', color: 'orange' },
}, plain)

export const GATE_KIND_META: Record<GateKind, { label: string; color: string }> = tolerant({
  ReviewGate: { label: 'Review gate', color: 'pink' },
  ApprovalGate: { label: 'Approval gate', color: 'success' },
  DisputedFindings: { label: 'Escalated dispute', color: 'danger' },
  LearnProposal: { label: 'Learn proposal', color: 'cyan' },
  MergeGate: { label: 'Merge gate', color: 'blue' },
}, plain)

export const QUEUE_STATUS_META: Record<QueueItemStatus, { label: string; color: string }> = tolerant({
  Unassigned: { label: 'unassigned', color: 'gray' },
  Assigned: { label: 'assigned', color: 'active' },
  InProgress: { label: 'in progress', color: 'cyan' },
  Done: { label: 'done', color: 'success' },
}, plain)

export const SLA_META: Record<SlaState, { label: string; color: string }> = tolerant({
  Ok: { label: 'within SLA', color: 'success' },
  Warn: { label: 'near SLA', color: 'warning' },
  Breached: { label: 'SLA breached', color: 'danger' },
}, plain)

/** Display colour for a resolved model name. Unknown models are grey. */
export function modelColor(model: string): string {
  const m = model.toLowerCase()
  if (m.includes('jev')) return 'cyan'
  if (m.includes('luna') || m.includes('gpt')) return 'green'
  if (m.includes('haiku')) return 'orange'
  if (m.includes('grok')) return 'gray'
  if (m.includes('sonnet')) return 'grape'
  if (m.includes('opus')) return 'violet'
  return 'gray'
}

/** "jev-1.14.0" → "jev", "gpt-5.6-luna" → "luna"; others unchanged. */
export function modelShort(model: string): string {
  const m = model.toLowerCase()
  if (m.startsWith('jev')) return 'jev'
  if (m.includes('luna')) return 'luna'
  if (m.startsWith('grok')) return 'grok'
  return model
}

export const BLAST_DIMS: { key: 'reach' | 'externalContract' | 'data' | 'rollbackAtRelease' | 'reversalAfterMonths' | 'exposure'; label: string; levels: string[] }[] = [
  { key: 'reach', label: 'Reach', levels: ['One place', 'A few', 'A module', 'Many modules', 'System-wide'] },
  { key: 'externalContract', label: 'External contract', levels: ['None', 'Unlikely', 'Possible', 'Likely', 'Certain'] },
  { key: 'data', label: 'Persistent data', levels: ['None', 'Unlikely', 'Possible', 'Likely', 'Certain'] },
  { key: 'rollbackAtRelease', label: 'Rollback at release', levels: ['Trivial', 'Easy', 'Moderate', 'Hard', 'Very hard'] },
  { key: 'reversalAfterMonths', label: 'Reversal after months', levels: ['Trivial', 'Easy', 'Moderate', 'Hard', 'One-way door'] },
  { key: 'exposure', label: 'Exposure', levels: ['Internal only', 'Low', 'Medium', 'High', 'Critical path'] },
]

/** 1..5 → bar colour. */
export function blastColor(score: number): string {
  if (score >= 5) return 'var(--mantine-color-danger-6)'
  if (score >= 4) return 'var(--mantine-color-orange-6)'
  if (score >= 3) return 'var(--mantine-color-yellow-6)'
  return 'var(--mantine-color-success-7)'
}

/** The bot-only round limit before the human-review gate opens (policy.yaml rounds; not in the v0 contract). */
export const BOT_ROUND_LIMIT = 3
