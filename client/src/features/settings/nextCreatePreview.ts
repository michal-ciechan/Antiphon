import type { AgentModelLevel } from '../../api/agents'
import type { AgentTaskKind } from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
import type { ComplexityChainListDto, TaskComplexity } from '../../api/complexityChains'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinDto, RoutingPinProvenance, RoutingPinStrength } from '../../api/routingPins'

/** Snapshot of routing for a fresh create without a card, explicit kind/agent or bypass flags. */
export type NextCreateSource = 'explicit' | 'matrix' | 'pin' | 'rolePolicy' | 'agent' | 'exhausted' | 'refused' | 'unknown'

export interface NextCreatePreview {
  source: NextCreateSource
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  alias: string | null
  complexity: TaskComplexity | null
  skipped: string[]
  /** Display reasons correspond to skipped aliases, in walk order. */
  skipReasons: string[]
  floored: boolean
  pin: { strength: RoutingPinStrength; provenance: RoutingPinProvenance } | null
  projected: RolePolicyRowDto | null
  note: string | null
}

export interface NextCreateContext {
  kind: AgentTaskKind
  explicitLevel: AgentModelLevel | null
  row: RolePolicyRowDto | undefined
  cells: ComplexityChainListDto | undefined
}

interface Candidate {
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  alias: string | null
  availableNow: boolean | null
  unavailableReason: string | null
}

const AGENT_ONLY_PIN = 'stage pin binds a standing agent; its model and admission depend on the server'
const DELEGATE_KINDS: AgentKind[] = ['ClaudeCode', 'Codex', 'Grok']

function blank(source: NextCreateSource): NextCreatePreview {
  return { source, agentKind: null, modelLevel: null, alias: null, complexity: null,
    skipped: [], skipReasons: [], floored: false, pin: null, projected: null, note: null }
}

function unknown(reason: string): NextCreatePreview {
  return { ...blank('unknown'), note: `unknown — ${reason} depends on the server` }
}

function refused(candidate: Candidate | null, note: string): NextCreatePreview {
  return { ...blank('refused'), agentKind: candidate?.agentKind ?? null,
    modelLevel: candidate?.modelLevel ?? null, alias: candidate?.alias ?? null, note }
}

function samePair(a: Candidate, b: Candidate) {
  return a.agentKind === b.agentKind && a.modelLevel === b.modelLevel
}

function unique(candidates: Candidate[]): Candidate[] {
  return candidates.filter((c, i) => candidates.findIndex((other) => samePair(c, other)) === i)
}

function forbidden(candidate: Candidate, pin: RoutingPinDto | null): boolean {
  return !!candidate.alias && !!pin?.forbiddenAliases.some((alias) => alias.toLowerCase() === candidate.alias!.toLowerCase())
}

/** GET evaluates holds before quota; create's single-target arm checks quota before holds. */
function availability(candidate: Candidate): 'available' | 'held' | 'quota' | 'unknown' {
  if (candidate.availableNow === true) return 'available'
  if (candidate.availableNow === false && candidate.unavailableReason?.startsWith('held')) return 'held'
  if (candidate.availableNow === false && candidate.unavailableReason?.startsWith('quota ')) return 'quota'
  return 'unknown'
}

/** Resolve aliases from server observations, not a second model ladder in the client. */
function observations(context: NextCreateContext): Candidate[] {
  return [
    ...(context.row?.stagePin?.candidates ?? []).filter((c) => c.agentKind && c.modelLevel && c.alias),
    ...(context.cells?.chains.flatMap((c) => [...(c.effective?.candidates ?? []), ...c.candidates]) ?? []),
  ]
}

function resolvePair(context: NextCreateContext, kind: AgentKind | null, level: AgentModelLevel | null): Candidate | null {
  const row = context.row
  const agentKind = kind ?? row?.kind ?? (row?.alias ? 'ClaudeCode' : null)
  const modelLevel = level ?? row?.level ?? null
  if (!agentKind || !modelLevel) return null
  const pair = { agentKind, modelLevel, alias: null, availableNow: null, unavailableReason: null }
  const observed = observations(context).find((c) => samePair(c, pair))
  if (observed) return observed
  if (row?.effective.agentKind === agentKind && row.effective.modelLevel === modelLevel && row.effective.alias) {
    return { ...pair, alias: row.effective.alias }
  }
  if ((row?.kind ?? 'ClaudeCode') === agentKind && row?.level === modelLevel && row.alias) {
    return { ...pair, alias: row.alias }
  }
  return null
}

