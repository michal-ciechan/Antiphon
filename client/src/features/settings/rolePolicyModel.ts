import type { AgentModelLevel } from '../../api/agents'
import type { AgentKind } from '../../api/boards'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinCandidateDto } from '../../api/routingPins'

/** CARD-0097 F-1: a stage pin whose head has no candidate keeps `source: pin` and withholds the target. */
export const NO_PIN_CANDIDATE = 'no candidate (agent-only or forbid-only; see the pin)'

function targetLabel(kind: AgentKind | 'unknown', level: AgentModelLevel, alias: string): string {
  return `${kind}/${level} (${alias})`
}

/**
 * The RolePolicy default; a null kind is ClaudeCode (C097 V05). A null level is shown as unknown
 * (C097 V05 configured-level-null). An empty alias beside a null kind is the server's answer for an
 * undefined kind (C097 V05 configured-kind-undefined).
 */
export function defaultLabel(row: RolePolicyRowDto): string {
  if (!row.configured) {
    return row.level ? `not configured — DefaultLevel ${row.level} (${row.alias})` : 'not configured — DefaultLevel unknown'
  }
  if (!row.level) return 'unknown'
  return targetLabel(row.kind ?? (row.alias ? 'ClaudeCode' : 'unknown'), row.level, row.alias)
}

/**
 * The next create's target as the server composed it: `default →` for `rolePolicy`, the pin's
 * strength and provenance for `pin`, and `unknown` for a source F-1 does not list (C097 V06).
 */
export function effectiveLabel(row: RolePolicyRowDto): string {
  const { source, agentKind, modelLevel, alias, strength } = row.effective
  const target = agentKind && modelLevel && alias ? targetLabel(agentKind, modelLevel, alias) : null
  if (source === 'rolePolicy') return target ? `default → ${target}` : 'unknown'
  if (source !== 'pin') return 'unknown'
  const provenance = row.stagePin?.provenance
  if (!strength || !provenance) return 'unknown'
  const prefix = `${strength} ${provenance} pin`
  if (target) return `${prefix} → ${target}`
  if (!agentKind && !modelLevel && !alias) return `${prefix} → ${NO_PIN_CANDIDATE}`
  return 'unknown'
}

/** The manual ladder unless the role has an escalation timer (C097 V24). */
export function escalateLabel(row: RolePolicyRowDto): string {
  if (row.escalateTo == null) return '—'
  if (row.escalateAfterMinutes == null) return `${row.escalateTo} (manual)`
  return `${row.escalateTo} after ${row.escalateAfterMinutes} min quiet`
}

/** A ceiling of zero or less turns the timeout off (C097 V24). */
export function timeoutLabel(row: RolePolicyRowDto): string {
  if (row.timeoutMinutes <= 0) return 'off'
  return `${row.timeoutMinutes} min`
}

export function pinCandidateLabel(candidate: RoutingPinCandidateDto, index: number): string {
  return `${index + 1}. ${candidate.agentKind ?? '—'}/${candidate.modelLevel ?? '—'} (${candidate.alias ?? '—'})`
}

/** The server's per-candidate verdict (C097 V25). */
export function pinCandidateAvailability(candidate: RoutingPinCandidateDto): string {
  if (candidate.availableNow) return 'available'
  return candidate.unavailableReason ?? 'unavailable'
}
