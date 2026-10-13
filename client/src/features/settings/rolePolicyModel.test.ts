import { describe, expect, it } from 'vitest'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinDto } from '../../api/routingPins'
import { defaultLabel, effectiveLabel, escalateLabel, timeoutLabel } from './rolePolicyModel'

function row(overrides: Partial<RolePolicyRowDto> = {}): RolePolicyRowDto {
  return {
    role: 'Docs',
    configured: true,
    level: 'Medium',
    kind: null,
    alias: 'sonnet',
    escalateTo: null,
    escalateAfterMinutes: null,
    timeoutMinutes: 240,
    stagePin: null,
    effective: {
      source: 'rolePolicy',
      agentKind: 'ClaudeCode',
      modelLevel: 'Medium',
      alias: 'sonnet',
      strength: null,
    },
    ...overrides,
  }
}

function stagePin(overrides: Partial<RoutingPinDto> = {}): RoutingPinDto {
  return {
    id: 'pin-docs',
    cardId: null,
    cardIdentifier: null,
    role: 'Docs',
    provenance: 'Human',
    strength: 'Required',
    agentKind: null,
    modelLevel: null,
    modelAlias: null,
    agentId: null,
    forbiddenAliases: [],
    notBefore: null,
    notAfter: null,
    reason: 'C097 model test',
    sourceTaskId: null,
    createdAt: '2026-10-13T16:47:00Z',
    updatedAt: '2026-10-13T16:47:00Z',
    candidates: [],
    candidateCount: 0,
    ...overrides,
  }
}

/** `%s` keeps the case name unquoted in the runtime title (`$name` would quote it). */
function named<T extends { name: string }>(cases: T[]): Array<[string, T]> {
  return cases.map((testCase) => [testCase.name, testCase])
}

describe('rolePolicyModel', () => {
  it.each(named([
    {
      name: 'null-kind-low',
      input: row({ configured: true, kind: null, level: 'Low', alias: 'haiku' }),
      expected: 'ClaudeCode/Low (haiku)',
    },
    {
      name: 'grok-frontier',
      input: row({ kind: 'Grok', level: 'Frontier', alias: 'grok-4.7' }),
      expected: 'Grok/Frontier (grok-4.7)',
    },
    {
      name: 'codex-medium',
      input: row({ kind: 'Codex', level: 'Medium', alias: 'gpt-6.1-sol' }),
      expected: 'Codex/Medium (gpt-6.1-sol)',
    },
    {
      name: 'unconfigured',
      input: row({ configured: false, kind: null, level: 'High', alias: 'opus' }),
      expected: 'not configured — DefaultLevel High (opus)',
    },
  ]))('C097 V05 %s', (_name, { input, expected }) => {
    expect(defaultLabel(input)).toBe(expected)
  })

  it.each(named([
    {
      name: 'pin-required-human',
      input: row({
        stagePin: stagePin({ provenance: 'Human', strength: 'Required' }),
        effective: {
          source: 'pin',
          agentKind: 'Codex',
          modelLevel: 'Frontier',
          alias: 'gpt-6-astra',
          strength: 'Required',
        },
      }),
      expected: 'Required Human pin → Codex/Frontier (gpt-6-astra)',
    },
    {
      name: 'pin-preferred-auto',
      input: row({
        stagePin: stagePin({ provenance: 'Auto', strength: 'Preferred' }),
        effective: {
          source: 'pin',
          agentKind: 'ClaudeCode',
          modelLevel: 'High',
          alias: 'opus',
          strength: 'Preferred',
        },
      }),
      expected: 'Preferred Auto pin → ClaudeCode/High (opus)',
    },
    {
      name: 'role-policy',
      input: row({
        effective: {
          source: 'rolePolicy',
          agentKind: 'ClaudeCode',
          modelLevel: 'Low',
          alias: 'haiku',
          strength: null,
        },
      }),
      expected: 'default → ClaudeCode/Low (haiku)',
    },
    {
      name: 'pin-without-candidates',
      input: row({
        stagePin: stagePin({ provenance: 'Human', strength: 'Required' }),
        effective: { source: 'pin', agentKind: null, modelLevel: null, alias: null, strength: 'Required' },
      }),
      expected: 'Required Human pin → no candidate (agent-only or forbid-only; see the pin)',
    },
    {
      name: 'unknown-source',
      input: row({
        stagePin: stagePin({ provenance: 'Human', strength: 'Required' }),
        effective: {
          source: 'future' as never,
          agentKind: 'ClaudeCode',
          modelLevel: 'High',
          alias: 'opus',
          strength: 'Required',
        },
      }),
      expected: 'unknown',
    },
  ]))('C097 V06 %s', (_name, { input, expected }) => {
    expect(effectiveLabel(input)).toBe(expected)
  })

  it.each(named([
    {
      name: 'escalate-manual',
      actual: () => escalateLabel(row({ escalateTo: 'High', escalateAfterMinutes: null })),
      expected: 'High (manual)',
    },
    {
      name: 'escalate-none',
      actual: () => escalateLabel(row({ escalateTo: null, escalateAfterMinutes: null })),
      expected: '—',
    },
    {
      name: 'escalate-timed',
      actual: () => escalateLabel(row({ escalateTo: 'Frontier', escalateAfterMinutes: 7 })),
      expected: 'Frontier after 7 min quiet',
    },
    {
      name: 'timeout-240',
      actual: () => timeoutLabel(row({ timeoutMinutes: 240 })),
      expected: '240 min',
    },
    {
      name: 'timeout-off',
      actual: () => timeoutLabel(row({ timeoutMinutes: 0 })),
      expected: 'off',
    },
  ]))('C097 V24 %s', (_name, { actual, expected }) => {
    expect(actual()).toBe(expected)
  })
})
