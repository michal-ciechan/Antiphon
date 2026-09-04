import type { AgentModelLevel } from '../../api/agents'
import { AGENT_MODEL_LEVEL_OPTIONS } from '../../api/agents'
import type { AgentTaskRole } from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
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

/** The three current delegatable chain kinds. Not derived from availability aliases or TUI profiles. */
export const CHAIN_KIND_OPTIONS = [
  { value: 'ClaudeCode', label: 'ClaudeCode' },
  { value: 'Codex', label: 'Codex' },
  { value: 'Grok', label: 'Grok' },
] as const satisfies ReadonlyArray<{ value: AgentKind; label: string }>

export const CHAIN_KINDS = CHAIN_KIND_OPTIONS.map((option) => option.value)

export const MAX_CHAIN_CANDIDATES = 8

export const CHAIN_MUTATION_EFFECT =
  'New complexity-routed dispatches use this cell immediately. Eligible queued and blocked-for-routing tasks are re-walked on the next dispatcher tick. Running sessions keep the model they started with.'

export const CHAIN_SAVE_SUCCESS = `Saved. ${CHAIN_MUTATION_EFFECT}`

export const CHAIN_CLEAR_SUCCESS = `Cleared. ${CHAIN_MUTATION_EFFECT}`

export const INHERITED_REPLACE_WARNING =
  'Saving writes an own rule for this cell. It replaces the inherited list as a whole; it does not append.'

export const UNSET_CELL_EDITOR_NOTE =
  'This cell is currently Unset — a -Complexity dispatch will block until a row is saved.'

export function cellEditorTitle(
  role: AgentTaskRole | null,
  complexity: TaskComplexity,
): string {
  return `Configure ${role ?? 'Any role'} / ${complexity}`
}

export function canClearOverride(
  resolvedFrom: ComplexityResolvedFrom,
  isAnyRoleRow: boolean,
): boolean {
  if (isAnyRoleRow) return resolvedFrom === 'any'
  return resolvedFrom === 'role'
}

export function fallbackResolvedFromForRoleCell(
  anyChain: ComplexityChainDto | undefined,
): ComplexityResolvedFrom {
  if (!anyChain) return 'none'
  return effectiveResolvedFrom(anyChain)
}

export function isReplacingInheritedList(
  resolvedFrom: ComplexityResolvedFrom,
  isAnyRoleRow: boolean,
): boolean {
  if (resolvedFrom === 'config') return true
  return !isAnyRoleRow && resolvedFrom === 'any'
}

export function clearOverrideCopy(input: {
  isAnyRoleRow: boolean
  role: AgentTaskRole | null
  complexity: TaskComplexity
  fallbackResolvedFrom: ComplexityResolvedFrom
}): { title: string; body: string; confirm: string } {
  const confirm = 'Confirm clear'
  if (input.isAnyRoleRow) {
    return {
      title: `Clear Any role ${input.complexity} fallback?`,
      body:
        `Clearing the Any role ${input.complexity} row removes the fallback used by every role that inherits this cell. Roles without their own rule will fall back to configuration if one exists, otherwise become Unset — a -Complexity dispatch will block until an operator sets a row.`,
      confirm,
    }
  }

  const cell = `${input.role}/${input.complexity}`
  if (input.fallbackResolvedFrom === 'none') {
    return {
      title: `Clear ${cell} override?`,
      body:
        `Clearing this ${cell} override leaves this cell Unset — a -Complexity dispatch for this role and tier will block until an operator sets a row.`,
      confirm,
    }
  }

  if (input.fallbackResolvedFrom === 'config') {
    return {
      title: `Clear ${cell} override?`,
      body:
        `Clearing this ${cell} override falls back to the configuration default. This cell currently replaces the inherited list as a whole; it does not append.`,
      confirm,
    }
  }

  return {
    title: `Clear ${cell} override?`,
    body:
      `Clearing this ${cell} override falls back to the Any role list. This cell currently replaces Any role as a whole; it does not append.`,
    confirm,
  }
}

export function candidateListError(
  candidates: Array<{ agentKind: string; modelLevel: string }>,
): string | null {
  if (candidates.length === 0) {
    return 'A chain needs 1 to 8 candidates. An empty list is a DELETE, not a PUT.'
  }
  if (candidates.length > MAX_CHAIN_CANDIDATES) {
    return `A chain may list at most ${MAX_CHAIN_CANDIDATES} candidates (got ${candidates.length}).`
  }
  const seen = new Set<string>()
  for (const candidate of candidates) {
    const key = `${candidate.agentKind}/${candidate.modelLevel}`
    if (seen.has(key)) {
      return `Duplicate candidate ${key}. A chain lists each pair once.`
    }
    seen.add(key)
  }
  return null
}

export function nextUnusedCandidate(
  existing: Array<{ agentKind: string; modelLevel: string }>,
): { agentKind: AgentKind; modelLevel: AgentModelLevel } | null {
  const used = new Set(existing.map((candidate) => `${candidate.agentKind}/${candidate.modelLevel}`))
  for (const kind of CHAIN_KINDS) {
    for (const level of AGENT_MODEL_LEVEL_OPTIONS) {
      const key = `${kind}/${level.value}`
      if (!used.has(key)) return { agentKind: kind, modelLevel: level.value }
    }
  }
  return null
}

export function lookupFieldError(
  fields: Record<string, string>,
  name: string,
): string | undefined {
  if (fields[name]) return fields[name]
  const found = Object.entries(fields).find(([key]) => key.toLowerCase() === name.toLowerCase())
  return found?.[1]
}
