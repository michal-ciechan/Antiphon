import type { IconType } from 'react-icons'
import {
  TbAlertTriangle,
  TbClockExclamation,
  TbClockPause,
  TbEyeOff,
  TbHelpCircle,
  TbHourglassHigh,
  TbLink,
  TbMailExclamation,
  TbMessage,
  TbPlayerPlayFilled,
  TbPlugConnectedX,
  TbRepeat,
  TbSearch,
  TbSkull,
  TbUserOff,
  TbX,
} from 'react-icons/tb'
import type { AlertSeverity } from '../../api/agents'
import type { AttentionItemDto, AttentionKind } from '../../api/attention'

/**
 * Kind → how the row reads, and severity → which group it lands in. Pure and total: every
 * `AttentionKind` has an entry, enforced by the `Record` type and pinned by a test, because a kind
 * the server adds and the client cannot draw would render as a blank row on the one screen whose
 * whole job is to be legible in a hurry.
 */
export interface AttentionVisual {
  /** Two or three words. The badge, not the sentence — the headline carries the sentence. */
  label: string
  /** Semantic palette only (`taskVisuals.STATUS_COLOR` conventions); never the violet tier axis. */
  color: string
  icon: IconType
  /** What the condition means, for the badge tooltip. One line, plain. */
  hint: string
}

