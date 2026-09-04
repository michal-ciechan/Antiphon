import { describe, expect, it } from 'vitest'
import type { ComplexityChainDto } from '../../api/complexityChains'
import {
  CHAIN_MUTATION_EFFECT,
  canClearOverride,
  candidateListError,
  cellEditorTitle,
  clearOverrideCopy,
  fallbackResolvedFromForRoleCell,
  isReplacingInheritedList,
  nextUnusedCandidate,
} from './routingSettingsModel'

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
