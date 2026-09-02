import type { CardImportance, CardStatus, CardUrgency } from '../../api/boards'

/**
 * State identity is carried by NAME and POSITION first; colour is a redundant second encoding
 * (feature 011 §2.1). Amber/green is a known CVD-weak pair, so nothing here may be the only
 * thing telling two states apart — every node is name-labelled and every badge is worded.
 */
export const STATE_COLORS: Record<CardStatus, string> = {
  Backlog: 'gray',
  InProgress: 'active',
  Review: 'warning',
  Done: 'success',
  NeedsDecision: 'danger',
  Canceled: 'gray',
}

export function stateColor(status: CardStatus): string {
  return STATE_COLORS[status] ?? 'gray'
}

export function stateAccent(status: CardStatus): string {
  return `var(--mantine-color-${stateColor(status)}-5)`
}

export function stateLabel(status: CardStatus): string {
  return status === 'NeedsDecision' ? 'Needs decision' : status
}

/** Critical solid, then High / Normal / Low faded. Legible on a dark ground. */
const IMPORTANCE_OPACITY: Record<CardImportance, number> = {
  Critical: 1,
  High: 0.62,
  Normal: 0.34,
  Low: 0.18,
}

export function importanceFill(importance: CardImportance): { background: string; opacity: number } {
  return {
    background: 'var(--mantine-color-danger-5)',
    opacity: IMPORTANCE_OPACITY[importance] ?? 0.12,
  }
}

export function importanceBadgeColor(importance: CardImportance): string {
  if (importance === 'Critical') return 'danger'
  if (importance === 'High') return 'warning'
  return 'gray'
}

export function urgencyBadgeColor(urgency: CardUrgency): string {
  if (urgency === 'Now') return 'danger'
  if (urgency === 'Soon') return 'warning'
  return 'gray'
}

/** `now` / `soon` when the human rated it; `due 3d` when a date is what escalated it. */
export function urgencyBadgeText(
  card: { urgency: CardUrgency; effectiveUrgency: CardUrgency; dueAt: string | null },
  now: Date,
): string | null {
  if (card.effectiveUrgency === 'Normal') return null
  if (card.urgency !== 'Normal') return card.effectiveUrgency.toLowerCase()
  if (!card.dueAt) return card.effectiveUrgency.toLowerCase()
  const days = Math.ceil((new Date(card.dueAt).getTime() - now.getTime()) / 86_400_000)
  if (days <= 0) return 'due'
  return `due ${days}d`
}