export const ATTENTION_VISUALS: Record<AttentionKind, AttentionVisual> = {
  BlockedQuestion: {
    label: 'Blocked',
    color: 'warning',
    icon: TbHelpCircle,
    hint: 'The delegate asked a question. Only an answer moves it — a retry throws its work away.',
  },
  ParkedMessage: {
    label: 'Parked message',
    color: 'danger',
    icon: TbMailExclamation,
    hint: 'A queued message spent its delivery attempts. Nothing automatic will type it again.',
  },
  DeadSession: {
    label: 'Dead session',
    color: 'danger',
    icon: TbSkull,
    hint: 'The task is still open but its session is gone, stopped or failed.',
  },
  NeverStarted: {
    label: 'Never started',
    color: 'danger',
    icon: TbPlayerPlayFilled,
    hint: 'Dispatched, but the session has never written a single transcript entry.',
  },
  BriefUndelivered: {
    label: 'Brief waiting',
    color: 'warning',
    icon: TbHourglassHigh,
    hint: 'The brief is still queued while the session works on other work. The watchdog will not kill it.',
  },
  UncorrelatedReport: {
    label: 'Report lost',
    color: 'danger',
    icon: TbLink,
    hint: 'The delegate reported and the report could not be tied back to this task.',
  },
  PastExpectedIdle: {
    label: 'Idle past estimate',
    color: 'warning',
    icon: TbClockExclamation,
    hint: 'Well past its estimate AND idle at the prompt — usually finished but never reported.',
  },
  ChecksSpent: {
    label: 'Unwatched',
    color: 'warning',
    icon: TbEyeOff,
    hint: 'The check budget ran out with the task still open. Nothing is watching it now.',
  },
  SessionDisagreement: {
    label: 'Session disagreement',
    color: 'danger',
    icon: TbPlugConnectedX,
    hint: 'The runner and the database disagree about a session. The live process may be right.',
  },
  RecentCriticalIncident: {
    label: 'Incidents',
    color: 'danger',
    icon: TbAlertTriangle,
    hint: 'Error-or-worse incidents in the last 24h, collapsed per agent and kind.',
  },
  RecentFailure: {
    label: 'Failed today',
    color: 'gray',
    icon: TbX,
    hint: 'Settled as failed in the last 24h. Context, not an alarm.',
  },
  Overdue: {
    label: 'Overdue',
    color: 'warning',
    icon: TbHourglassHigh,
    hint: 'Closing on the deadline that will fail it - the role ceiling, or its current phase.',
  },
  ProgressStalled: {
    label: 'Stalled',
    color: 'warning',
    icon: TbRepeat,
    hint: 'Working, but nothing novel has landed for a while. Detection only — nothing is killed.',
  },
  CardNeedsDecision: {
    label: 'Needs decision',
    color: 'danger',
    icon: TbHelpCircle,
    hint: 'A card is parked on a decision only a person can make. Move it when you have decided.',
  },
  CardStalled: {
    label: 'Stalled card',
    color: 'warning',
    icon: TbClockPause,
    hint: 'In Progress with no open task and no live session. Move it, or start something.',
  },
  FailureUnacknowledged: {
    label: 'Failure unheard',
    color: 'danger',
    icon: TbAlertTriangle,
    hint: 'Failed before dispatch, and the completion note has not reached the caller.',
  },
  OrchestratorInvestigation: {
    label: 'Investigation',
    color: 'warning',
    icon: TbSearch,
    hint: 'The orchestrator read source files itself instead of dispatching. Detection only.',
  },
  InboundUnconsumed: {
    label: 'Inbound waiting',
    color: 'danger',
    icon: TbMailExclamation,
    hint: 'A chat message was received but the service did not consume it. Detection only.',
  },
  CallerNoteUndelivered: {
    label: 'Caller note waiting',
    color: 'warning',
    icon: TbHourglassHigh,
    hint: 'The caller has not received the note. Silence is not evidence the delegate is still running.',
  },
  CardlessDetailsNoPrompt: {
    label: 'Details needs a prompt',
    color: 'warning',
    icon: TbMessage,
    hint: 'A cardless start is still idle because Details was not sent as a prompt. Send a message; nothing sends Details automatically.',
  },
  QueuedInputStuck: {
    label: 'Input swallowed',
    color: 'warning',
    icon: TbMailExclamation,
    hint: 'Input was accepted by the TUI and never became a prompt — usually a blocking modal. Detection only; Esc or restart clears it.',
  },
  AgentOutlivedTask: {
    label: 'Outlived its task',
    color: 'warning',
    icon: TbUserOff,
    hint: 'A standing agent finished (or lost) its one job and nothing retired it. Stop it, or give it work — nothing automatic will.',
  },
  ReportUnsettled: {
    label: 'Report unsettled',
    color: 'warning',
    icon: TbClockExclamation,
    hint: 'The delegate already wrote the closing report line; the task row never left Dispatched. Settlement should catch this up; do not kill the session to unblock it.',
  },
  UnmarkedWaiting: {
    label: 'Waiting unmarked',
    color: 'warning',
    icon: TbHourglassHigh,
    hint: 'The delegate ended a turn without done/blocked/failed and is idle. It will Block if it stays that way; do not read Herdr done as finished.',
  },
  ModelAvailabilityHold: {
    label: 'Model held',
    color: 'danger',
    icon: TbClockPause,
    hint: 'This model is paused — queued work skips it and new creates 409 model_disabled. Clear the hold, wait until it expires, or pick an available alias.',
  },
}

export type AttentionGroupKey = 'now' | 'broken' | 'suspect' | 'failures'

export interface AttentionGroup {
  key: AttentionGroupKey
  title: string
  /** What the whole group means, so a reader knows why these rows sit together. */
  hint: string
  /** Collapsed by default: context you go looking for, not something demanding an answer. */
  collapsed: boolean
}

/**
 * Four groups, in the order a human triages. Severity IS the rank — the server orders by it and then
 * by age — so the grouping only names bands that already exist rather than inventing a second scale.
 * `RecentFailure` is pulled out of Warning into its own collapsed group: a settled failure is
 * history, and history under a heading that says "suspect" would read as an open problem.
 */
