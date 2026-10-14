import type { AgentModelLevel } from '../../api/agents'
import type { AgentTaskKind } from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
import type { ComplexityChainListDto, TaskComplexity } from '../../api/complexityChains'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinProvenance, RoutingPinStrength } from '../../api/routingPins'
import { effectiveLabel } from './rolePolicyModel'

/**
 * CARD-1250. Where the next create of a role, with no card and no explicit kind or agent, takes
 * its pair: `explicit` level, the configured default-tier row (`matrix`, or `pin` when a Required
 * stage pin's candidates are walked), the stage pin's standing `agent`, every candidate held
 * (`exhausted`, the create is Blocked) or the `rolePolicy` projection.
 */
export type NextCreateSource = 'explicit' | 'matrix' | 'pin' | 'rolePolicy' | 'agent' | 'exhausted'

export interface NextCreatePreview {
  source: NextCreateSource
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  alias: string | null
  /** The default tier a Worker create is stamped with; null when no row is walked. */
  complexity: TaskComplexity | null
  /** Aliases of the held candidates the walk passes over, in walk order. */
  skipped: string[]
  /** The Orchestrator MinOrchestratorLevel floor raised the level. */
  floored: boolean
  /** The pin that supplied the walked list, for the label. */
  pin: { strength: RoutingPinStrength; provenance: RoutingPinProvenance } | null
  /** The legacy row whose `effective` projection this preview kept unchanged. */
  projected: RolePolicyRowDto | null
  note: string | null
}

export interface NextCreateContext {
  kind: AgentTaskKind
  explicitLevel: AgentModelLevel | null
  /** The role's GET /api/role-policy row. */
  row: RolePolicyRowDto | undefined
  /** GET /api/complexity-chains?role= for the same role; undefined while loading or after a failure. */
  cells: ComplexityChainListDto | undefined
}

interface Candidate {
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  alias: string | null
  availableNow: boolean
}

const AGENT_ONLY_PIN = 'stage pin binds a standing agent; the create runs on that agent'

function blank(source: NextCreateSource): NextCreatePreview {
  return {
    source,
    agentKind: null,
    modelLevel: null,
    alias: null,
    complexity: null,
    skipped: [],
    floored: false,
    pin: null,
    projected: null,
    note: null,
  }
}

/** The walk: the first available candidate, or Blocked on the head (AgentTaskService OD-6). */
function walk(candidates: Candidate[], source: 'matrix' | 'pin'): NextCreatePreview | null {
  if (candidates.length === 0) return null
  const index = candidates.findIndex((c) => c.availableNow)
  const chosen = index < 0 ? candidates[0] : candidates[index]
  return {
    ...blank(index < 0 ? 'exhausted' : source),
    agentKind: chosen.agentKind,
    modelLevel: chosen.modelLevel,
    alias: chosen.alias,
    skipped: index < 0 ? [] : candidates.slice(0, index).map((c) => c.alias ?? '—'),
  }
}

function pinRef(strength: RoutingPinStrength, provenance: RoutingPinProvenance) {
  return { strength, provenance }
}

/** Worker only: the configured default-tier row, or null when the create would not walk it. */
function fromDefaultTier(cells: ComplexityChainListDto | undefined): NextCreatePreview | null | undefined {
  const tier = cells?.defaultComplexity
  const chain = cells && tier ? cells.chains.find((c) => c.complexity === tier) : undefined
  const effective = chain?.effective
  if (!tier || !chain || !effective) return undefined
  if (effective.source === 'agent') return { ...blank('agent'), note: effective.withheldReason ?? AGENT_ONLY_PIN }
  if (!chain.resolvedFrom || chain.resolvedFrom === 'none') return undefined
  const preview = walk(effective.candidates, effective.source === 'pin' ? 'pin' : 'matrix')
  if (!preview) return null
  const pin = effective.source === 'pin' && effective.stagePin
    ? pinRef(effective.stagePin.strength, effective.stagePin.provenance)
    : null
  return { ...preview, complexity: tier, pin }
}

/** The pin-or-policy path: a pin list of two or more is walked (CARD-0322); one target is projected. */
function fromRolePolicy(row: RolePolicyRowDto): NextCreatePreview | null {
  const stagePin = row.stagePin
  const pinCandidates = (stagePin?.candidates ?? []).filter((c) => c.agentKind && c.modelLevel)
  if (stagePin?.agentId && pinCandidates.length === 0) return { ...blank('agent'), note: AGENT_ONLY_PIN }

  const list: Candidate[] = [...pinCandidates]
  if (stagePin?.strength === 'Preferred' && list.length > 0 && row.effective.source === 'pin' && row.level) {
    // The policy target follows a Preferred pin; the route evaluates no availability for it.
    list.push({ agentKind: row.kind ?? 'ClaudeCode', modelLevel: row.level, alias: row.alias || null, availableNow: true })
  }
  if (stagePin && list.length >= 2) {
    const walked = walk(list, 'pin')
    return walked && { ...walked, pin: pinRef(stagePin.strength, stagePin.provenance) }
  }

  const { source, agentKind, modelLevel, alias } = row.effective
  if ((source !== 'pin' && source !== 'rolePolicy') || !modelLevel) return null
  return { ...blank(source), agentKind, modelLevel, alias, projected: row }
}

/** An Orchestrator create is floored at MinOrchestratorLevel (High), as the server clamps it. */
function floor(preview: NextCreatePreview): NextCreatePreview {
  if (preview.modelLevel !== 'Medium' && preview.modelLevel !== 'Low') return preview
  return { ...preview, modelLevel: 'High', floored: true }
}

export function previewNextCreate(context: NextCreateContext): NextCreatePreview | null {
  const { kind, explicitLevel, row, cells } = context
  if (explicitLevel) return { ...blank('explicit'), modelLevel: explicitLevel }

  if (kind === 'Worker') {
    const matrix = fromDefaultTier(cells)
    if (matrix !== undefined) return matrix
  }

  const legacy = row ? fromRolePolicy(row) : null
  return legacy && kind === 'Orchestrator' ? floor(legacy) : legacy
}

function target(preview: NextCreatePreview): string {
  return `${preview.agentKind ?? 'unknown'}/${preview.modelLevel ?? 'unknown'} (${preview.alias ?? '—'})`
}

function skipping(preview: NextCreatePreview): string {
  return preview.skipped.length > 0 ? `, skipping held ${preview.skipped.join(', ')}` : ''
}

export function nextCreateLabel(preview: NextCreatePreview | null): string {
  if (!preview) return 'unknown'
  switch (preview.source) {
    case 'explicit':
      return `explicit ${preview.modelLevel}`
    case 'agent':
      return preview.note ?? AGENT_ONLY_PIN
    case 'exhausted':
      return preview.complexity
        ? `every ${preview.complexity}-row candidate is held → Blocked until a hold clears`
        : 'every pin candidate is held → Blocked until a hold clears'
    case 'matrix':
      return `${preview.complexity} row → ${target(preview)}${skipping(preview)}`
    default: {
      const base = preview.projected
        ? effectiveLabel(preview.projected)
        : `${preview.pin ? `${preview.pin.strength} ${preview.pin.provenance} pin` : 'stage pin'} → ${target(preview)}${skipping(preview)}`
      return preview.floored ? `${base}; Orchestrator floor → High` : base
    }
  }
}