/** ComplexityRoutingService.FirstSkipReasonAsync: kind, forbid, hold, quota, in that order. */
function walk(context: NextCreateContext, candidates: Candidate[], source: 'matrix' | 'pin'): NextCreatePreview {
  if (candidates.length === 0) return unknown('routing candidate list')
  const skipped: string[] = []
  const skipReasons: string[] = []
  for (const candidate of candidates) {
    const state = availability(candidate)
    let reason: string | null = null
    if (!candidate.agentKind || !DELEGATE_KINDS.includes(candidate.agentKind)
      || (context.kind === 'Orchestrator' && candidate.agentKind !== 'ClaudeCode')) {
      reason = 'not a delegate kind'
    } else if (!candidate.alias || !candidate.modelLevel) {
      return unknown('routing target')
    } else if (forbidden(candidate, context.row?.stagePin ?? null)) {
      reason = 'forbidden'
    } else if (state === 'unknown') {
      return unknown('candidate availability')
    } else if (state !== 'available') {
      reason = state
    }
    if (!reason) {
      return { ...blank(source), agentKind: candidate.agentKind, modelLevel: candidate.modelLevel,
        alias: candidate.alias, skipped, skipReasons }
    }
    skipped.push(candidate.alias ?? '—')
    // Retain the held-head wording when that candidate is also held (C097 V25).
    skipReasons.push(state === 'held' ? 'held' : reason)
  }
  const head = candidates[0]
  return { ...blank('exhausted'), agentKind: head.agentKind, modelLevel: head.modelLevel,
    alias: head.alias, skipped, skipReasons }
}

/** No-chain composition happens BEFORE MatrixDefaultApplies (AgentTaskService:789-829). */
function composePin(context: NextCreateContext, stagePin: RoutingPinDto): Candidate[] | NextCreatePreview {
  const raw = stagePin.candidates ?? (stagePin.agentKind || stagePin.modelLevel
    ? [{ agentKind: stagePin.agentKind, modelLevel: stagePin.modelLevel }]
    : [])
  const compatible = raw.filter((c) => !context.explicitLevel || !c.modelLevel || c.modelLevel === context.explicitLevel)
  if (raw.length > 0 && compatible.length === 0 && stagePin.strength === 'Required') {
    return refused(null, 'create refused (409 routing_pin_conflict): explicit level disagrees with the Required stage pin')
  }
  const slots: Candidate[] = []
  for (const candidate of compatible) {
    const resolved = resolvePair(context, candidate.agentKind, candidate.modelLevel ?? context.explicitLevel)
    if (!resolved) return unknown('pin target or availability')
    slots.push(resolved)
  }
  if (stagePin.strength === 'Preferred') {
    const policy = resolvePair(context, null, context.explicitLevel)
    if (!policy) return unknown('policy target')
    slots.push(policy)
  }
  return unique(slots)
}

function single(context: NextCreateContext, candidate: Candidate, source: 'pin' | 'rolePolicy', overlaid = false): NextCreatePreview {
  const stagePin = context.row?.stagePin ?? null
  if (!candidate.agentKind || !DELEGATE_KINDS.includes(candidate.agentKind)) {
    return refused(candidate, 'create refused (422): target is not a delegate kind')
  }
  if (context.kind === 'Orchestrator') {
    // A one-slot pin is an explicit kind overlay, while an unpinned policy kind is clamped.
    if (overlaid && candidate.agentKind !== 'ClaudeCode') {
      return refused(candidate, 'create refused (422): an Orchestrator requires ClaudeCode')
    }
    // MinOrchestratorLevel is configurable and absent from the read DTOs. Frontier is already
    // the strongest defined level. Walked lists do not call ResolveLevel and bypass this arm.
    if (candidate.modelLevel !== 'Frontier') return unknownFloor()
    if (candidate.agentKind !== 'ClaudeCode') {
      const clamped = resolvePair(context, 'ClaudeCode', candidate.modelLevel)
      if (!clamped) return unknown('Orchestrator alias')
      candidate = clamped
    }
  }
  if (forbidden(candidate, stagePin)) {
    return refused(candidate, `create refused (409 routing_pin_forbidden): ${candidate.alias} is forbidden by the stage pin`)
  }
  const state = availability(candidate)
  if (state === 'quota') return refused(candidate, `create refused (409 subscription_quota_low): ${candidate.alias} is quota-limited`)
  if (state === 'held') {
    return refused(candidate, `create refused (409): ${candidate.alias} is held; model_disabled or subscription_quota_low depends on the server`)
  }
  return { ...blank(source), agentKind: candidate.agentKind, modelLevel: candidate.modelLevel, alias: candidate.alias,
    pin: source === 'pin' && stagePin ? { strength: stagePin.strength, provenance: stagePin.provenance } : null,
    note: state === 'unknown' ? 'availability depends on the server' : null }
}

function unknownFloor(): NextCreatePreview {
  return { ...blank('unknown'), note: 'unknown — Orchestrator level depends on the server floor' }
}

