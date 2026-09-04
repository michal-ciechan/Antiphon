import type { AgentTaskRole } from '../../api/agentTasks'
import type {
  ComplexityChainDto,
  ComplexityResolvedFrom,
  TaskComplexity,
} from '../../api/complexityChains'
import type { RoutingPinDto } from '../../api/routingPins'

export const DEFAULT_COMPLEXITIES: TaskComplexity[] = ['Hard', 'Medium', 'Easy']

export const USAGE_UNKNOWN =
  'Unknown — usage monitoring is off or no provider sample is available'

export const CARD_PIN_SCOPE =
  'These pins apply only to the named card. They do not override every task in that role.'

export const COMPLEXITY_ROUTING_BOUNDARY_TITLE = 'Applies to complexity-routed tasks'

export const COMPLEXITY_ROUTING_BOUNDARY =
  'New complexity-routed dispatches read the saved cell immediately. No session restart is required. Queued complexity-chain tasks whose snapshot can no longer run, and tasks blocked for routing, are re-walked against the current matrix on the next dispatcher tick. Running sessions keep the model they started with. Saving a cell never interrupts, replaces, or migrates a mid-turn delegate. Tasks launched with an explicit kind or level, and other queued work that is not complexity-routed, keep their snapshots and are not retroactively routed by this matrix.'

export const NON_COMPLEXITY_BOUNDARY =
  'RolePolicy still resolves work that is not launched with -Complexity, including default kind and level, escalation, timeout, and WIP. This matrix does not govern every dispatch.'

export function untilLabel(value: string | null): string {
  return value ? value : 'until cleared'
}

export function effectiveResolvedFrom(chain: ComplexityChainDto): ComplexityResolvedFrom {
  if (chain.resolvedFrom) return chain.resolvedFrom
  if (chain.source === 'pin') return chain.role ? 'role' : 'any'
  if (chain.candidates.length > 0) return 'config'
  return 'none'
}

export function resolvedFromLabel(
  resolvedFrom: ComplexityResolvedFrom,
  isAnyRoleRow: boolean,
): string {
  if (resolvedFrom === 'any') return isAnyRoleRow ? 'Any role rule' : 'Inherits Any role'
  if (resolvedFrom === 'role') return 'Own rule'
  if (resolvedFrom === 'config') return 'Configuration fallback'
  return 'Unset — dispatch blocks'
}

export function complexitiesOf(
  complexities: TaskComplexity[] | string[] | undefined,
): TaskComplexity[] {
  if (!complexities || complexities.length === 0) return DEFAULT_COMPLEXITIES
  return complexities as TaskComplexity[]
}

export function anyRoleChains(
  chains: ComplexityChainDto[],
  complexities: TaskComplexity[],
): ComplexityChainDto[] {
  const nullRole = chains.filter((chain) => chain.role == null)
  return complexities.map(
    (complexity) =>
      nullRole.find((chain) => chain.complexity === complexity) ?? {
        complexity,
        candidates: [],
        provenance: null,
        source: 'config',
        reason: null,
        notAfter: null,
        updatedAt: null,
        role: null,
        resolvedFrom: 'none',
      },
  )
}

export function cellByComplexity(
  chains: ComplexityChainDto[] | undefined,
  complexity: TaskComplexity,
): ComplexityChainDto | undefined {
  return chains?.find((chain) => chain.complexity === complexity)
}

export function isStageWidePin(pin: RoutingPinDto): boolean {
  return pin.cardId == null
}

export function groupPins(pins: RoutingPinDto[]): {
  stageWide: RoutingPinDto[]
  cardSpecific: RoutingPinDto[]
} {
  const stageWide: RoutingPinDto[] = []
  const cardSpecific: RoutingPinDto[] = []
  for (const pin of pins) {
    if (isStageWidePin(pin)) stageWide.push(pin)
    else cardSpecific.push(pin)
  }
  return { stageWide, cardSpecific }
}

export function stagePinsForRole(pins: RoutingPinDto[], role: AgentTaskRole): RoutingPinDto[] {
  return pins.filter((pin) => isStageWidePin(pin) && pin.role === role)
}

export function pinTargetLabel(pin: RoutingPinDto): string {
  if (pin.agentKind && pin.modelLevel) return `${pin.agentKind}/${pin.modelLevel}`
  if (pin.agentKind) return pin.agentKind
  if (pin.modelAlias) return pin.modelAlias
  if (pin.modelLevel) return pin.modelLevel
  return 'the pinned target'
}

export function pinEffectCopy(pin: RoutingPinDto): string {
  const arrow = `${pin.role} → ${pinTargetLabel(pin)}`
  if (pin.strength === 'Required') {
    return `Required ${pin.provenance} pin: ${arrow}. This bypasses the matrix cells for ${pin.role}.`
  }
  return `Preferred ${pin.provenance} pin: ${arrow}. This prepends to the matrix candidates, then falls through to this row.`
}

export function cardPinCopy(pin: RoutingPinDto): string {
  const card = pin.cardIdentifier ?? pin.cardId ?? 'this card'
  const arrow = `${pin.role} → ${pinTargetLabel(pin)}`
  return `${pin.strength} ${pin.provenance} pin on ${card} (${arrow}). Applies only to this card, not to every ${pin.role} task.`
}

export function candidateLabel(
  candidate: ComplexityChainDto['candidates'][number],
  index: number,
): string {
  return `${index + 1}. ${candidate.agentKind}/${candidate.modelLevel} (${candidate.alias})`
}

export function candidateAvailabilityLabel(
  candidate: ComplexityChainDto['candidates'][number],
): string {
  if (candidate.availableNow) return 'available'
  return candidate.unavailableReason ?? 'unavailable'
}
