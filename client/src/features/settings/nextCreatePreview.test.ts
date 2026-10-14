import { describe, expect, it } from 'vitest'
import type { ComplexityCandidateDto, ComplexityChainDto, ComplexityChainListDto, ComplexityEffectiveDto, TaskComplexity } from '../../api/complexityChains'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinDto } from '../../api/routingPins'
import { nextCreateLabel, previewNextCreate, type NextCreateContext } from './nextCreatePreview'

function row(overrides: Partial<RolePolicyRowDto> = {}): RolePolicyRowDto {
  return {
    role: 'Code',
    configured: true,
    level: 'Frontier',
    kind: 'ClaudeCode',
    alias: 'fable',
    escalateTo: null,
    escalateAfterMinutes: null,
    timeoutMinutes: 240,
    stagePin: null,
    effective: { source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', strength: null },
    ...overrides,
  }
}

const astra: ComplexityCandidateDto = { agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', availableNow: true, unavailableReason: null }
const opus: ComplexityCandidateDto = { agentKind: 'ClaudeCode', modelLevel: 'High', alias: 'opus', availableNow: true, unavailableReason: null }
const fable: ComplexityCandidateDto = { agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', availableNow: true, unavailableReason: null }
const luna: ComplexityCandidateDto = { agentKind: 'Codex', modelLevel: 'Low', alias: 'gpt-5.6-luna', availableNow: true, unavailableReason: null }
const held = (c: ComplexityCandidateDto): ComplexityCandidateDto => ({ ...c, availableNow: false, unavailableReason: 'held (manual)' })

function effective(overrides: Partial<ComplexityEffectiveDto> = {}): ComplexityEffectiveDto {
  return { source: 'role', candidates: [astra, opus], stagePin: null, governsDefault: true, withheldReason: null, ...overrides }
}

function chain(complexity: TaskComplexity, eff: ComplexityEffectiveDto | null, resolvedFrom: ComplexityChainDto['resolvedFrom'] = 'role'): ComplexityChainDto {
  return {
    complexity,
    candidates: eff?.candidates ?? [],
    provenance: 'Human',
    source: 'pin',
    reason: null,
    notAfter: null,
    updatedAt: null,
    role: 'Code',
    resolvedFrom,
    effective: eff,
  }
}

function cells(defaultComplexity: TaskComplexity, chains: ComplexityChainDto[]): ComplexityChainListDto {
  return { chains, defaultComplexity }
}

const mediumRow = cells('Medium', [chain('Hard', effective({ candidates: [fable] })), chain('Medium', effective()), chain('Easy', effective({ candidates: [luna] }))])

function pin(overrides: Partial<RoutingPinDto> = {}): RoutingPinDto {
  return {
    id: 'pin-code',
    cardId: null,
    cardIdentifier: null,
    role: 'Code',
    provenance: 'Human',
    strength: 'Required',
    agentKind: null,
    modelLevel: null,
    modelAlias: null,
    agentId: null,
    forbiddenAliases: [],
    notBefore: null,
    notAfter: null,
    reason: 'code on astra',
    sourceTaskId: null,
    createdAt: '2026-10-14T10:00:00Z',
    updatedAt: '2026-10-14T10:00:00Z',
    candidates: [],
    candidateCount: 0,
    ...overrides,
  }
}

describe('previewNextCreate', () => {
  it('C1250 V1 worker-reads-default-tier-cell', () => {
    const preview = previewNextCreate({ kind: 'Worker', explicitLevel: null, row: row(), cells: mediumRow })
    expect(preview).toMatchObject({ source: 'matrix', agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', complexity: 'Medium', skipped: [] })
    expect(nextCreateLabel(preview)).toBe('Medium row → Codex/Frontier (gpt-6-astra)')
  })

  it('C1250 V2 held-head-walks-to-next-candidate', () => {
    const preview = previewNextCreate({
      kind: 'Worker',
      explicitLevel: null,
      row: row(),
      cells: cells('Medium', [chain('Medium', effective({ candidates: [held(astra), opus] }))]),
    })
    expect(preview).toMatchObject({ source: 'matrix', agentKind: 'ClaudeCode', modelLevel: 'High', alias: 'opus', skipped: ['gpt-6-astra'] })
    expect(nextCreateLabel(preview)).toBe('Medium row → ClaudeCode/High (opus), skipping held gpt-6-astra')
  })

  it('C1250 V3 every-candidate-held-is-blocked', () => {
    const preview = previewNextCreate({
      kind: 'Worker',
      explicitLevel: null,
      row: row(),
      cells: cells('Medium', [chain('Medium', effective({ candidates: [held(astra), held(opus)] }))]),
    })
    expect(preview).toMatchObject({ source: 'exhausted', agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', complexity: 'Medium' })
    expect(nextCreateLabel(preview)).toBe('every Medium-row candidate is held → Blocked until a hold clears')
  })

  it('C1250 V4 non-medium-configured-default-tier', () => {
    const preview = previewNextCreate({ kind: 'Worker', explicitLevel: null, row: row(), cells: { ...mediumRow, defaultComplexity: 'Easy' } })
    expect(preview).toMatchObject({ source: 'matrix', agentKind: 'Codex', modelLevel: 'Low', alias: 'gpt-5.6-luna', complexity: 'Easy' })
    expect(nextCreateLabel(preview)).toBe('Easy row → Codex/Low (gpt-5.6-luna)')
  })

  it('C1250 V5 orchestrator-ignores-the-row-and-discloses-the-floor', () => {
    const docs = row({
      role: 'Docs',
      level: 'Medium',
      alias: 'sonnet',
      effective: { source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Medium', alias: 'sonnet', strength: null },
    })
    const preview = previewNextCreate({ kind: 'Orchestrator', explicitLevel: null, row: docs, cells: mediumRow })
    expect(preview).toMatchObject({ source: 'unknown', agentKind: null, modelLevel: null, complexity: null, floored: false })
    expect(nextCreateLabel(preview)).toBe('unknown — Orchestrator level depends on the server floor')

    const code = previewNextCreate({ kind: 'Orchestrator', explicitLevel: null, row: row(), cells: mediumRow })
    expect(code).toMatchObject({ source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', floored: false })
    expect(nextCreateLabel(code)).toBe('default → ClaudeCode/Frontier (fable)')
  })

  it('C1250 V6 agent-only-stage-pin', () => {
    const reason = 'stage pin binds standing agent Scout (Codex); an ordinary dispatch runs on that agent and does not walk this row'
    const worker = previewNextCreate({
      kind: 'Worker',
      explicitLevel: null,
      row: row({ stagePin: pin({ agentId: 'agent-1' }) }),
      cells: cells('Medium', [chain('Medium', effective({ source: 'agent', candidates: [], withheldReason: reason }))]),
    })
    expect(worker).toMatchObject({ source: 'agent', agentKind: null, modelLevel: null, alias: null })
    expect(nextCreateLabel(worker)).toBe('stage pin binds a standing agent; its model and admission depend on the server')

    const orchestrator = previewNextCreate({
      kind: 'Orchestrator',
      explicitLevel: null,
      row: row({
        stagePin: pin({ agentId: 'agent-1' }),
        effective: { source: 'pin', agentKind: null, modelLevel: null, alias: null, strength: 'Required' },
      }),
      cells: undefined,
    })
    expect(orchestrator).toMatchObject({ source: 'agent', modelLevel: null })
    expect(nextCreateLabel(orchestrator)).toBe('stage pin binds a standing agent; its model and admission depend on the server')
  })

  it('C1250 V7 explicit-level-bypasses-the-row', () => {
    const preview = previewNextCreate({ kind: 'Worker', explicitLevel: 'Low', row: row(), cells: mediumRow })
    expect(preview).toMatchObject({ source: 'explicit', modelLevel: 'Low', complexity: null })
    expect(nextCreateLabel(preview)).toBe('explicit Low')
  })

  it.each([
    ['no-cells', undefined],
    ['empty-matrix', cells('Medium', [])],
    ['unresolved-row', cells('Medium', [chain('Medium', effective({ source: 'rolePolicy', candidates: [fable] }), 'none')])],
  ])('C1250 V8 %s-respects-read-certainty', (name, list) => {
    const preview = previewNextCreate({ kind: 'Worker', explicitLevel: null, row: row(), cells: list })
    if (name === 'no-cells') {
      expect(preview).toMatchObject({ source: 'unknown', agentKind: null, modelLevel: null, alias: null, complexity: null })
      expect(nextCreateLabel(preview)).toBe('unknown — default-tier routing depends on the server')
    } else {
      expect(preview).toMatchObject({ source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', complexity: null })
      expect(nextCreateLabel(preview)).toBe(name === 'empty-matrix'
        ? 'default → ClaudeCode/Frontier (fable); availability depends on the server'
        : 'default → ClaudeCode/Frontier (fable)')
    }
  })

  it('C1250 V9 required-stage-pin-walks-inside-the-row', () => {
    const stagePin = pin({ candidates: [held(astra), opus], candidateCount: 2 })
    const preview = previewNextCreate({
      kind: 'Worker',
      explicitLevel: null,
      row: row({ stagePin }),
      cells: cells('Medium', [chain('Medium', effective({ source: 'pin', stagePin, candidates: [held(astra), opus] }))]),
    })
    expect(preview).toMatchObject({ source: 'pin', agentKind: 'ClaudeCode', modelLevel: 'High', complexity: 'Medium', skipped: ['gpt-6-astra'] })
    expect(nextCreateLabel(preview)).toBe('Required Human pin → ClaudeCode/High (opus), skipping held gpt-6-astra')
  })

  it('C1250 V10 legacy-pin-list-skips-a-held-head', () => {
    const plan = row({
      stagePin: pin({
        candidates: [
          { agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', availableNow: false, unavailableReason: 'held' },
          { agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', availableNow: true, unavailableReason: null },
        ],
        candidateCount: 2,
      }),
      effective: { source: 'pin', agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', strength: 'Required' },
    })
    for (const kind of ['Worker', 'Orchestrator'] as const) {
      const preview = previewNextCreate({ kind, explicitLevel: null, row: plan, cells: cells('Medium', []) })
      expect(preview).toMatchObject({ source: 'pin', agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', skipped: ['gpt-6-astra'] })
      expect(nextCreateLabel(preview)).toBe('Required Human pin → ClaudeCode/Frontier (fable), skipping held gpt-6-astra')
    }
  })

  it('C1250 V11 withheld-row-is-unknown', () => {
    const preview = previewNextCreate({
      kind: 'Worker',
      explicitLevel: null,
      row: row(),
      cells: cells('Medium', [chain('Medium', effective({ candidates: [], withheldReason: 'routing candidate names a kind or level which delegation cannot launch' }))]),
    })
    expect(preview).toMatchObject({ source: 'unknown', modelLevel: null, alias: null })
    expect(nextCreateLabel(preview)).toBe('unknown — default-tier routing depends on the server')
  })
})

// Expected tuples are transcribed from the server branches, not computed by the preview.
// References are anchored to server source at 805d4a6d3 (reopened for repair 63109ffc; server unchanged).
const sol: ComplexityCandidateDto = { ...astra, modelLevel: 'High', alias: 'gpt-6.1-sol' }
const sonnet: ComplexityCandidateDto = { ...opus, modelLevel: 'Medium', alias: 'sonnet' }
const quota = (c: ComplexityCandidateDto): ComplexityCandidateDto => ({ ...c, availableNow: false, unavailableReason: 'quota 2% remaining, resets in 10m' })
function scenario(options: {
  kind?: NextCreateContext['kind']; level?: NextCreateContext['explicitLevel'];
  pin?: RoutingPinDto; policy?: RolePolicyRowDto; candidates?: ComplexityCandidateDto[];
  resolved?: ComplexityChainDto['resolvedFrom']; source?: ComplexityEffectiveDto['source'];
  missingCells?: boolean; withheld?: string;
} = {}): NextCreateContext {
  const policy = options.policy ?? row()
  const stagePin = options.pin ?? null
  return {
    kind: options.kind ?? 'Worker', explicitLevel: options.level ?? null,
    row: { ...policy, stagePin, effective: stagePin?.candidates?.[0]?.alias
      ? { source: 'pin', agentKind: stagePin.candidates[0].agentKind, modelLevel: stagePin.candidates[0].modelLevel, alias: stagePin.candidates[0].alias, strength: stagePin.strength }
      : policy.effective },
    cells: options.missingCells ? undefined : cells('Medium', [chain('Medium', effective({
      candidates: options.candidates ?? [astra, sol, opus], source: options.source ?? 'role', stagePin,
      withheldReason: options.withheld ?? null,
    }), options.resolved ?? 'role')]),
  }
}
function listed(candidates: RoutingPinDto['candidates'], overrides: Partial<RoutingPinDto> = {}): RoutingPinDto {
  return pin({ candidates, candidateCount: candidates?.length, ...overrides })
}
const lowPolicy = row({ role: 'Docs', level: 'Medium', alias: 'sonnet', effective: { source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Medium', alias: 'sonnet', strength: null } })
const codexPolicy = row({ kind: 'Codex', alias: 'gpt-6-astra', effective: { source: 'rolePolicy', agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', strength: null } })
type ParityRow = { id: string; server: string; context: NextCreateContext; expected: [string, string | null, string | null, string | null, string | null] | null; label?: string }
const parityRows: ParityRow[] = [
  { id: 'D1 required-forbid-only-single-role-held', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'role', source: 'role', candidates: [held(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409): gpt-6-astra is held; model_disabled or subscription_quota_low depends on the server' },
  { id: 'D1 required-forbid-only-single-role-quota', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'role', source: 'role', candidates: [quota(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 subscription_quota_low): gpt-6-astra is quota-limited' },
  { id: 'D1 required-forbid-only-single-role-forbidden', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['GPT-6-ASTRA'] }), resolved: 'role', source: 'role', candidates: [astra] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 routing_pin_forbidden): gpt-6-astra is forbidden by the stage pin' },
  { id: 'D1 required-forbid-only-single-role-available', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'role', source: 'role', candidates: [astra] }), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'], label: 'Medium row → Codex/Frontier (gpt-6-astra)' },
  { id: 'D1 required-forbid-only-single-any-held', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'any', source: 'any', candidates: [held(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409): gpt-6-astra is held; model_disabled or subscription_quota_low depends on the server' },
  { id: 'D1 required-forbid-only-single-any-quota', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'any', source: 'any', candidates: [quota(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 subscription_quota_low): gpt-6-astra is quota-limited' },
  { id: 'D1 required-forbid-only-single-any-forbidden', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['GPT-6-ASTRA'] }), resolved: 'any', source: 'any', candidates: [astra] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 routing_pin_forbidden): gpt-6-astra is forbidden by the stage pin' },
  { id: 'D1 required-forbid-only-single-any-available', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'any', source: 'any', candidates: [astra] }), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'], label: 'Medium row → Codex/Frontier (gpt-6-astra)' },
  { id: 'D1 required-forbid-only-single-config-held', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'config', source: 'config', candidates: [held(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409): gpt-6-astra is held; model_disabled or subscription_quota_low depends on the server' },
  { id: 'D1 required-forbid-only-single-config-quota', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'config', source: 'config', candidates: [quota(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 subscription_quota_low): gpt-6-astra is quota-limited' },
  { id: 'D1 required-forbid-only-single-config-forbidden', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['GPT-6-ASTRA'] }), resolved: 'config', source: 'config', candidates: [astra] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 routing_pin_forbidden): gpt-6-astra is forbidden by the stage pin' },
  { id: 'D1 required-forbid-only-single-config-available', server: 'RoutingCandidates.cs:58-96; AgentTaskService.cs:987-1034', context: scenario({ pin: pin({ forbiddenAliases: ['unrelated-alias'] }), resolved: 'config', source: 'config', candidates: [astra] }), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'], label: 'Medium row → Codex/Frontier (gpt-6-astra)' },
  { id: 'no-pin-single-held-still-blocked', server: 'AgentTaskService.cs:995-998,1044-1051', context: scenario({ candidates: [held(astra)] }), expected: ['exhausted', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'], label: 'every Medium-row candidate is held → Blocked until a hold clears' },
  { id: 'worker-grok-row-target', server: 'ComplexityRoutingService.cs:498-501; AgentTaskService.cs:987-991', context: scenario({ candidates: [{ ...astra, agentKind: 'Grok', alias: 'grok-4.7' }] }), expected: ['matrix', 'Grok', 'Frontier', 'grok-4.7', 'Medium'], label: 'Medium row → Grok/Frontier (grok-4.7)' },
  { id: 'role_policy_level_invalid-Worker-withheld', server: 'AgentTaskService.cs:4122-4129; RolePolicyReadService.cs:17-24,91-100', context: scenario({ kind: 'Worker', policy: row({ level: null, alias: '', effective: { source: 'rolePolicy', agentKind: null, modelLevel: null, alias: null, strength: null } }), resolved: 'none', candidates: [], withheld: 'Code policy names an unsupported target' }), expected: ['unknown', null, null, null, null], label: 'unknown — default-tier routing depends on the server' },
  { id: 'role_policy_kind_unsupported-Worker-withheld', server: 'AgentTaskService.cs:4174-4187; RolePolicyReadService.cs:17-24,91-100', context: scenario({ kind: 'Worker', policy: row({ kind: 'Raw', alias: '', effective: { source: 'rolePolicy', agentKind: null, modelLevel: null, alias: null, strength: null } }), resolved: 'none', candidates: [], withheld: 'Code policy names an unsupported target' }), expected: ['unknown', null, null, null, null], label: 'unknown — default-tier routing depends on the server' },
  { id: 'role_policy_level_invalid-Orchestrator-withheld', server: 'AgentTaskService.cs:4122-4129; RolePolicyReadService.cs:17-24,91-100', context: scenario({ kind: 'Orchestrator', policy: row({ level: null, alias: '', effective: { source: 'rolePolicy', agentKind: null, modelLevel: null, alias: null, strength: null } }), resolved: 'none', candidates: [], withheld: 'Code policy names an unsupported target' }), expected: null, label: 'unknown — policy target depends on the server' },
  { id: 'role_policy_kind_unsupported-Orchestrator-withheld', server: 'AgentTaskService.cs:4174-4187; RolePolicyReadService.cs:17-24,91-100', context: scenario({ kind: 'Orchestrator', policy: row({ kind: 'Raw', alias: '', effective: { source: 'rolePolicy', agentKind: null, modelLevel: null, alias: null, strength: null } }), resolved: 'none', candidates: [], withheld: 'Code policy names an unsupported target' }), expected: null, label: 'unknown — policy target depends on the server' },

  { id: 'F1 required-forbid-only', server: 'ComplexityRoutingService.cs:504-508', context: scenario({ pin: pin({ forbiddenAliases: ['GPT-6-ASTRA'] }) }), expected: ['matrix', 'Codex', 'High', 'gpt-6.1-sol', 'Medium'] },
  { id: 'F1 legacy-forbidden-head', server: 'ComplexityRoutingService.cs:504-508', context: scenario({ pin: listed([astra, opus], { forbiddenAliases: ['gpt-6-astra'] }), resolved: 'none' }), expected: ['pin', 'ClaudeCode', 'High', 'opus', null] },
  { id: 'F2 preferred-forbid-only-refusal', server: 'AgentTaskService.cs:809-815,1116', context: scenario({ pin: pin({ strength: 'Preferred', forbiddenAliases: ['FABLE'] }) }), expected: ['refused', 'ClaudeCode', 'Frontier', 'fable', null], label: 'create refused (409 routing_pin_forbidden): fable is forbidden by the stage pin' },
  { id: 'F2 preferred-forbid-only-policy', server: 'RoutingCandidates.cs:79-87; AgentTaskService.cs:924', context: scenario({ pin: pin({ strength: 'Preferred', forbiddenAliases: ['opus'] }), candidates: [astra, fable] }), expected: ['rolePolicy', 'ClaudeCode', 'Frontier', 'fable', null] },
  { id: 'F2 preferred-dedup-held', server: 'RoutingCandidates.cs:155-160; AgentTaskService.cs:1167,1181-1186', context: scenario({ pin: listed([held(fable)], { strength: 'Preferred' }), candidates: [astra, held(fable), opus] }), expected: ['refused', 'ClaudeCode', 'Frontier', 'fable', null], label: 'create refused (409): fable is held; model_disabled or subscription_quota_low depends on the server' },
  { id: 'F2 preferred-dedup-available', server: 'RoutingCandidates.cs:155-160', context: scenario({ pin: listed([fable], { strength: 'Preferred' }) }), expected: ['pin', 'ClaudeCode', 'Frontier', 'fable', null] },
  { id: 'F3 orchestrator-kind-filter', server: 'ComplexityRoutingService.cs:498-499', context: scenario({ kind: 'Orchestrator', pin: listed([astra, opus]) }), expected: ['pin', 'ClaudeCode', 'High', 'opus', null] },
  { id: 'F3 walked-medium-not-floored', server: 'AgentTaskService.cs:1082', context: scenario({ kind: 'Orchestrator', pin: listed([sonnet, opus]) }), expected: ['pin', 'ClaudeCode', 'Medium', 'sonnet', null], label: 'Required Human pin → ClaudeCode/Medium (sonnet)' },
  { id: 'F4 single-required-held', server: 'AgentTaskService.cs:809-815,1167', context: scenario({ pin: listed([held(astra)]), source: 'pin', candidates: [held(astra)] }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409): gpt-6-astra is held; model_disabled or subscription_quota_low depends on the server' },
  { id: 'required-single-available', server: 'AgentTaskService.cs:809-815', context: scenario({ pin: listed([opus]), candidates: [astra, opus] }), expected: ['pin', 'ClaudeCode', 'High', 'opus', null] },
  { id: 'required-single-forbidden-before-quota', server: 'AgentTaskService.cs:1116-1120', context: scenario({ pin: listed([quota(astra)], { forbiddenAliases: ['gpt-6-astra'] }) }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 routing_pin_forbidden): gpt-6-astra is forbidden by the stage pin' },
  { id: 'required-single-quota', server: 'AgentTaskService.cs:1120', context: scenario({ pin: listed([quota(astra)]) }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (409 subscription_quota_low): gpt-6-astra is quota-limited' },
  { id: 'preferred-multiple-uses-matrix', server: 'AgentTaskService.cs:803-804,924-939; RoutingCandidates.cs:66-74', context: scenario({ pin: listed([held(opus)], { strength: 'Preferred' }), source: 'pin+chain', candidates: [held(opus), astra, sol] }), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'required-multiple-replaces-matrix', server: 'RoutingCandidates.cs:58-63', context: scenario({ pin: listed([held(astra), opus]), source: 'pin', candidates: [held(astra), opus] }), expected: ['pin', 'ClaudeCode', 'High', 'opus', 'Medium'] },
  { id: 'required-dedup-single', server: 'RoutingCandidates.cs:136-140,155-160', context: scenario({ pin: listed([astra, astra]) }), expected: ['pin', 'Codex', 'Frontier', 'gpt-6-astra', null] },
  { id: 'preferred-policy-tail-unknown', server: 'RoutingCandidates.cs:79-87; RolePolicyReadService.cs:91-100', context: scenario({ pin: listed([held(astra)], { strength: 'Preferred' }), resolved: 'none', candidates: [] }), expected: ['unknown', null, null, null, null], label: 'unknown — candidate availability depends on the server' },
  { id: 'preferred-policy-tail-evaluated', server: 'RoutingCandidates.cs:79-87', context: scenario({ pin: listed([held(astra)], { strength: 'Preferred' }), resolved: 'none', candidates: [held(astra), fable] }), expected: ['pin', 'ClaudeCode', 'Frontier', 'fable', null] },
  { id: 'matrix-quota-skip', server: 'ComplexityRoutingService.cs:518-524', context: scenario({ candidates: [quota(astra), opus] }), expected: ['matrix', 'ClaudeCode', 'High', 'opus', 'Medium'], label: 'Medium row → ClaudeCode/High (opus), skipping gpt-6-astra (quota)' },
  { id: 'matrix-quota-exhausted', server: 'AgentTaskService.cs:1046-1050', context: scenario({ candidates: [quota(astra), quota(opus)] }), expected: ['exhausted', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'], label: 'no eligible Medium-row candidate → Blocked; routing must become eligible' },
  { id: 'matrix-forbidden-exhausted', server: 'ComplexityRoutingService.cs:504-508; AgentTaskService.cs:1046', context: scenario({ pin: pin({ forbiddenAliases: ['gpt-6-astra', 'opus'] }), candidates: [astra, opus] }), expected: ['exhausted', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'orchestrator-list-exhausted', server: 'AgentTaskService.cs:1097-1101', context: scenario({ kind: 'Orchestrator', pin: listed([astra, sol]) }), expected: ['exhausted', 'Codex', 'Frontier', 'gpt-6-astra', null] },
  { id: 'orchestrator-explicit-pin-kind-refused', server: 'AgentTaskService.cs:4190-4200', context: scenario({ kind: 'Orchestrator', pin: listed([astra]) }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null], label: 'create refused (422): an Orchestrator requires ClaudeCode' },
  { id: 'orchestrator-policy-kind-clamped', server: 'AgentTaskService.cs:4202-4204', context: scenario({ kind: 'Orchestrator', policy: codexPolicy, candidates: [astra, fable] }), expected: ['rolePolicy', 'ClaudeCode', 'Frontier', 'fable', null] },
  { id: 'orchestrator-policy-alias-unknown', server: 'AgentTaskService.cs:4202-4204; RolePolicyReadService.cs:91-100', context: scenario({ kind: 'Orchestrator', policy: codexPolicy }), expected: ['unknown', null, null, null, null] },
  { id: 'orchestrator-configured-floor-unexposed', server: 'AgentTaskService.cs:4132-4133; RolePolicyReadService.cs:65-79', context: scenario({ kind: 'Orchestrator', policy: lowPolicy }), expected: ['unknown', null, null, null, null], label: 'unknown — Orchestrator level depends on the server floor' },
  { id: 'orchestrator-explicit-floor-unexposed', server: 'AgentTaskService.cs:4132-4133', context: scenario({ kind: 'Orchestrator', level: 'Low' }), expected: ['unknown', null, null, null, null] },
  { id: 'explicit-required-conflict', server: 'RoutingPinService.cs:348-357', context: scenario({ level: 'Low', pin: listed([astra]) }), expected: ['refused', null, null, null, null], label: 'create refused (409 routing_pin_conflict): explicit level disagrees with the Required stage pin' },
  { id: 'explicit-required-narrowed-single', server: 'RoutingPinService.cs:380-384; AgentTaskService.cs:809', context: scenario({ level: 'High', pin: listed([astra, opus]) }), expected: ['pin', 'ClaudeCode', 'High', 'opus', null] },
  { id: 'explicit-preferred-incompatible-unknown-alias', server: 'RoutingCandidates.cs:129-140,79-87', context: scenario({ level: 'Low', pin: listed([astra], { strength: 'Preferred' }) }), expected: ['unknown', null, null, null, null] },
  { id: 'partial-pin-availability-not-evaluated', server: 'RoutingPinService.cs:795-800; RoutingCandidates.cs:136-140', context: scenario({ pin: listed([{ ...astra, modelLevel: null, alias: null }]), resolved: 'none', candidates: [] }), expected: ['unknown', null, null, null, null] },
  { id: 'partial-pin-filled-by-effective-view', server: 'ComplexityEffective.cs:34-38; RoutingCandidates.cs:136-140', context: scenario({ pin: listed([{ ...astra, modelLevel: null, alias: null }]), candidates: [astra, opus] }), expected: ['pin', 'Codex', 'Frontier', 'gpt-6-astra', null] },
  { id: 'agent-with-candidates-needs-agent-state', server: 'AgentTaskService.cs:840-861', context: scenario({ pin: listed([astra], { agentId: 'standing-agent' }) }), expected: ['unknown', null, null, null, null], label: 'unknown — standing-agent kind, model and admission depend on the server' },
  { id: 'preferred-agent-only-needs-agent-state', server: 'RoutingCandidates.cs:79-87; AgentTaskService.cs:840-861', context: scenario({ pin: pin({ strength: 'Preferred', agentId: 'standing-agent' }) }), expected: ['unknown', null, null, null, null] },
  { id: 'availability-read-error-not-exhausted', server: 'AgentTaskService.cs:949-953; ComplexityChainService.cs:165-174', context: scenario({ candidates: [{ ...astra, availableNow: false, unavailableReason: 'availability unavailable' }, opus] }), expected: ['unknown', null, null, null, null] },
  { id: 'withheld-slot-not-silently-skipped', server: 'ComplexityEffective.cs:58-64', context: scenario({ candidates: [opus], withheld: 'routing candidate names a kind or level which delegation cannot launch' }), expected: ['unknown', null, null, null, null] },
  { id: 'missing-policy-cannot-evaluate-forbids', server: 'AgentTaskService.cs:773; ComplexityRoutingService.cs:504-508', context: { ...scenario(), row: undefined }, expected: ['unknown', null, null, null, null] },
  { id: 'any-row', server: 'ComplexityRoutingService.cs:326-328', context: scenario({ resolved: 'any', source: 'any' }), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'config-row', server: 'ComplexityRoutingService.cs:330-340', context: scenario({ resolved: 'config', source: 'config' }), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'legacy-policy-forbidden', server: 'RoutingPinService.cs:413-432', context: scenario({ pin: pin({ forbiddenAliases: ['fable'] }), resolved: 'none' }), expected: ['refused', 'ClaudeCode', 'Frontier', 'fable', null] },
  { id: 'legacy-policy-held', server: 'AgentTaskService.cs:1181-1186', context: scenario({ resolved: 'none', candidates: [held(fable)] }), expected: ['refused', 'ClaudeCode', 'Frontier', 'fable', null] },
]
describe('C1250 V17 server-branch parity', () => {
  for (const { id, server, context, expected, label } of parityRows) {
    it(`${id} [${server}]`, () => {
      const preview = previewNextCreate(context)
      if (expected === null) expect(preview).toBeNull()
      else expect([preview?.source, preview?.agentKind, preview?.modelLevel, preview?.alias, preview?.complexity]).toEqual(expected)
      if (label) expect(nextCreateLabel(preview)).toBe(label)
    })
  }
})

// V-22: card identifiers use the stored title, with server-only binding resolution left unknown.
const contextRows: ParityRow[] = [
  { id: 'goal-boundary-ua7cb-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "ꟋCARD-1250 repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-ua7cb-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "CARD-1250Ɤ repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-ua7cb-in-word-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix xꟋCARD-1250" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-ua7cb-in-digits', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-12Ɤ3" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-ua7cb-in-word-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-1250Ɤx" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1c89-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "ᲉCARD-1250 repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1c89-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "CARD-1250Ᲊ repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1c89-in-word-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix xᲉCARD-1250" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1c89-in-digits', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-12Ᲊ3" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1c89-in-word-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-1250Ᲊx" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1ad0-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "᫐CARD-1250 repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1ad0-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "CARD-1250᫐ repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1ad0-in-word-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix x᫐CARD-1250" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1ad0-in-digits', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-12᫐3" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u1ad0-in-word-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-1250᫐x" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u10400-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "𐐀CARD-1250 repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u10400-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "CARD-1250𐐀 repair" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u10400-in-word-prefix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix x𐐀CARD-1250" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u10400-in-digits', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-12𐐀3" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-boundary-u10400-in-word-suffix', server: 'AgentTaskCardBinder.cs:44-64; AgentTaskService.cs:4519-4535,4546-4547', context: { ...scenario(), goal: "Fix CARD-1250𐐀x" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-ascii-letter-prefix', server: 'AgentTaskCardBinder.cs:44-64', context: { ...scenario(), goal: "ACARD-1250" }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-ascii-letter-suffix', server: 'AgentTaskCardBinder.cs:44-64', context: { ...scenario(), goal: "CARD-1250Ax" }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-ascii-digit-prefix', server: 'AgentTaskCardBinder.cs:44-64', context: { ...scenario(), goal: "0CARD-1250" }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-ascii-digit-suffix', server: 'AgentTaskCardBinder.cs:44-64', context: { ...scenario(), goal: "CARD-12500x" }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-ascii-underscore-prefix', server: 'AgentTaskCardBinder.cs:44-64', context: { ...scenario(), goal: "_CARD-1250" }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-ascii-underscore-suffix', server: 'AgentTaskCardBinder.cs:44-64', context: { ...scenario(), goal: "CARD-1250_x" }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-supplementary-prefix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "𐐀CARD-1" }, expected: ['unknown', null, null, null, null] },
  { id: 'goal-supplementary-suffix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "CARD-1𐐀" }, expected: ['unknown', null, null, null, null] },
  { id: 'goal-nonjoiner-prefix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "‌CARD-1" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-joiner-prefix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "‍CARD-1" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-nonjoiner-suffix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "CARD-1‌" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-joiner-suffix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "CARD-1‍" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-supplementary-digit-suffix', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "CARD-12𐒠" }, expected: ['unknown', null, null, null, null] },
  { id: 'goal-unparseable-decimal', server: 'AgentTaskCardBinder.cs:43-64', context: { ...scenario(), goal: "CARD-12١" }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },

  { id: 'goal-leading', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"CARD-1250 — change requested on Review: "} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-interior', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"Repair CARD-1250 please"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-case-and-zeros', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"repair card-0001250"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-hash-not-bound', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"#1250 repair"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-plain-card-not-bound', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"card 1250 repair"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-prefix-boundary', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"XCARD-1250 repair"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-unicode-prefix-boundary', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"éCARD-1250 repair"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-suffix-boundary', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"CARD-1250x repair"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-overflow-not-bound', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"CARD-2147483648 repair"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-max-int', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"CARD-2147483647 repair"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-zero', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"CARD-0000 repair"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-second-line-not-title', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"Repair preview\nCARD-1250"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-empty-lines', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"\r\n\r\nCARD-1250 repair"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'goal-whitespace-first-line', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":" \nCARD-1250 repair"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-unicode-newline', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"Repair previewCARD-1250"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-after-clamp', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx CARD-1250"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'goal-clamp-partial-identifier', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"goal":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx CARD-1250"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'title-precedes-goal', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"title":"Repair preview","goal":"CARD-1250"} }, expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'explicit-title-card', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"title":"Repair CARD-1250","goal":"Repair preview"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'explicit-card-hash', server: 'AgentTaskCardBinder.cs:94-95,127-154', context: { ...scenario(), ...{"explicitCard":"#1250"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'explicit-card-guid', server: 'AgentTaskCardBinder.cs:94-95,127-154', context: { ...scenario(), ...{"explicitCard":"00000000-0000-0000-0000-000000001250"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'card-origin-edited-goal', server: 'AgentTaskCardBinder.cs:43-64,92-116; AgentTaskService.cs:4519-4547', context: { ...scenario(), ...{"cardContext":true,"goal":"Repair preview"} }, expected: ['unknown', null, null, null, null], label: 'unknown — a card pin may apply; routing depends on the server' },
  { id: 'card-over-stage-pin', server: 'RoutingPinService.cs:306-308', context: { ...scenario({ pin: listed([opus]) }), goal: 'CARD-1250 repair' }, expected: ['unknown', null, null, null, null] },
  { id: 'human-card-exempts-stage-forbid', server: 'RoutingPinService.cs:413-418', context: { ...scenario({ pin: pin({ forbiddenAliases: ['gpt-6-astra'] }), candidates: [astra] }), goal: 'CARD-1250 repair' }, expected: ['unknown', null, null, null, null] },
  { id: 'card-and-explicit-level', server: 'AgentTaskService.cs:773-778', context: { ...scenario({ level: 'Low' }), goal: 'CARD-1250 repair' }, expected: ['unknown', null, null, null, null] },
  { id: 'explicit-kind-unobserved', server: 'AgentTaskService.cs:924-945,4169-4204', context: { ...scenario(), explicitKind: 'Grok' }, expected: ['unknown', null, null, null, null], label: 'unknown — explicit agent kind routing depends on the server' },
  { id: 'standing-agent-explicit', server: 'AgentTaskService.cs:840-861', context: { ...scenario(), explicitAgentId: 'standing-agent' }, expected: ['unknown', null, null, null, null], label: 'unknown — standing-agent kind, model and admission depend on the server' },
  { id: 'agent-only-pin', server: 'AgentTaskService.cs:840-861', context: scenario({ pin: pin({ agentId: 'standing-agent' }) }), expected: ['agent', null, null, null, null], label: 'stage pin binds a standing agent; its model and admission depend on the server' },
  { id: 'agent-mixed-pin', server: 'AgentTaskService.cs:840-861', context: scenario({ pin: listed([astra], { agentId: 'standing-agent' }) }), expected: ['unknown', null, null, null, null] },
  { id: 'quota-observed', server: 'AgentTaskService.cs:1117-1127', context: scenario({ pin: listed([quota(astra)]) }), expected: ['refused', 'Codex', 'Frontier', 'gpt-6-astra', null] },
  { id: 'quota-unobserved', server: 'AgentTaskService.cs:1117-1127', context: scenario({ resolved: 'none', candidates: [] }), expected: ['rolePolicy', 'ClaudeCode', 'Frontier', 'fable', null], label: 'default → ClaudeCode/Frontier (fable); availability depends on the server' },
  { id: 'explicit-level', server: 'AgentTaskService.cs:4117-4135', context: scenario({ level: 'Low' }), expected: ['explicit', null, 'Low', null, null], label: 'explicit Low' },
  { id: 'orchestrator-floor', server: 'AgentTaskService.cs:4132-4133', context: scenario({ kind: 'Orchestrator', level: 'Low' }), expected: ['unknown', null, null, null, null] },
  { id: 'worker-kind', server: 'AgentTaskService.cs:924-945', context: scenario(), expected: ['matrix', 'Codex', 'Frontier', 'gpt-6-astra', 'Medium'] },
  { id: 'orchestrator-kind', server: 'AgentTaskService.cs:924-945,4190-4204', context: scenario({ kind: 'Orchestrator', candidates: [astra, fable] }), expected: ['rolePolicy', 'ClaudeCode', 'Frontier', 'fable', null] },
  ...(['Check', 'Distill', 'Diagnose'] as const).map((role): ParityRow => ({ id: 'specialist-' + role, server: 'AgentTaskCardBinder.cs:91-92; AgentTaskEnums.cs:431-432; AgentTaskService.cs:3740-3744', context: { ...scenario({ policy: row({ role }) }), goal: 'CARD-1250 repair' }, expected: ['rolePolicy', 'ClaudeCode', 'Frontier', 'fable', null] })),
]
describe('C1250 V22 create-context parity', () => {
  for (const { id, server, context, expected, label } of contextRows) {
    it(`${id} [${server}]`, () => {
      const preview = previewNextCreate(context)
      expect([preview?.source, preview?.agentKind, preview?.modelLevel, preview?.alias, preview?.complexity]).toEqual(expected)
      if (label) expect(nextCreateLabel(preview)).toBe(label)
    })
  }
})