export function previewNextCreate(context: NextCreateContext): NextCreatePreview | null {
  const { kind, explicitLevel, row, cells } = context
  if (!row) return unknown('role policy snapshot')
  const stagePin = row.stagePin
  const tier = cells?.defaultComplexity
  const chain = tier ? cells?.chains.find((c) => c.complexity === tier) : undefined
  const effective = chain?.effective

  if (stagePin?.agentId) {
    if (stagePin.strength === 'Preferred' || stagePin.candidates?.length || explicitLevel) {
      return { ...blank('unknown'), note: 'unknown — standing-agent kind, model and admission depend on the server' }
    }
    return { ...blank('agent'), note: AGENT_ONLY_PIN }
  }
  if (kind === 'Worker' && effective?.source === 'agent') return { ...blank('agent'), note: AGENT_ONLY_PIN }

  const composed = stagePin ? composePin(context, stagePin) : []
  if (!Array.isArray(composed)) return composed
  if (composed.length === 1) {
    const isPin = (stagePin?.candidates?.length ?? 0) > 0 || !!stagePin?.agentKind || !!stagePin?.modelLevel
    return single(context, composed[0], isPin ? 'pin' : 'rolePolicy', true)
  }

  if (!explicitLevel && kind === 'Worker') {
    if (!cells) return unknown('default-tier routing')
    if (effective?.withheldReason) return unknown('default-tier routing')
    if (chain?.resolvedFrom && chain.resolvedFrom !== 'none') {
      if (!effective) return unknown('default-tier routing')
      const preview = walk(context, effective.candidates, effective.source === 'pin' ? 'pin' : 'matrix')
      if (preview.source === 'unknown') return preview
      // A Required forbid-only pin leaves the row eligible, but a single exhausted
      // candidate takes create's refusal arm (AgentTaskService.cs:995-1034).
      if (preview.source === 'exhausted' && stagePin?.strength === 'Required' && effective.candidates.length === 1) {
        return single(context, effective.candidates[0], 'rolePolicy')
      }
      return { ...preview, complexity: tier ?? null,
        pin: effective.source === 'pin' && (stagePin ?? effective.stagePin)
          ? { strength: (stagePin ?? effective.stagePin)!.strength, provenance: (stagePin ?? effective.stagePin)!.provenance } : null }
    }
  }
  if (composed.length >= 2 && stagePin) {
    const preview = walk(context, composed, 'pin')
    return { ...preview, pin: { strength: stagePin.strength, provenance: stagePin.provenance } }
  }
  if (explicitLevel && !stagePin) {
    return kind === 'Orchestrator' && explicitLevel !== 'Frontier'
      ? unknownFloor() : { ...blank('explicit'), modelLevel: explicitLevel }
  }
  // A partial/unsupported policy target is withheld by the API; do not invent a kind or tier.
  const candidate = resolvePair(context, null, explicitLevel)
  if (!candidate) return null
  return single(context, candidate, 'rolePolicy')
}

function target(preview: NextCreatePreview): string {
  return `${preview.agentKind ?? 'unknown'}/${preview.modelLevel ?? 'unknown'} (${preview.alias ?? '—'})`
}

export function nextCreateSkipping(preview: NextCreatePreview): string {
  if (preview.skipped.length === 0) return ''
  return preview.skipReasons.every((reason) => reason === 'held')
    ? `, skipping held ${preview.skipped.join(', ')}`
    : `, skipping ${preview.skipped.map((alias, i) => `${alias} (${preview.skipReasons[i]})`).join(', ')}`
}

export function nextCreateLabel(preview: NextCreatePreview | null): string {
  if (!preview) return 'unknown — policy target depends on the server'
  switch (preview.source) {
    case 'explicit': return `explicit ${preview.modelLevel}`
    case 'agent': return preview.note ?? AGENT_ONLY_PIN
    case 'unknown': return preview.note ?? 'unknown — depends on the server'
    case 'refused': return preview.note ?? 'create refused'
    case 'exhausted': {
      const list = preview.complexity ? `${preview.complexity}-row` : 'pin'
      return preview.skipReasons.every((reason) => reason === 'held')
        ? `every ${list} candidate is held → Blocked until a hold clears`
        : `no eligible ${list} candidate → Blocked; routing must become eligible`
    }
    case 'matrix': return `${preview.complexity} row → ${target(preview)}${nextCreateSkipping(preview)}`
    default: {
      const prefix = preview.pin ? `${preview.pin.strength} ${preview.pin.provenance} pin` : 'default'
      return `${prefix} → ${target(preview)}${nextCreateSkipping(preview)}${preview.note ? `; ${preview.note}` : ''}`
    }
  }
}
