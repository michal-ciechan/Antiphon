import type { CardStatus } from '../../api/boards'

/**
 * State identity is carried by NAME and POSITION first; colour is a redundant second encoding
 * (feature 011 §2.1). Amber/green is a known CVD-weak pair, so nothing here may be the only
 * thing telling two states apart — every node is name-labelled and every badge is worded.
 */
const STATE_COLORS: Record<CardStatus, string> = {
  Backlog: 'gray',
  InProgress: 'active',
  Review: 'warning',
  Done: 'success',
  Blocked: 'danger',
  Canceled: 'gray',
}

export function stateColor(status: CardStatus): string {
  return STATE_COLORS[status] ?? 'gray'
}

export function stateAccent(status: CardStatus): string {
  return `var(--mantine-color-${stateColor(status)}-5)`
}

/** A single-hue ramp: P0 solid, later priorities progressively faded. Legible on a dark ground. */
const PRIORITY_OPACITY = [1, 0.62, 0.34, 0.18]

export function priorityFill(priority: number): { background: string; opacity: number } {
  return {
    background: 'var(--mantine-color-danger-5)',
    opacity: PRIORITY_OPACITY[priority] ?? 0.12,
  }
}

export function priorityBadgeColor(priority: number): string {
  if (priority === 0) return 'danger'
  if (priority === 1) return 'warning'
  return 'gray'
}
