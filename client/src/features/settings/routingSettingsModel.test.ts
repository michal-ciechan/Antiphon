import { describe, expect, it } from 'vitest'
import type { ComplexityChainDto, ComplexityEffectiveDto } from '../../api/complexityChains'
import type { RoutingPinDto } from '../../api/routingPins'
import {
  CHAIN_MUTATION_EFFECT,
  canClearOverride,
  candidateAvailabilityLabel,
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
  pendingReadLabel,
  routingPinsReadLabel,
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
  it.each((['pin', 'pin+chain', 'role', 'any', 'config', 'rolePolicy', 'agent'] as const)
    .flatMap(source => (['Hard', 'Medium', 'Easy'] as const).map(tier => ({ source, tier }))))(
    'C1258 V-1 stage-pin failure $source/$tier', ({ source, tier }) => {
      expect(effectiveCellLabel(effective({
        source, stagePin, candidates: [codex], governsDefault: tier === 'Medium',
        withheldReason: 'stage pin read failed (InvalidOperationException)',
      }))).toBe('Effective routing unknown')
    },
  )

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
    ['role-with-read-failure', effective({ source: 'role', candidates: [opus], withheldReason: 'stage pin read failed (InvalidOperationException)' }), 'Effective routing unknown'],
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

describe('C1270 failed-read display', () => {
  const chainFailed = 'chain read failed (InvalidOperationException)'
  const metadataFailed = 'chain metadata read failed (InvalidOperationException)'
  const availabilityFailed = 'availability read failed (InvalidOperationException)'
  const agentId = '7f0c2d4e-0000-4000-8000-000000000001'
  const missingAgent = `stage pin binds standing agent ${agentId} (unavailable); an ordinary dispatch runs on that agent and does not walk this row`
  const namedAgent = 'stage pin binds standing agent Reviewer Bot (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row'
  const preferred = { ...stagePin, strength: 'Preferred' } as const
  const heldCodex = { ...codex, availableNow: false, unavailableReason: availabilityFailed }

  it.each([
    ['preferred-chain-read-failure-default', effective({ source: 'pin+chain', stagePin: preferred, candidates: [codex, opus], withheldReason: chainFailed }), 'Effective routing unknown'],
    ['preferred-chain-read-failure-non-default', effective({ source: 'pin+chain', stagePin: preferred, candidates: [codex], governsDefault: false, withheldReason: chainFailed }), 'Effective routing unknown'],
    ['preferred-chain-read-failure-after-policy-diagnostic', effective({ source: 'pin+chain', stagePin: preferred, candidates: [codex], withheldReason: policyReason + '; ' + chainFailed }), 'Effective routing unknown'],
    ['required-chain-read-failure-default', effective({ source: 'pin', stagePin, candidates: [codex], withheldReason: chainFailed }), 'Required Human pin → Codex/Frontier (gpt-6-astra)'],
    ['required-chain-read-failure-non-default', effective({ source: 'pin', stagePin, candidates: [codex], governsDefault: false, withheldReason: chainFailed }), 'Required Human pin → Codex/Frontier (gpt-6-astra)'],
    ['preferred-chain-marker-is-not-free-text', effective({ source: 'pin+chain', stagePin: preferred, candidates: [codex, grok], withheldReason: 'diagnostic mentions chain read failed (InvalidOperationException)' }), 'Preferred Human pin → Codex/Frontier (gpt-6-astra), then this row'],
    ['preferred-metadata-read-failure', effective({ source: 'pin+chain', stagePin: preferred, candidates: [codex, grok], withheldReason: metadataFailed }), 'Preferred Human pin → Codex/Frontier (gpt-6-astra), then this row'],
    ['required-metadata-read-failure', effective({ source: 'pin', stagePin, candidates: [codex], withheldReason: metadataFailed }), 'Required Human pin → Codex/Frontier (gpt-6-astra)'],
    ['preferred-availability-read-failure', effective({ source: 'pin+chain', stagePin: preferred, candidates: [heldCodex, grok], withheldReason: availabilityFailed }), 'Preferred Human pin → Codex/Frontier (gpt-6-astra), then this row'],
    ['required-availability-read-failure', effective({ source: 'pin', stagePin, candidates: [heldCodex], withheldReason: availabilityFailed }), 'Required Human pin → Codex/Frontier (gpt-6-astra)'],
    ['preferred-standing-agent-read-failure', effective({ source: 'agent', stagePin: preferred, governsDefault: false, withheldReason: missingAgent + '; standing agent read failed (InvalidOperationException)' }), `Pinned to standing agent ${agentId} (name unknown) — an ordinary dispatch runs there; this row is not walked`],
    ['required-standing-agent-read-failure', effective({ source: 'agent', stagePin, governsDefault: false, withheldReason: missingAgent + '; standing agent read failed (InvalidOperationException)' }), `Pinned to standing agent ${agentId} (name unknown) — an ordinary dispatch runs there; this row is not walked`],
    ['standing-agent-missing-is-not-a-read-failure', effective({ source: 'agent', stagePin, governsDefault: false, withheldReason: missingAgent }), `Pinned to standing agent ${agentId} (unavailable) — an ordinary dispatch runs there; this row is not walked`],
    ['required-standing-agent-with-chain-read-failure', effective({ source: 'agent', stagePin, governsDefault: false, withheldReason: namedAgent + '; ' + chainFailed }), 'Pinned to standing agent Reviewer Bot (ClaudeCode) — an ordinary dispatch runs there; this row is not walked'],
    ['preferred-standing-agent-with-chain-read-failure', effective({ source: 'agent', stagePin: preferred, governsDefault: false, withheldReason: namedAgent + '; ' + chainFailed }), 'Pinned to standing agent Reviewer Bot (ClaudeCode) — an ordinary dispatch runs there; this row is not walked'],
  ])('C1270 V-1 effective label %s', (_name, input, expected) => {
    expect(effectiveCellLabel(input as ComplexityEffectiveDto)).toBe(expected)
  })

  it.each([
    ['availability-read-failure', heldCodex, 'availability unknown: availability read failed (InvalidOperationException)'],
    ['availability-marker-is-not-free-text', { ...codex, availableNow: false, unavailableReason: 'probe says availability read failed (InvalidOperationException)' }, 'probe says availability read failed (InvalidOperationException)'],
    ['held', { ...codex, availableNow: false, unavailableReason: 'held until 2026-09-04T00:00:00Z (manual)' }, 'held until 2026-09-04T00:00:00Z (manual)'],
    ['no-reason', { ...codex, availableNow: false, unavailableReason: null }, 'unavailable'],
    ['available', codex, 'available'],
  ])('C1270 V-1 availability %s', (_name, candidate, expected) => {
    expect(candidateAvailabilityLabel(candidate as ComplexityChainDto['candidates'][number])).toBe(expected)
  })
})

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

// CARD-1270 D-1: React Query 5 reports an offline query with no data as pending and paused,
// with isLoading false; neither state is a value.
describe('C1270 D-1 pending reads', () => {
  it.each([
    { name: 'loading', state: { isPending: true, fetchStatus: 'fetching' }, expected: 'Model availability loading.' },
    { name: 'paused offline', state: { isPending: true, fetchStatus: 'paused' }, expected: 'Model availability unknown (read paused offline).' },
    { name: 'pending and idle', state: { isPending: true, fetchStatus: 'idle' }, expected: 'Model availability loading.' },
    { name: 'loaded and refetching', state: { isPending: false, fetchStatus: 'fetching' }, expected: null },
    { name: 'loaded and paused', state: { isPending: false, fetchStatus: 'paused' }, expected: null },
  ] as const)('C1270 D-1 pending read label $name', ({ state, expected }) => {
    expect(pendingReadLabel(state, 'Model availability')).toBe(expected)
  })

  it.each([
    { name: 'loading', state: { isPending: true, fetchStatus: 'fetching', error: null }, expected: { row: 'Stage and card pins loading.', list: 'Card-specific pins loading.' } },
    { name: 'paused offline', state: { isPending: true, fetchStatus: 'paused', error: null }, expected: { row: 'Stage and card pins unknown (routing pins read paused offline)', list: 'Card-specific pins unknown (routing pins read paused offline).' } },
    { name: 'failed', state: { isPending: false, fetchStatus: 'idle', error: new Error('boom') }, expected: { row: 'Stage and card pins unknown (routing pins read failed: boom)', list: 'Card-specific pins unknown (routing pins read failed: boom).' } },
    { name: 'loaded', state: { isPending: false, fetchStatus: 'idle', error: null }, expected: null },
    { name: 'loaded and paused', state: { isPending: false, fetchStatus: 'paused', error: null }, expected: null },
  ] as const)('C1270 D-1 routing pins read label $name', ({ state, expected }) => {
    expect(routingPinsReadLabel(state, 'boom')).toEqual(expected)
  })
})

describe('C1270 D-2 clear copy after a failed read', () => {
  const chainSegment = 'chain read failed (InvalidOperationException)'
  const metadataSegment = 'chain metadata read failed (InvalidOperationException)'

  it.each([
    { fallback: 'any', tail: 'Without an own rule this cell falls back to the Any role list.' },
    { fallback: 'config', tail: 'Without an own rule this cell falls back to the configuration default.' },
    { fallback: 'none', tail: 'Without an own rule this cell is Unset — a -Complexity dispatch for this role and tier will block until an operator sets a row.' },
  ] as const)('C1270 D-2 clear copy after a chain read failure falls back to $fallback', ({ fallback, tail }) => {
    const copy = clearOverrideCopy({
      isAnyRoleRow: false, role: 'Plan', complexity: 'Easy', fallbackResolvedFrom: fallback,
      chainUnknown: chainSegment, metadataUnknown: null,
    })
    expect(copy.title).toBe('Clear Plan/Easy override?')
    expect(copy.body).toBe(`Clearing Plan/Easy deletes this cell's own rule, with its reason and expiry, if one exists; its current value is unknown (${chainSegment}). ${tail}`)
    expect(copy.body).not.toMatch(/currently/)
    expect(copy.confirm).toBe('Confirm clear')
  })

  it.each([
    { fallback: 'any', head: 'Clearing this Plan/Easy override falls back to the Any role list. This cell currently replaces Any role as a whole; it does not append.' },
    { fallback: 'config', head: 'Clearing this Plan/Easy override falls back to the configuration default. This cell currently replaces the inherited list as a whole; it does not append.' },
    { fallback: 'none', head: 'Clearing this Plan/Easy override leaves this cell Unset — a -Complexity dispatch for this role and tier will block until an operator sets a row.' },
  ] as const)('C1270 D-2 clear copy after a chain metadata read failure names the unread reason and expiry ($fallback)', ({ fallback, head }) => {
    const copy = clearOverrideCopy({
      isAnyRoleRow: false, role: 'Plan', complexity: 'Easy', fallbackResolvedFrom: fallback,
      chainUnknown: null, metadataUnknown: metadataSegment,
    })
    expect(copy.body).toBe(`${head} The stored reason and expiry are unknown (${metadataSegment}); clearing deletes them with the row.`)
  })

  it('C1270 D-2 clear copy without a failed read is unchanged', () => {
    expect(clearOverrideCopy({
      isAnyRoleRow: false, role: 'Plan', complexity: 'Easy', fallbackResolvedFrom: 'any',
      chainUnknown: null, metadataUnknown: null,
    }).body).toBe('Clearing this Plan/Easy override falls back to the Any role list. This cell currently replaces Any role as a whole; it does not append.')
  })
})
