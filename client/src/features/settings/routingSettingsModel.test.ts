import { describe, expect, it } from 'vitest'
import type { ComplexityChainDto, ComplexityEffectiveDto } from '../../api/complexityChains'
import type { RoutingPinDto } from '../../api/routingPins'
import {
  CHAIN_MUTATION_EFFECT,
  canClearOverride,
  candidateListError,
  cellEditorTitle,
  clearOverrideCopy,
  fallbackResolvedFromForRoleCell,
  isReplacingInheritedList,
  nextUnusedCandidate,
  effectiveCellLabel,
  unsetCellLabel,
  nonComplexityBoundary,
  cardPinCountLabel,
} from './routingSettingsModel'

const opus = { agentKind: 'ClaudeCode', modelLevel: 'High', alias: 'opus', availableNow: true, unavailableReason: null } as const
const codex = { agentKind: 'Codex', modelLevel: 'Frontier', alias: 'gpt-6-astra', availableNow: true, unavailableReason: null } as const
const grok = { agentKind: 'Grok', modelLevel: 'Frontier', alias: 'grok-4.7', availableNow: true, unavailableReason: null } as const
const stagePin = {
  id: 'pin-plan', cardId: null, cardIdentifier: null, role: 'Plan', provenance: 'Human',
  strength: 'Required', agentKind: 'Codex', modelLevel: 'Frontier', notBefore: null,
  reason: 'operator choice', candidateCount: 1,
} as const
const policyReason = 'Plan policy names Raw/Frontier, which delegation cannot launch'
const agentReason = 'stage pin binds standing agent c097m-standing (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row'

function effective(overrides: Partial<ComplexityEffectiveDto>): ComplexityEffectiveDto {
  return { source: 'rolePolicy', candidates: [], stagePin: null, governsDefault: true, withheldReason: null, ...overrides }
}

