export type CardImportance = 'Low' | 'Normal' | 'High' | 'Critical'
export type CardUrgency = 'Normal' | 'Soon' | 'Now'
export type CardQuadrant = 'DoFirst' | 'Schedule' | 'Clear' | 'Someday'

export const IMPORTANCE_VALUES: readonly CardImportance[] = ['Low', 'Normal', 'High', 'Critical']
export const URGENCY_VALUES: readonly CardUrgency[] = ['Normal', 'Soon', 'Now']
export const QUADRANT_ORDER: readonly CardQuadrant[] = ['DoFirst', 'Schedule', 'Clear', 'Someday']

const IMPORTANCE_WEIGHT: Record<CardImportance, number> = {
  Low: 0,
  Normal: 1,
  High: 2,
  Critical: 3,
}

const URGENCY_WEIGHT: Record<CardUrgency, number> = {
  Normal: 0,
  Soon: 1,
  Now: 2,
}

export const NOW_DUE_MS = 3 * 86_400_000
export const SOON_DUE_MS = 14 * 86_400_000

export function effectiveUrgency(
  stored: CardUrgency,
  dueAt: string | null | undefined,
  now: Date,
): CardUrgency {
  const implied = impliedByDueAt(dueAt, now)
  return URGENCY_WEIGHT[stored] >= URGENCY_WEIGHT[implied] ? stored : implied
}

export function quadrant(importance: CardImportance, effective: CardUrgency): CardQuadrant {
  const important = importance === 'High' || importance === 'Critical'
  const urgent = effective !== 'Normal'
  if (important && urgent) return 'DoFirst'
  if (important) return 'Schedule'
  if (urgent) return 'Clear'
  return 'Someday'
}

export function rank(
  importance: CardImportance,
  urgency: CardUrgency,
  dueAt: string | null | undefined,
  now: Date,
): number {
  const effective = effectiveUrgency(urgency, dueAt, now)
  return 13 - (3 * IMPORTANCE_WEIGHT[importance] + 2 * URGENCY_WEIGHT[effective])
}

function impliedByDueAt(dueAt: string | null | undefined, now: Date): CardUrgency {
  if (!dueAt) return 'Normal'
  const remaining = new Date(dueAt).getTime() - now.getTime()
  if (Number.isNaN(remaining)) return 'Normal'
  if (remaining <= NOW_DUE_MS) return 'Now'
  if (remaining <= SOON_DUE_MS) return 'Soon'
  return 'Normal'
}
