import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import type { CardDto } from '../../api/boards'

/**
 * "While you were away" — the pure half of band 3 on the mobile home (spec
 * `2026-08-17-mobile-thread-and-plan-surfacing.md` §D3/M6; CARD-0036's pull half).
 *
 * <p>Everything here is a calculation over data the caller already holds: tasks, cards, and the
 * last-seen timestamp. The localStorage access lives at the edge (`readLastSeen`/`stampLastSeen`
 * take the storage as an argument) so the delta logic is testable without a DOM shim, and so no
 * render ever computes against a clock it read itself — the caller passes `now`.</p>
 */

export const AWAY_LAST_SEEN_KEY = 'antiphon-mobile-home-last-seen'

/** First visit ever has no last-seen; the window falls back to the last 24 hours, and says so. */
export const AWAY_FALLBACK_HOURS = 24

const SETTLED = new Set<AgentTaskSummaryDto['status']>(['Succeeded', 'Failed', 'Canceled'])

export interface CardChangeEntry {
  card: CardDto
  /** What happened in the window. A card that both started and finished reads as done. */
  change: 'started' | 'done'
  /** When it happened — the sort key, newest first. */
  atUtc: string
}

export interface AwayDelta {
  /** The window's start — the previous visit, or now minus the 24h fallback. */
  sinceUtc: string
  /** True when there was no stored last-seen, so the heading says "last 24h", not "since …". */
  firstVisit: boolean
  /** Tasks that settled inside the window, newest first. Check-interpretation tasks excluded. */
  settledTasks: AgentTaskSummaryDto[]
  /** Cards whose state moved inside the window, newest first. */
  cardChanges: CardChangeEntry[]
  /**
   * Spend on the work that settled in the window — each task's OWN cost, not its subtree, so a
   * parent and child that both settle are never counted twice. Deliberately not "all spend in the
   * window": per-window spend on still-running work is not derivable from cumulative task rows,
   * and a number that pretends otherwise would be a lie with decimals.
   */
  settledSpendUsd: number
}

export function computeAwayDelta(
  tasks: AgentTaskSummaryDto[],
  cards: CardDto[],
  lastSeenUtc: string | null,
  nowUtcMs: number,
): AwayDelta {
  const firstVisit = lastSeenUtc === null
  const sinceMs = firstVisit
    ? nowUtcMs - AWAY_FALLBACK_HOURS * 60 * 60 * 1000
    : Date.parse(lastSeenUtc)
  const inWindow = (iso: string | null): boolean => {
    if (!iso) return false
    const at = Date.parse(iso)
    return at > sinceMs && at <= nowUtcMs
  }

  const settledTasks = tasks
    // Check tasks are machinery (CARD-0047): one exists per interpreted check-in, none of them is
    // anybody's delegated work, and a busy fleet would drown the band in them.
    .filter((task) => task.role !== 'Check')
    .filter((task) => SETTLED.has(task.status) && inWindow(task.completedAt))
    .sort((a, b) => Date.parse(b.completedAt!) - Date.parse(a.completedAt!))

  const cardChanges: CardChangeEntry[] = []
  for (const card of cards) {
    if (inWindow(card.completedAt)) {
      cardChanges.push({ card, change: 'done', atUtc: card.completedAt! })
    } else if (inWindow(card.startedAt)) {
      cardChanges.push({ card, change: 'started', atUtc: card.startedAt! })
    }
  }
  cardChanges.sort((a, b) => Date.parse(b.atUtc) - Date.parse(a.atUtc))

  const settledSpendUsd = settledTasks.reduce((sum, task) => sum + task.costUsd, 0)

  return {
    sinceUtc: new Date(sinceMs).toISOString(),
    firstVisit,
    settledTasks,
    cardChanges,
    settledSpendUsd,
  }
}

/**
 * One line of a settled task's report — the first sentence, capped so a row stays a row. The
 * report's own line breaks end a "sentence" too: delegates lead with the outcome line by
 * convention, and that line is exactly what the band wants.
 */
export function firstSentence(text: string | null, maxChars = 140): string | null {
  const trimmed = text?.trim()
  if (!trimmed) return null
  const firstLine = trimmed.split('\n', 1)[0].trim()
  const period = firstLine.match(/^.*?[.!?](?=\s|$)/)
  const sentence = (period ? period[0] : firstLine).trim()
  if (sentence.length <= maxChars) return sentence
  return `${sentence.slice(0, maxChars - 1).trimEnd()}…`
}

/** The stored last-seen, or null when absent or unparseable (never trust old storage). */
export function readLastSeen(storage: Pick<Storage, 'getItem'>): string | null {
  const stored = storage.getItem(AWAY_LAST_SEEN_KEY)
  if (!stored || Number.isNaN(Date.parse(stored))) return null
  return stored
}

export function stampLastSeen(storage: Pick<Storage, 'setItem'>, nowIso: string): void {
  storage.setItem(AWAY_LAST_SEEN_KEY, nowIso)
}