export const ATTENTION_GROUPS: AttentionGroup[] = [
  {
    key: 'now',
    title: 'Needs you now',
    hint: 'Nothing moves these but a person',
    collapsed: false,
  },
  {
    key: 'broken',
    title: 'Broken',
    hint: 'Something failed and has not been picked up',
    collapsed: false,
  },
  {
    key: 'suspect',
    title: 'Suspect',
    hint: 'Worth a look — the right answer is often "leave it"',
    collapsed: false,
  },
  {
    key: 'failures',
    title: 'Recent failures',
    hint: 'Settled in the last 24h — context, not an alarm',
    collapsed: true,
  },
]

export function groupOf(item: AttentionItemDto): AttentionGroupKey {
  if (item.kind === 'RecentFailure') return 'failures'
  return severityGroup(item.severity)
}

/**
 * The Home glance's three chips. Same four groups as the panel — `failures` is history, so it
 * stays off the landing screen the way `NeedsAttentionBadge` already excluded `RecentFailure`.
 * New kinds fall in through `groupOf`; this is not a second taxonomy.
 */
export type HomeAttentionBucket = 'blocked' | 'broken' | 'review' | 'hidden'

export function homeBucketOf(item: AttentionItemDto): HomeAttentionBucket {
  const group = groupOf(item)
  switch (group) {
    case 'now':
      return 'blocked'
    case 'broken':
      return 'broken'
    case 'suspect':
      return 'review'
    case 'failures':
      return 'hidden'
  }
}

export function homeBucketCounts(items: AttentionItemDto[]): {
  blocked: number
  broken: number
  review: number
} {
  const counts = { blocked: 0, broken: 0, review: 0 }
  for (const item of items) {
    const bucket = homeBucketOf(item)
    if (bucket === 'hidden') continue
    counts[bucket] += 1
  }
  return counts
}

function severityGroup(severity: AlertSeverity): AttentionGroupKey {
  if (severity === 'Critical') return 'now'
  if (severity === 'Error') return 'broken'
  return 'suspect'
}

export const SEVERITY_COLOR: Record<AlertSeverity, string> = {
  Critical: 'danger',
  Error: 'danger',
  Warning: 'warning',
  Info: 'gray',
}

/**
 * Where clicking a row goes. A stuck row's natural click-through is the thing that explains it, and
 * for task-scoped rows that is the drawer that already exists on the sibling tab — the reason this
 * view lives on the Orchestrator page at all.
 */
export function targetOf(item: AttentionItemDto): string | null {
  if (item.cardId && item.boardId) return `/boards/${item.boardId}?card=${item.cardId}`
  if (item.taskId) return `/orchestrator?tab=delegations&task=${item.taskId}`
  // `?agent=` is how AgentsPage takes a selection — the incident drawer opens on the agent it names.
  if (item.agentId) return `/agents?agent=${item.agentId}`
  return null
}

/**
 * How long the condition has been true. Lives here rather than in the component for the same reason
 * `elapsedSeconds` does in `taskVisuals` — a clock read during render is impure, and the injectable
 * `now` is what lets a test assert an age instead of a moving target.
 */
export function ageSeconds(item: AttentionItemDto, now: number = Date.now()): number | null {
  if (!item.sinceUtc) return null
  return Math.max(0, (now - Date.parse(item.sinceUtc)) / 1000)
}

/**
 * Stable identity for a row across polls. The server sends no id, so the SUBJECT is the identity —
 * and `sinceUtc` is part of it, not decoration: `RecentCriticalIncident` is grouped per agent AND
 * per incident kind, and the DTO carries that kind only inside the prose headline, so two rows for
 * one agent can otherwise match on every id field and collide. `sinceUtc` is the oldest incident in
 * each group, which differs, and it is stable across polls for every other kind too — unlike the
 * headline, which counts elapsed time and would remount the row every fifteen seconds.
 */
export function keyOf(item: AttentionItemDto): string {
  return [item.kind, item.cardId, item.taskId, item.sessionId, item.messageId, item.agentId, item.modelKind, item.modelAlias, item.sinceUtc].join('|')
}
