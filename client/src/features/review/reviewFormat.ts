import type { NodeExecutionDto, ReviewSubjectDto } from '../../api/reviewEngine'

/**
 * The one cost formatter (design §15, D-20). Under $1: rounded cents ("5¢", "<1¢" for a non-zero
 * amount under half a cent). $1 or more: rounded whole dollars ("$4"). No decimals.
 */
export function fmtCost(usd: number): string {
  const sign = usd < 0 ? '−' : ''
  const v = Math.abs(usd)
  if (v === 0) return '0¢'
  const cents = Math.round(v * 100)
  if (cents === 0) return `${sign}<1¢`
  if (cents < 100) return `${sign}${cents}¢`
  return `${sign}$${Math.round(v).toLocaleString('en-US')}`
}

/** The exact amount, for tooltips. */
export function fmtCostExact(usd: number): string {
  return `$${usd.toFixed(usd < 1 ? 4 : 2)}`
}

export function fmtTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`
  if (n >= 1000) return `${(n / 1000).toFixed(n >= 100_000 ? 0 : 1)}k`
  return String(n)
}

/** "12s ago", "5m ago", "3h ago", "yesterday", "4d ago". */
export function fmtAgo(iso: string | null, nowMs: number): string {
  if (!iso) return '—'
  const s = Math.max(0, Math.round((nowMs - Date.parse(iso)) / 1000))
  if (s < 60) return `${s}s ago`
  const m = Math.round(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.round(m / 60)
  if (h < 24) return `${h}h ago`
  const d = Math.round(h / 24)
  return d === 1 ? 'yesterday' : `${d}d ago`
}

/** Working minutes as "45 wm" or "3.6 wh". */
export function fmtWorkingMinutes(minutes: number): string {
  if (minutes < 60) return `${Math.round(minutes)} wm`
  const h = minutes / 60
  return `${h.toFixed(h < 10 ? 1 : 0)} wh`
}

export function fmtDuration(startedAt: string | null, completedAt: string | null, nowMs: number): string {
  if (!startedAt) return '—'
  const ms = (completedAt ? Date.parse(completedAt) : nowMs) - Date.parse(startedAt)
  const s = Math.max(0, Math.round(ms / 1000))
  if (s < 60) return `${s}s`
  const m = Math.round(s / 60)
  if (m < 120) return `${m}m`
  return `${(m / 60).toFixed(1)}h`
}

/** "payments-api#612" — the repo without its owner, as the tables show it. */
export function prRef(subject: Pick<ReviewSubjectDto, 'repo' | 'prNumber'>): string {
  const name = subject.repo.split('/').pop() ?? subject.repo
  return subject.prNumber === null ? name : `${name}#${subject.prNumber}`
}

export function shortSha(sha: string): string {
  return sha.slice(0, 7)
}

/** Actual (metered HTTP) versus API-equivalent estimate (seats), from the executions' transports. */
export function costSplit(nodes: NodeExecutionDto[]): { actual: number; estimate: number } {
  let actual = 0
  let estimate = 0
  for (const n of nodes) {
    if (n.transport === 'Http') actual += n.costUsd
    else estimate += n.costUsd
  }
  return { actual, estimate }
}

export function personLabel(login: string | null, displayName?: string | null): string {
  if (!login) return 'Unassigned'
  return displayName ?? `@${login}`
}