describe('effective matrix cell', () => {
  it.each([
    ['required-pin', effective({ source: 'pin', stagePin, candidates: [codex] }), 'Required Human pin → Codex/Frontier (gpt-6-astra)'],
    ['preferred-pin', effective({ source: 'pin+chain', stagePin: { ...stagePin, strength: 'Preferred' }, candidates: [grok, opus] }), 'Preferred Human pin → Grok/Frontier (grok-4.7), then this row'],
    ['role-policy', effective({ candidates: [opus] }), 'Falls back to RolePolicy → ClaudeCode/High (opus)'],
    ['unlaunchable-policy', effective({ withheldReason: policyReason }), 'Falls back to RolePolicy → not launchable: Plan policy names Raw/Frontier, which delegation cannot launch'],
    ['standing-agent', effective({ source: 'agent', stagePin, governsDefault: false, withheldReason: agentReason }), 'Pinned to standing agent c097m-standing (ClaudeCode) — an ordinary dispatch runs there; this row is not walked'],
    ['own-rule', effective({ source: 'role', candidates: [opus] }), 'Own rule'],
    ['any-role', effective({ source: 'any', candidates: [opus] }), 'Inherits Any role'],
    ['configuration', effective({ source: 'config', candidates: [opus] }), 'Configuration fallback'],
    ['read-failure-without-target', effective({ withheldReason: 'chain read failed (InvalidOperationException)' }), 'Effective routing unknown'],
    ['read-failure-with-policy-target', effective({ candidates: [opus], withheldReason: 'chain read failed (InvalidOperationException)' }), 'Effective routing unknown'],
    ['default-pin-read-failure', effective({ candidates: [opus], withheldReason: 'stage pin read failed (InvalidOperationException)' }), 'Effective routing unknown'],
    ['empty-pin-read-failure', effective({ withheldReason: 'stage pin read failed (InvalidOperationException)' }), 'Effective routing unknown'],
    ['non-default-pin-read-failure', effective({ governsDefault: false, candidates: [opus], withheldReason: 'stage pin read failed (InvalidOperationException)' }), 'Effective routing unknown'],
    ['pin-read-failure-after-policy-diagnostic', effective({ withheldReason: 'Plan policy names Raw/Frontier, which delegation cannot launch; stage pin read failed (InvalidOperationException)' }), 'Effective routing unknown'],
    ['pin-read-marker-is-not-free-text', effective({ candidates: [opus], withheldReason: 'diagnostic mentions stage pin read failed (InvalidOperationException)' }), 'Falls back to RolePolicy → ClaudeCode/High (opus)'],
    ['preferred-with-unlaunchable-policy', effective({ source: 'pin+chain', stagePin: { ...stagePin, strength: 'Preferred' }, candidates: [grok], withheldReason: policyReason }), 'Preferred Human pin → Grok/Frontier (grok-4.7), then this row'],
    ['pin-with-read-failure', effective({ source: 'pin', stagePin, candidates: [codex], withheldReason: 'chain read failed (InvalidOperationException)' }), 'Required Human pin → Codex/Frontier (gpt-6-astra)'],
    ['role-with-read-failure', effective({ source: 'role', candidates: [opus], withheldReason: 'stage pin read failed (InvalidOperationException)' }), 'Own rule'],
    ['any-with-read-failure', effective({ source: 'any', candidates: [opus], withheldReason: 'chain metadata read failed (InvalidOperationException)' }), 'Inherits Any role'],
    ['config-with-read-failure', effective({ source: 'config', candidates: [opus], withheldReason: 'availability read failed (InvalidOperationException)' }), 'Configuration fallback'],
    ['agent-with-read-failure', effective({ source: 'agent', stagePin, governsDefault: false, withheldReason: agentReason + '; chain read failed (InvalidOperationException)' }), 'Pinned to standing agent c097m-standing (ClaudeCode) — an ordinary dispatch runs there; this row is not walked'],
  ])('C097M V-C1 %s', (_name, input, expected) => {
    expect(effectiveCellLabel(input as ComplexityEffectiveDto)).toBe(expected)
  })

  it.each([
    ['Easy', false, 'Unset — a -Complexity Easy dispatch blocks'],
    ['Hard', false, 'Unset — a -Complexity Hard dispatch blocks'],
    ['Medium', true, 'Unset — default dispatch falls back to RolePolicy'],
  ] as const)('C097M V-C2 unset %s', (tier, governsDefault, expected) => {
    expect(unsetCellLabel(tier, governsDefault)).toBe(expected)
  })

  it('C097M V-C2 ordinary-worker-boundary', () => {
    expect(nonComplexityBoundary('Medium')).toBe('An ordinary Worker dispatch without -Complexity, -Kind/-Level, -Agent or ignoreModelDisabled walks the Medium row; explicit asks, orchestrators, standing agents and specialist roles keep their existing routing path.')
    expect(nonComplexityBoundary('Hard')).toBe('An ordinary Worker dispatch without -Complexity, -Kind/-Level, -Agent or ignoreModelDisabled walks the Hard row; explicit asks, orchestrators, standing agents and specialist roles keep their existing routing path.')
  })

  it.each([
    [[], null],
    [[{ role: 'Plan', cardId: 'card-1' }], '1 card pin'],
    [[{ role: 'Plan', cardId: 'card-1' }, { role: 'Plan', cardId: 'card-2' }], '2 card pins'],
    [[{ role: 'Plan', cardId: null }, { role: 'Code', cardId: 'card-1' }], null],
  ])('C097M V-C3 card-pin-count %#', (pins, expected) => {
    expect(cardPinCountLabel('Plan', pins as RoutingPinDto[])).toBe(expected)
  })
})

function chain(overrides: Partial<ComplexityChainDto> = {}): ComplexityChainDto {
  return {
    complexity: 'Hard',
    candidates: [],
    provenance: null,
    source: 'config',
    reason: null,
    notAfter: null,
    updatedAt: null,
    role: null,
    resolvedFrom: 'none',
    ...overrides,
  }
}

