import { describe, expect, it } from 'vitest'
import type { ComplexityCandidateDto, ComplexityChainDto, ComplexityChainListDto, ComplexityEffectiveDto, TaskComplexity } from '../../api/complexityChains'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinDto } from '../../api/routingPins'
import { nextCreateLabel, previewNextCreate } from './nextCreatePreview'

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

  it('C1250 V5 orchestrator-ignores-the-row-and-keeps-the-floor', () => {
    const docs = row({
      role: 'Docs',
      level: 'Medium',
      alias: 'sonnet',
      effective: { source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Medium', alias: 'sonnet', strength: null },
    })
    const preview = previewNextCreate({ kind: 'Orchestrator', explicitLevel: null, row: docs, cells: mediumRow })
    expect(preview).toMatchObject({ source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'High', complexity: null, floored: true })
    expect(nextCreateLabel(preview)).toBe('default → ClaudeCode/Medium (sonnet); Orchestrator floor → High')

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
    expect(nextCreateLabel(worker)).toBe(reason)

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
    expect(nextCreateLabel(orchestrator)).toBe('stage pin binds a standing agent; the create runs on that agent')
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
  ])('C1250 V8 %s-keeps-the-role-policy', (_name, list) => {
    const preview = previewNextCreate({ kind: 'Worker', explicitLevel: null, row: row(), cells: list })
    expect(preview).toMatchObject({ source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: 'Frontier', alias: 'fable', complexity: null })
    expect(nextCreateLabel(preview)).toBe('default → ClaudeCode/Frontier (fable)')
  })

  it('C1250 V9 required-stage-pin-walks-inside-the-row', () => {
    const stagePin = { ...pin(), candidates: undefined } as unknown as ComplexityEffectiveDto['stagePin']
    const preview = previewNextCreate({
      kind: 'Worker',
      explicitLevel: null,
      row: row(),
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
    expect(preview).toBeNull()
    expect(nextCreateLabel(preview)).toBe('unknown')
  })
})
