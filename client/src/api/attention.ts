import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AlertSeverity } from './agents'

/**
 * The "what needs a human" projection (CARD-0035): one fleet-global list of work that is stuck,
 * every row naming the condition that put it there and the evidence for it.
 *
 * <p>The property that makes it worth opening is a NON-membership rule, and it is enforced on the
 * server: a session that is mid-turn with a live transcript is never listed, however far past its
 * estimate it has run. Genuinely slow is not stuck. Nothing in this file may widen that — a client
 * that synthesised rows the server did not send would be re-deriving stuckness from a screen, which
 * is the exact thing this projection exists to stop.</p>
 */

/** The named conditions. Mirrors the server enum; the server serialises these as strings. */
export type AttentionKind =
  /** The delegate asked a question. Only a human answer moves this. */
  | 'BlockedQuestion'
  /** A queued message spent its delivery attempts and parked — nothing will retry it (CARD-0055). */
  | 'ParkedMessage'
  /** An open task whose session row is missing, Stopped, Failed, or already ended. */
  | 'DeadSession'
  /** Dispatched, past the grace, and the session has written not one transcript entry. */
  | 'NeverStarted'
  /**
   * Dispatched, past the delivery grace, the brief is still Pending, and the session is mid-turn.
   * The watchdog deferred this rather than fail-and-kill (CARD-0117).
   */
  | 'BriefUndelivered'
  /** The delegate reported and the report could not be tied back to the task. */
  | 'UncorrelatedReport'
  /** Well past the estimate AND idle at the prompt — "finished but never reported". */
  | 'PastExpectedIdle'
  /** The check budget ran out with the task still open. Nobody is watching it any more. */
  | 'ChecksSpent'
  /** The runner and the database disagree about a session (leaked, or unclaimed). */
  | 'SessionDisagreement'
  /** Error-or-worse incidents in the last 24h, collapsed per agent and kind. */
  | 'RecentCriticalIncident'
  /** Context, not an alarm: tasks that failed in the last 24h. */
  | 'RecentFailure'
  /**
   * Closing on a deadline that will FAIL the task: the role's hard wall-clock ceiling, or - while
   * the session is mid-turn - the phase-aware model-wait / local-execution deadline (CARD-0020).
   * Listed from 80% of the limit, so a reply, a check or a cancel are all still open.
   */
  | 'Overdue'
  /**
   * Working, rows still landing, none of them novel (CARD-0153). Detection only — steer it,
   * cancel it, or open the session; nothing automatic kills it.
   */
  | 'ProgressStalled'
  /** A card is parked until a human makes and records a decision. */
  | 'CardNeedsDecision'
  /**
   * A card has sat In Progress past the stale threshold with no open bound task, no live session
   * and no owning card session (CARD-0040). Detection only - nothing un-stalls it automatically.
   */
  | 'CardStalled'
  /**
   * Failed before dispatch, and nothing shows the caller has heard (CARD-0231). Counted, unlike
   * RecentFailure; disappears the moment a note is Sent/Dropped, a status poll, or a drawer read.
   */
  | 'FailureUnacknowledged'
  /**
   * The orchestrator did a cold source-read run with no dispatch (CARD-0247). Detection only —
   * Process group, Warning, never counted as Broken.
   */
  | 'OrchestratorInvestigation'
  /**
   * An inbound channel message the Antiphon consumer group never consumed (CARD-0245).
   * Critical. Detection only — the gateway already acknowledged the chat if it could.
   */
  | 'InboundUnconsumed'
  /**
   * A caller-session Delegation or Check note still Pending past the delivery grace
   * (CARD-0267). Detection only — silence is not evidence the delegate is still running.
   */
  | 'CallerNoteUndelivered'
  /**
   * A current cardless start is still idle after grace because Details was not sent as a
   * prompt (CARD-0287). Detection only — Details stays standing metadata; nothing auto-sends
   * it. Only a fresh interactive launch qualifies; a resume or Herdr-attached empty shell
   * does not.
   */
  | 'CardlessDetailsNoPrompt'

/** Verbs the server already serves. The row names them so the client never infers them from kind. */
export type AttentionAction =
  | 'Reply'
  | 'Retry'
  | 'Cancel'
  | 'Escalate'
  | 'SendNow'
  | 'CancelMessage'
  | 'OpenDrawer'
  | 'KillSession'
  | 'OpenAgent'
  | 'OpenCard'

export interface AttentionItemDto {
  kind: AttentionKind
  /** Critical = needs you now, Error = broken, Warning = suspect. The row's rank AND its group. */
  severity: AlertSeverity
  taskId: string | null
  sessionId: string | null
  agentId: string | null
  messageId: string | null
  cardId?: string | null
  boardId?: string | null
  title: string
  /** One server-computed line naming the condition in this row's own numbers. */
  headline: string
  /** The derivation in text: incident message, failure reason, parked body, check digest tail. */
  evidence: string
  /** When the condition began, best-effort. The ordering key inside a severity band. */
  sinceUtc: string | null
  /** Rolled-up spend for the task and everything under it, when the row is task-scoped. */
  subtreeCostUsd: number | null
  actions: AttentionAction[]
}

export interface AttentionDto {
  generatedAt: string
  /**
   * Whether the session runner answered this sweep. False means `SessionDisagreement` is ABSENT,
   * not empty — "nobody asked" is a weaker claim than "nothing disagrees", and the panel says so
   * rather than letting a down runner read as a clean bill of health.
   */
  runnerConsulted: boolean
  items: AttentionItemDto[]
}

export interface AttentionSummaryDto {
  open: number
  decisions: number
  generatedAt: string
}

export const attentionKeys = {
  all: ['attention'] as const,
  summary: ['attention', 'summary'] as const,
}

/**
 * The 15s poll matches `useAgentTasks`, and exists for the same reason: SignalR's `AgentTaskChanged`
 * already invalidates this key (`useSignalRInvalidation`), so the interval only covers a dropped
 * connection. CARD-0035 §D5 rules out a new SignalR event for this view — the conditions are
 * derived from state that already broadcasts.
 */
export function useAttention(enabled = true) {
  return useQuery({
    queryKey: attentionKeys.all,
    queryFn: () => apiGet<AttentionDto>('/attention'),
    refetchInterval: 15_000,
    enabled,
  })
}

/** Counts-only variant for the global nav badge; attention panels still use the full projection. */
export function useAttentionSummary() {
  return useQuery({
    queryKey: attentionKeys.summary,
    queryFn: () => apiGet<AttentionSummaryDto>('/attention/summary'),
    refetchInterval: 15_000,
  })
}