describe('cell editor copy', () => {
  it('titles Any role and a named role distinctly', () => {
    expect(cellEditorTitle(null, 'Hard')).toBe('Configure Any role / Hard')
    expect(cellEditorTitle('Plan', 'Easy')).toBe('Configure Plan / Easy')
  })

  it('allows Clear only for an own matrix row', () => {
    expect(canClearOverride('any', true)).toBe(true)
    expect(canClearOverride('config', true)).toBe(false)
    expect(canClearOverride('none', true)).toBe(false)
    expect(canClearOverride('role', false)).toBe(true)
    expect(canClearOverride('any', false)).toBe(false)
    expect(canClearOverride('config', false)).toBe(false)
  })

  it('warns that saving an inherited cell replaces the list as a whole', () => {
    expect(isReplacingInheritedList('any', false)).toBe(true)
    expect(isReplacingInheritedList('config', false)).toBe(true)
    expect(isReplacingInheritedList('config', true)).toBe(true)
    expect(isReplacingInheritedList('any', true)).toBe(false)
    expect(isReplacingInheritedList('role', false)).toBe(false)
  })

  it('uses the stronger Any-role warning because it may remove many fallbacks', () => {
    const copy = clearOverrideCopy({
      isAnyRoleRow: true,
      role: null,
      complexity: 'Hard',
      fallbackResolvedFrom: 'none',
    })
    expect(copy.title).toMatch(/Any role Hard/)
    expect(copy.body).toMatch(/removes the fallback used by every role/)
    expect(copy.body).toMatch(/Unset/)
    expect(copy.body).toMatch(/block/)
    expect(copy.confirm).toBe('Confirm clear')
  })

  it('distinguishes fallback to Any role, configuration, and blocking unset', () => {
    expect(
      clearOverrideCopy({
        isAnyRoleRow: false,
        role: 'Plan',
        complexity: 'Hard',
        fallbackResolvedFrom: 'any',
      }).body,
    ).toMatch(/falls back to the Any role list/)

    expect(
      clearOverrideCopy({
        isAnyRoleRow: false,
        role: 'Code',
        complexity: 'Medium',
        fallbackResolvedFrom: 'config',
      }).body,
    ).toMatch(/falls back to the configuration default/)

    const blocking = clearOverrideCopy({
      isAnyRoleRow: false,
      role: 'Plan',
      complexity: 'Easy',
      fallbackResolvedFrom: 'none',
    })
    expect(blocking.body).toMatch(/Unset/)
    expect(blocking.body).toMatch(/block/)
  })

  it('reads the any-role row as the role-cell fallback after Clear', () => {
    expect(fallbackResolvedFromForRoleCell(undefined)).toBe('none')
    expect(fallbackResolvedFromForRoleCell(chain({ resolvedFrom: 'any' }))).toBe('any')
    expect(fallbackResolvedFromForRoleCell(chain({ resolvedFrom: 'config' }))).toBe('config')
  })

  it('states the D6 new/queued, not running boundary', () => {
    expect(CHAIN_MUTATION_EFFECT).toMatch(/New complexity-routed dispatches/)
    expect(CHAIN_MUTATION_EFFECT).toMatch(/queued/)
    expect(CHAIN_MUTATION_EFFECT).toMatch(/Running sessions keep the model they started with/)
  })
})

describe('candidate drafts', () => {
  it('rejects empty, oversized, and duplicate lists in the same words as the server', () => {
    expect(candidateListError([])).toMatch(/1 to 8 candidates/)
    expect(
      candidateListError(
        Array.from({ length: 9 }, (_, index) => ({
          agentKind: 'ClaudeCode',
          modelLevel: index === 0 ? 'Frontier' : `extra-${index}`,
        })),
      ),
    ).toMatch(/at most 8/)
    expect(
      candidateListError([
        { agentKind: 'Codex', modelLevel: 'Frontier' },
        { agentKind: 'Codex', modelLevel: 'Frontier' },
      ]),
    ).toBe('Duplicate candidate Codex/Frontier. A chain lists each pair once.')
  })

  it('picks the next unused kind/level pair without using availability aliases', () => {
    expect(nextUnusedCandidate([])).toEqual({ agentKind: 'ClaudeCode', modelLevel: 'Frontier' })
    expect(
      nextUnusedCandidate([{ agentKind: 'ClaudeCode', modelLevel: 'Frontier' }]),
    ).toEqual({ agentKind: 'ClaudeCode', modelLevel: 'High' })
  })
})
