import { onlineManager } from '@tanstack/react-query'
import { cleanup } from '@testing-library/react'
import { HttpResponse, delay, http, type JsonBodyType } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AgentTaskRole } from '../../api/agentTasks'
import {
  complexityChainKeys,
  type ComplexityCandidateDto,
  type ComplexityChainDto,
  type ComplexityChainListDto,
  type ComplexityEffectiveDto,
  type ComplexityResolvedFrom,
  type TaskComplexity,
} from '../../api/complexityChains'
import { modelAvailabilityKeys, type ModelAvailabilityDto } from '../../api/modelAvailability'
import { rolePolicyKeys } from '../../api/rolePolicy'
import { runnerDefaultKeys } from '../../api/runnerDefaults'
import { sessionRunnerKeys } from '../../api/sessionRunners'
import { routingPinKeys, type RoutingPinDto } from '../../api/routingPins'
import { subscriptionUsageKeys, type SubscriptionUsageObservationDto } from '../../api/subscriptionUsage'
import { act, renderWithProviders, screen, userEvent, within, waitFor } from '../../test/utils'
import { ROLE_POLICY_FIXTURE } from '../../test/mocks/handlers'
import { server } from '../../test/mocks/server'
import { RoutingSettingsTab } from './RoutingSettingsTab'
import {
  CARD_PIN_SCOPE,
  COMPLEXITY_ROUTING_BOUNDARY_TITLE,
  USAGE_UNKNOWN,
} from './routingSettingsModel'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const grok: ComplexityCandidateDto = {
  agentKind: 'Grok',
  modelLevel: 'Frontier',
  alias: 'grok-4.7',
  availableNow: true,
  unavailableReason: null,
}

const codex: ComplexityCandidateDto = {
  agentKind: 'Codex',
  modelLevel: 'Frontier',
  alias: 'gpt-6-astra',
  availableNow: true,
  unavailableReason: null,
}

const heldFable: ComplexityCandidateDto = {
  agentKind: 'ClaudeCode',
  modelLevel: 'Frontier',
  alias: 'fable',
  availableNow: false,
  unavailableReason: 'held until 2026-09-04T00:00:00Z (manual)',
}

function chain(
  complexity: TaskComplexity,
  overrides: Partial<ComplexityChainDto> = {},
): ComplexityChainDto {
  return {
    complexity,
    candidates: [],
    provenance: null,
    source: 'config',
    reason: null,
    notAfter: null,
    updatedAt: null,
    role: null,
    resolvedFrom: 'none',
    effective: null,
    ...overrides,
  }
}

function pin(overrides: Partial<RoutingPinDto> = {}): RoutingPinDto {
  return {
    id: 'pin-1',
    cardId: null,
    cardIdentifier: null,
    role: 'Code',
    provenance: 'Human',
    strength: 'Required',
    agentKind: 'Grok',
    modelLevel: null,
    modelAlias: 'grok-4.7',
    agentId: null,
    forbiddenAliases: [],
    notBefore: null,
    notAfter: null,
    reason: 'stage-wide Code → Grok',
    sourceTaskId: null,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  }
}

const listDto: ComplexityChainListDto = {
  defaultComplexity: 'Medium',
  roles: ['Plan', 'Code'],
  complexities: ['Hard', 'Medium', 'Easy'],
  chains: [
    chain('Hard', {
      resolvedFrom: 'any',
      source: 'pin',
      provenance: 'Human',
      candidates: [grok],
      reason: 'any-role Hard',
      updatedAt: '2026-09-04T00:00:00Z',
    }),
    chain('Medium', {
      resolvedFrom: 'any',
      source: 'pin',
      provenance: 'Human',
      candidates: [grok],
      updatedAt: '2026-09-04T00:00:00Z',
    }),
    chain('Easy', { resolvedFrom: 'none' }),
    chain('Hard', {
      role: 'Plan',
      resolvedFrom: 'role',
      source: 'pin',
      provenance: 'Human',
      candidates: [codex],
      reason: 'Plan cell',
      updatedAt: '2026-09-04T00:00:00Z',
    }),
  ],
}

const planEffective: ComplexityChainListDto = {
  defaultComplexity: 'Medium',
  roles: ['Plan', 'Code'],
  complexities: ['Hard', 'Medium', 'Easy'],
  chains: [
    chain('Hard', {
      role: 'Plan',
      resolvedFrom: 'role',
      source: 'pin',
      provenance: 'Human',
      candidates: [codex],
      reason: 'Plan cell',
    }),
    chain('Medium', {
      role: 'Plan',
      resolvedFrom: 'any',
      source: 'pin',
      provenance: 'Human',
      candidates: [grok],
    }),
    chain('Easy', { role: 'Plan', resolvedFrom: 'none' }),
  ],
}

const codeEffective: ComplexityChainListDto = {
  defaultComplexity: 'Medium',
  roles: ['Plan', 'Code'],
  complexities: ['Hard', 'Medium', 'Easy'],
  chains: [
    chain('Hard', {
      role: 'Code',
      resolvedFrom: 'config',
      source: 'config',
      provenance: 'Auto',
      candidates: [heldFable, grok],
    }),
    chain('Medium', {
      role: 'Code',
      resolvedFrom: 'any',
      source: 'pin',
      provenance: 'Human',
      candidates: [grok],
    }),
    chain('Easy', { role: 'Code', resolvedFrom: 'none' }),
  ],
}

const defaultPins: RoutingPinDto[] = [
  pin(),
  pin({
    id: 'pin-plan',
    role: 'Plan',
    strength: 'Preferred',
    agentKind: 'ClaudeCode',
    modelLevel: 'Frontier',
    modelAlias: 'fable',
    reason: 'prefer Claude for Plan',
  }),
  pin({
    id: 'pin-card',
    cardId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    cardIdentifier: 'CARD-0332',
    role: 'Code',
    strength: 'Preferred',
    agentKind: 'Codex',
    modelLevel: 'Frontier',
    modelAlias: 'gpt-6-astra',
    reason: 'this card only',
  }),
]

const availabilityWithHolds: ModelAvailabilityDto = {
  available: ['opus', 'sonnet'],
  holds: [
    {
      id: 'hold-dated',
      kind: 'ClaudeCode',
      modelAlias: 'fable',
      source: 'Manual',
      disabledUntil: '2026-09-04T00:00:00Z',
      hitAt: '2026-09-01T12:00:00Z',
      reason: 'weekly cap',
      rawText: null,
      sourceSessionId: null,
      sourceTaskId: null,
    },
    {
      id: 'hold-open',
      kind: 'Grok',
      modelAlias: 'grok-4.7',
      source: 'AutoDetected',
      disabledUntil: null,
      hitAt: '2026-09-02T08:00:00Z',
      reason: 'usage wall',
      rawText: null,
      sourceSessionId: null,
      sourceTaskId: null,
    },
  ],
}

const fullUsage: SubscriptionUsageObservationDto[] = [
  {
    provider: 'Codex',
    planLabel: 'ChatGPT Plus',
    remainingPercent: 73.5,
    resetsAt: '2026-09-06T00:00:00Z',
    observedAt: '2026-09-04T12:00:00Z',
    age: '00:15:00',
  },
]

function serveRouting(options?: {
  list?: ComplexityChainListDto
  effective?: Partial<Record<AgentTaskRole, ComplexityChainListDto | 'error'>>
  pins?: RoutingPinDto[]
  usage?: SubscriptionUsageObservationDto[]
  availability?: ModelAvailabilityDto
}) {
  const list = options?.list ?? listDto
  const effective = options?.effective ?? { Plan: planEffective, Code: codeEffective }
  server.use(
    http.get('/api/complexity-chains', ({ request }) => {
      const role = new URL(request.url).searchParams.get('role') as AgentTaskRole | null
      if (!role) return HttpResponse.json(list)
      const row = effective[role]
      if (row === 'error') return HttpResponse.json({ title: 'boom' }, { status: 500 })
      if (row) return HttpResponse.json(row)
      return HttpResponse.json({
        defaultComplexity: 'Medium',
        roles: list.roles,
        complexities: list.complexities,
        chains: [
          chain('Hard', { role, resolvedFrom: 'none' as ComplexityResolvedFrom }),
          chain('Medium', { role, resolvedFrom: 'none' as ComplexityResolvedFrom }),
          chain('Easy', { role, resolvedFrom: 'none' as ComplexityResolvedFrom }),
        ],
      } satisfies ComplexityChainListDto)
    }),
    http.get('/api/routing-pins', () => HttpResponse.json({ pins: options?.pins ?? defaultPins })),
    http.get('/api/subscription-usage', () => HttpResponse.json(options?.usage ?? [])),
    http.get('/api/model-availability', () =>
      HttpResponse.json(options?.availability ?? availabilityWithHolds),
    ),
  )
}

const c1270AgentId = '7f0c2d4e-0000-4000-8000-000000000001'
const c1270Reads = ['none', 'stage pin', 'chain', 'chain metadata', 'availability', 'standing agent', 'routing pins', 'role policy'] as const
type C1270Read = typeof c1270Reads[number]
type C1270Strength = 'Preferred' | 'Required'
const c1270Failure = (part: string) => `${part} read failed (InvalidOperationException)`

// Mirrors ComplexityChainService.GetEffectiveAsync for a Plan cell with a stored own rule [Grok]
// and a stage pin to Codex/Frontier: each failed part falls back and appends its diagnostic.
function c1270Cell(read: C1270Read, strength: C1270Strength, tier: TaskComplexity): ComplexityChainDto {
  const governsDefault = tier === 'Medium'
  const agentPin = read === 'standing agent'
  const stagePin = {
    id: 'pin-plan', cardId: null, cardIdentifier: null, role: 'Plan', provenance: 'Human', strength,
    agentKind: agentPin ? null : 'Codex', modelLevel: agentPin ? null : 'Frontier', notBefore: null,
    reason: 'operator choice', candidateCount: agentPin ? 0 : 1,
  } as const
  const pinCandidate = read === 'availability'
    ? { ...codex, availableNow: false, unavailableReason: c1270Failure('availability') }
    : codex
  const pinned: Pick<ComplexityEffectiveDto, 'source' | 'candidates'> = strength === 'Required'
    ? { source: 'pin', candidates: [pinCandidate] }
    : { source: 'pin+chain', candidates: [pinCandidate, grok] }
  const stored: Partial<ComplexityChainDto> = {
    role: 'Plan', resolvedFrom: 'role', source: 'pin', provenance: 'Human', candidates: [grok],
    reason: 'Plan cell', notAfter: '2026-11-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
  }
  switch (read) {
    case 'stage pin':
      return chain(tier, { ...stored, effective: { source: 'role', candidates: [grok], stagePin: null, governsDefault, withheldReason: c1270Failure('stage pin') } })
    case 'chain':
      // The failed chain loads as resolvedFrom none: the default tier appends RolePolicy after a Preferred pin.
      return chain(tier, { role: 'Plan', resolvedFrom: 'none', effective: {
        source: pinned.source,
        candidates: strength === 'Required' ? [codex] : governsDefault ? [codex, heldFable] : [codex],
        stagePin, governsDefault, withheldReason: c1270Failure('chain'),
      } })
    case 'chain metadata':
      return chain(tier, { ...stored, reason: null, notAfter: null, updatedAt: null, effective: { ...pinned, stagePin, governsDefault, withheldReason: c1270Failure('chain metadata') } })
    case 'availability':
      return chain(tier, { ...stored, effective: { ...pinned, stagePin, governsDefault, withheldReason: c1270Failure('availability') } })
    case 'standing agent':
      return chain(tier, { ...stored, effective: {
        source: 'agent', candidates: [], stagePin, governsDefault: false,
        withheldReason: `stage pin binds standing agent ${c1270AgentId} (unavailable); an ordinary dispatch runs on that agent and does not walk this row; ${c1270Failure('standing agent')}`,
      } })
    default:
      return chain(tier, { ...stored, effective: { ...pinned, stagePin, governsDefault, withheldReason: null } })
  }
}

const c1270Effective = {
  Required: '1. Codex/Frontier (gpt-6-astra)',
  PreferredTail: '2. Grok/Frontier (grok-4.7)',
  stored: '1. Grok/Frontier (grok-4.7)',
  reason: 'Plan cell',
  expiry: 'Expires 2026-11-01T00:00:00Z',
  chainMetadata: 'Provenance, reason and expiry unknown: chain read failed (InvalidOperationException)',
  metadata: 'Reason and expiry unknown: chain metadata read failed (InvalidOperationException)',
  availability: 'availability unknown: availability read failed (InvalidOperationException)',
} as const
const c1270Label = {
  Required: 'Required Human pin → Codex/Frontier (gpt-6-astra)',
  Preferred: 'Preferred Human pin → Codex/Frontier (gpt-6-astra), then this row',
  unknown: 'Effective routing unknown',
  agent: `Pinned to standing agent ${c1270AgentId} (name unknown) — an ordinary dispatch runs there; this row is not walked`,
} as const

// Every displayed cell field x each failing read. `present` texts are whole elements in the cell;
// `absent` texts must not appear anywhere in it.
const c1270Expectations: Record<C1270Read, Record<C1270Strength, { label: string; present: string[]; absent: string[]; alert: string | null }>> = {
  'none': {
    Required: { label: c1270Label.Required, present: [c1270Effective.Required, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['unknown', 'read failed'], alert: null },
    Preferred: { label: c1270Label.Preferred, present: [c1270Effective.Required, c1270Effective.PreferredTail, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['unknown', 'read failed'], alert: null },
  },
  'stage pin': {
    Required: { label: c1270Label.unknown, present: ['Human', c1270Effective.reason, c1270Effective.expiry], absent: ['Own rule', 'Codex/Frontier', 'Grok/Frontier', 'Required Human pin', 'then this row', 'expiry unknown'], alert: c1270Failure('stage pin') },
    Preferred: { label: c1270Label.unknown, present: ['Human', c1270Effective.reason, c1270Effective.expiry], absent: ['Own rule', 'Codex/Frontier', 'Grok/Frontier', 'Preferred Human pin', 'then this row', 'expiry unknown'], alert: c1270Failure('stage pin') },
  },
  'chain': {
    Required: { label: c1270Label.Required, present: [c1270Effective.Required, 'Stored cell: unknown (read failed)', c1270Effective.chainMetadata], absent: ['Own rule', 'Grok/Frontier', 'ClaudeCode/Frontier', 'then this row', 'Plan cell', 'Expires', 'Stored cell: unset', 'dispatch blocks'], alert: c1270Failure('chain') },
    Preferred: { label: c1270Label.unknown, present: ['Stored cell: unknown (read failed)', c1270Effective.chainMetadata], absent: ['Preferred Human pin', 'Codex/Frontier', 'Grok/Frontier', 'ClaudeCode/Frontier', 'then this row', 'Own rule', 'Plan cell', 'Expires', 'Stored cell: unset', 'dispatch blocks'], alert: c1270Failure('chain') },
  },
  'chain metadata': {
    Required: { label: c1270Label.Required, present: [c1270Effective.Required, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.metadata], absent: ['Plan cell', 'Expires', 'Effective routing unknown', 'Stored cell: unknown'], alert: c1270Failure('chain metadata') },
    Preferred: { label: c1270Label.Preferred, present: [c1270Effective.Required, c1270Effective.PreferredTail, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.metadata], absent: ['Plan cell', 'Expires', 'Effective routing unknown', 'Stored cell: unknown'], alert: c1270Failure('chain metadata') },
  },
  'availability': {
    Required: { label: c1270Label.Required, present: [c1270Effective.Required, c1270Effective.availability, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['Effective routing unknown', 'expiry unknown'], alert: c1270Failure('availability') },
    Preferred: { label: c1270Label.Preferred, present: [c1270Effective.Required, c1270Effective.PreferredTail, c1270Effective.availability, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['Effective routing unknown', 'expiry unknown'], alert: c1270Failure('availability') },
  },
  'standing agent': {
    Required: { label: c1270Label.agent, present: ['Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['Codex/Frontier', 'Effective routing unknown', '(unavailable) —'], alert: c1270Failure('standing agent') },
    Preferred: { label: c1270Label.agent, present: ['Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['Codex/Frontier', 'Effective routing unknown', '(unavailable) —'], alert: c1270Failure('standing agent') },
  },
  'routing pins': {
    Required: { label: c1270Label.Required, present: [c1270Effective.Required, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['unknown', 'read failed'], alert: null },
    Preferred: { label: c1270Label.Preferred, present: [c1270Effective.Required, c1270Effective.PreferredTail, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['unknown', 'read failed'], alert: null },
  },
  'role policy': {
    Required: { label: c1270Label.Required, present: [c1270Effective.Required, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['unknown', 'read failed'], alert: null },
    Preferred: { label: c1270Label.Preferred, present: [c1270Effective.Required, c1270Effective.PreferredTail, 'Own rule', 'Human', c1270Effective.stored, c1270Effective.reason, c1270Effective.expiry], absent: ['unknown', 'read failed'], alert: null },
  },
}

describe('C1270 failed-read matrix display', () => {
  it.each(c1270Reads.flatMap(read => (['Preferred', 'Required'] as const).flatMap(strength =>
    (['Medium', 'Easy'] as const).map(tier => ({ read, strength, tier })))))(
    'C1270 V-2 $read read failure with a $strength pin on $tier', async ({ read, strength, tier }) => {
      const expected = c1270Expectations[read][strength]
      const stagePin = pin({
        id: 'pin-plan', role: 'Plan', strength,
        agentKind: read === 'standing agent' ? null : 'Codex',
        modelLevel: read === 'standing agent' ? null : 'Frontier',
        modelAlias: read === 'standing agent' ? null : 'gpt-6-astra',
        agentId: read === 'standing agent' ? c1270AgentId : null,
      })
      serveRouting({ pins: [stagePin], effective: { Plan: { ...planEffective, chains: [c1270Cell(read, strength, tier)] } } })
      if (read === 'routing pins') server.use(http.get('/api/routing-pins', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
      if (read === 'role policy') server.use(http.get('/api/role-policy', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
      renderWithProviders(<RoutingSettingsTab />)

      const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
      expect(await within(cell).findByText(expected.label)).toBeInTheDocument()
      for (const text of expected.present) expect(within(cell).getByText(text)).toBeInTheDocument()
      for (const text of expected.absent) expect(cell).not.toHaveTextContent(text)
      if (expected.alert) expect(within(cell).getByRole('alert')).toHaveTextContent(expected.alert)
      else expect(within(cell).queryByRole('alert')).not.toBeInTheDocument()
      expect(within(cell).getByRole('button', { name: `Configure Plan / ${tier}` })).toBeEnabled()

      const row = cell.closest('tr')!
      const target = read === 'standing agent' ? 'the pinned target' : 'Codex/Frontier'
      const banner = strength === 'Required'
        ? `Required Human pin: Plan → ${target}. Overrides the Plan cells while this pin is active.`
        : `Preferred Human pin: Plan → ${target}. Tried before each Plan cell.`
      if (read === 'routing pins') {
        expect(await screen.findByText('Could not load routing pins. Matrix cells still reflect the chain service.')).toBeInTheDocument()
        expect(row).not.toHaveTextContent(banner)
      } else {
        expect(await within(row).findByText(banner)).toBeInTheDocument()
      }
      if (read === 'role policy') {
        expect(await within(screen.getByTestId('role-policy-section')).findByText('Could not load role policy.')).toBeInTheDocument()
      }
    },
  )
})

// The server nulls reason/expiry when the chain or metadata read fails. These fixtures keep a stored
// reason beside the failure so the display guard, not the fixture, decides what is hidden.
function c1270StoredReasonCell(read: 'chain' | 'chain metadata', tier: TaskComplexity): ComplexityChainDto {
  return { ...c1270Cell(read, 'Required', tier), reason: 'Plan cell', notAfter: '2026-11-01T00:00:00Z' }
}

const c1270AgentName = 'stage pin binds standing agent Reviewer Bot (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row'

describe('C1270 failed-read reachable surfaces', () => {
  it.each((['chain', 'chain metadata'] as const).flatMap(read => (['Medium', 'Easy'] as const).map(tier => ({ read, tier }))))(
    'C1270 V-2 $read read failure beside a stored reason on $tier', async ({ read, tier }) => {
      serveRouting({ effective: { Plan: { ...planEffective, chains: [c1270StoredReasonCell(read, tier)] } } })
      renderWithProviders(<RoutingSettingsTab />)

      const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
      expect(await within(cell).findByText(read === 'chain' ? c1270Effective.chainMetadata : c1270Effective.metadata)).toBeInTheDocument()
      expect(cell).not.toHaveTextContent('Plan cell')
      expect(cell).not.toHaveTextContent('Expires')
    },
  )

  it.each(['Preferred', 'Required'] as const)(
    'C1270 V-2 standing agent pin with a chain read failure keeps the agent route with a $strength pin', async (strength) => {
      serveRouting({
        pins: [pin({ id: 'pin-plan', role: 'Plan', strength, agentKind: null, modelLevel: null, modelAlias: null, agentId: c1270AgentId })],
        effective: { Plan: { ...planEffective, chains: [chain('Easy', { role: 'Plan', resolvedFrom: 'none', effective: {
          source: 'agent', candidates: [], governsDefault: false,
          stagePin: { id: 'pin-plan', cardId: null, cardIdentifier: null, role: 'Plan', provenance: 'Human', strength, agentKind: null, modelLevel: null, notBefore: null, reason: 'operator choice', candidateCount: 0 },
          withheldReason: `${c1270AgentName}; ${c1270Failure('chain')}`,
        } })] } },
      })
      renderWithProviders(<RoutingSettingsTab />)

      const cell = await screen.findByTestId('routing-matrix-cell-Plan-Easy')
      expect(await within(cell).findByText('Pinned to standing agent Reviewer Bot (ClaudeCode) — an ordinary dispatch runs there; this row is not walked')).toBeInTheDocument()
      expect(within(cell).getByText('Stored cell: unknown (read failed)')).toBeInTheDocument()
      expect(within(cell).getByText(c1270Effective.chainMetadata)).toBeInTheDocument()
      expect(within(cell).getByRole('alert')).toHaveTextContent(c1270Failure('chain'))
      for (const text of ['Effective routing unknown', 'Own rule', 'Grok/Frontier', 'dispatch blocks', 'Stored cell: unset']) expect(cell).not.toHaveTextContent(text)
    },
  )
})

const c1270EditorNote = {
  chain: 'Current value unknown (chain read failed (InvalidOperationException)). The editor starts empty; it does not show the stored candidates, reason or expiry.',
  metadata: 'Stored reason and expiry unknown (chain metadata read failed (InvalidOperationException)). The Reason and Expires fields start empty; they do not show the stored values.',
} as const
const c1270EditorConfirm = {
  chain: 'Overwrite the unread stored cell. Save or Clear replaces its candidates, reason and expiry as a whole.',
  metadata: 'Overwrite the unread reason and expiry. Save writes the Reason and Expires entered here; an empty reason stores the default label and an empty expiry stores none.',
} as const

const c1270EditorCases = [
  ...(['none', 'stage pin', 'availability', 'standing agent'] as const).map(read => ({ read, label: read, cell: (tier: TaskComplexity) => c1270Cell(read, 'Required', tier) })),
  { read: 'chain' as const, label: 'chain', cell: (tier: TaskComplexity) => c1270Cell('chain', 'Required', tier) },
  { read: 'chain' as const, label: 'chain beside a stored reason', cell: (tier: TaskComplexity) => c1270StoredReasonCell('chain', tier) },
  { read: 'chain metadata' as const, label: 'chain metadata', cell: (tier: TaskComplexity) => c1270Cell('chain metadata', 'Required', tier) },
  { read: 'chain metadata' as const, label: 'chain metadata beside a stored reason', cell: (tier: TaskComplexity) => c1270StoredReasonCell('chain metadata', tier) },
]

describe('C1270 cell editor after a failed read', () => {
  it.each(c1270EditorCases.flatMap(c => (['Medium', 'Easy'] as const).map(tier => ({ ...c, tier }))))(
    'C1270 V-2 editor after a $label read failure on $tier', async ({ read, cell: makeCell, tier }) => {
      const put = vi.fn()
      serveRouting({ effective: { Plan: { ...planEffective, chains: [makeCell(tier)] } } })
      server.use(http.put('/api/complexity-chains/:role/:complexity', async ({ params, request }) => {
        put(`${params.role}/${params.complexity}`, await request.json())
        return HttpResponse.json(chain(tier, { role: 'Plan' }))
      }))
      renderWithProviders(<RoutingSettingsTab />)

      const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
      await userEvent.click(within(cell).getByRole('button', { name: `Configure Plan / ${tier}` }))
      const editor = await screen.findByTestId('routing-cell-editor')
      const reason = within(editor).getByLabelText('Reason (optional)')
      const expires = within(editor).getByLabelText('Expires (optional)')
      const save = within(editor).getByRole('button', { name: 'Save' })
      const clear = within(editor).getByRole('button', { name: 'Clear override' })
      expect(editor).not.toHaveTextContent('This cell is currently Unset')

      if (read === 'chain') {
        expect(within(editor).getByText(c1270EditorNote.chain)).toBeInTheDocument()
        expect(within(editor).queryByTestId('routing-cell-editor-candidate-0')).not.toBeInTheDocument()
        expect(reason).toHaveValue('')
        expect(expires).toHaveValue('')
        const confirm = within(editor).getByRole('checkbox', { name: c1270EditorConfirm.chain })
        expect(confirm).not.toBeChecked()
        expect(save).toBeDisabled()
        expect(clear).toBeDisabled()
        await userEvent.click(within(editor).getByRole('button', { name: 'Add candidate' }))
        await userEvent.type(reason, 'rewrite')
        expect(save).toBeDisabled()
        await userEvent.click(confirm)
        expect(clear).toBeEnabled()
        expect(save).toBeEnabled()
        await userEvent.click(save)
        await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
        expect(put.mock.calls[0][0]).toBe(`Plan/${tier}`)
        expect(put.mock.calls[0][1]).toMatchObject({ provenance: 'Human', reason: 'rewrite', notAfter: null })
        expect(put.mock.calls[0][1].candidates).toHaveLength(1)
      } else if (read === 'chain metadata') {
        expect(within(editor).getByText(c1270EditorNote.metadata)).toBeInTheDocument()
        expect(within(editor).getByTestId('routing-cell-editor-candidate-0')).toHaveAttribute('data-agent-kind', 'Grok')
        expect(reason).toHaveValue('')
        expect(expires).toHaveValue('')
        const confirm = within(editor).getByRole('checkbox', { name: c1270EditorConfirm.metadata })
        expect(confirm).not.toBeChecked()
        expect(save).toBeDisabled()
        expect(clear).toBeEnabled()
        await userEvent.click(confirm)
        expect(save).toBeEnabled()
        await userEvent.click(save)
        await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
        expect(put).toHaveBeenCalledWith(`Plan/${tier}`, {
          candidates: [{ agentKind: 'Grok', modelLevel: 'Frontier' }], provenance: 'Human', reason: null, notAfter: null,
        })
      } else {
        expect(editor).not.toHaveTextContent('unknown')
        expect(within(editor).queryByRole('checkbox')).not.toBeInTheDocument()
        expect(within(editor).getByTestId('routing-cell-editor-candidate-0')).toHaveAttribute('data-agent-kind', 'Grok')
        expect(reason).toHaveValue('Plan cell')
        expect(expires).not.toHaveValue('')
        expect(save).toBeEnabled()
        expect(clear).toBeEnabled()
        expect(put).not.toHaveBeenCalled()
      }
    },
  )
})

describe('C1270 routing pins read state', () => {
  const unknownRow = 'Stage and card pins unknown (routing pins read failed: boom)'
  const unknownList = 'Card-specific pins unknown (routing pins read failed: boom).'

  it.each(['failed', 'loading', 'stale after a failed refetch'] as const)(
    'C1270 V-2 routing pins %s', async (state) => {
      serveRouting()
      if (state === 'failed') server.use(http.get('/api/routing-pins', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
      if (state === 'loading') server.use(http.get('/api/routing-pins', async () => { await delay('infinite'); return HttpResponse.json({ pins: [] }) }))
      const { queryClient } = renderWithProviders(<RoutingSettingsTab />)
      const code = await screen.findByTestId('routing-matrix-row-Code')
      if (state === 'stale after a failed refetch') {
        expect(await within(code).findByText('1 card pin')).toBeInTheDocument()
        expect(within(code).getByTestId('routing-pin-banner-Code')).toBeInTheDocument()
        server.use(http.get('/api/routing-pins', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
        await act(async () => { await queryClient.invalidateQueries({ queryKey: routingPinKeys.all }) })
      }

      const cardPins = screen.getByTestId('routing-card-pins')
      if (state === 'loading') {
        expect(await within(code).findByText('Stage and card pins loading.')).toBeInTheDocument()
        expect(within(cardPins).getByText('Card-specific pins loading.')).toBeInTheDocument()
      } else {
        expect(await within(code).findByText(unknownRow)).toBeInTheDocument()
        expect(within(screen.getByTestId('routing-card-pins')).getByText(unknownList)).toBeInTheDocument()
        expect(screen.getByText('Could not load routing pins. Matrix cells still reflect the chain service.')).toBeInTheDocument()
      }
      expect(screen.getByTestId('routing-card-pins')).not.toHaveTextContent('No card-specific pins')
      expect(screen.getByTestId('routing-card-pins')).not.toHaveTextContent('CARD-0332')
      expect(within(code).queryByTestId('routing-pin-banner-Code')).not.toBeInTheDocument()
      expect(code).not.toHaveTextContent('1 card pin')
      expect(screen.getByTestId('routing-matrix-row-Plan')).not.toHaveTextContent('Preferred Human pin: Plan')
    },
  )
})

// CARD-1270 D-1: every read of the tab x every query state. React Query 5 reports an offline
// query with no data as pending and paused (isLoading false, no data, no error). Only a
// successful read, or the last success while a refetch is in flight, may show a value or an
// absence; every other state shows its own loading, paused or failed text.
const c1270D1States = ['loading', 'paused', 'failed', 'refetch failed', 'fetching with stale data', 'success empty', 'success'] as const
type C1270D1State = typeof c1270D1States[number]

interface C1270D1Read {
  url: string
  /** `/api/complexity-chains` serves the list (role null) and each role row (role set). */
  role?: AgentTaskRole | null
  key: readonly unknown[]
  surface: string
  populated: JsonBodyType
  /** Null when the read has no empty success. */
  empty: JsonBodyType
  loading: string
  paused: string
  failed: string
  /** Texts or display values only a successful populated read shows. */
  shown: string[]
  /** The definite absence a successful empty read shows; null when the read has none. */
  emptyShown: string | null
  configure?: boolean
}

const c1270D1Runner = (runnerId: string, platform: string) => ({
  runnerId, displayName: runnerId, platform, platformObservedAt: '2026-09-25T00:00:00Z', available: true,
  dispatchEligible: true, unavailableReason: null, capacity: 4, occupied: 0, capacityKind: 'sessions',
  capacityObservedAt: '2026-09-25T00:00:00Z', stale: false, features: [],
})

const c1270D1Reads: Record<string, C1270D1Read> = {
  'complexity chains list': {
    url: '/api/complexity-chains', role: null, key: complexityChainKeys.list(), surface: 'routing-matrix-section',
    populated: listDto, empty: { ...listDto, roles: [], chains: [] },
    loading: 'Complexity chains loading.', paused: 'Complexity chains unknown (read paused offline).',
    failed: 'Could not load complexity chains.',
    shown: ['Any role rule'], emptyShown: 'Unset — a -Complexity Hard dispatch blocks', configure: true,
  },
  'Plan effective cells': {
    url: '/api/complexity-chains', role: 'Plan', key: complexityChainKeys.effective('Plan'), surface: 'routing-matrix-row-Plan',
    populated: planEffective,
    empty: { ...planEffective, chains: (['Hard', 'Medium', 'Easy'] as const).map(tier => chain(tier, { role: 'Plan' })) },
    loading: 'Plan effective cells loading.', paused: 'Plan effective cells unknown (read paused offline).',
    failed: 'Could not load Plan effective cells.',
    shown: ['Own rule', 'Inherits Any role'], emptyShown: 'Unset — a -Complexity Hard dispatch blocks', configure: true,
  },
  'routing pins': {
    url: '/api/routing-pins', key: routingPinKeys.all, surface: 'routing-card-pins',
    populated: { pins: defaultPins }, empty: { pins: [] },
    loading: 'Card-specific pins loading.', paused: 'Card-specific pins unknown (routing pins read paused offline).',
    failed: 'Card-specific pins unknown (routing pins read failed: boom).',
    shown: ['CARD-0332'], emptyShown: 'No card-specific pins.',
  },
  'model availability': {
    url: '/api/model-availability', key: modelAvailabilityKeys.all, surface: 'routing-availability-section',
    populated: availabilityWithHolds, empty: { available: [], holds: [] },
    loading: 'Model availability loading.', paused: 'Model availability unknown (read paused offline).',
    failed: 'Could not load model availability.',
    shown: ['weekly cap', 'Held'], emptyShown: 'This snapshot has no available aliases and no holds.',
  },
  'subscription usage': {
    url: '/api/subscription-usage', key: subscriptionUsageKeys.all, surface: 'routing-usage-section',
    populated: fullUsage, empty: [],
    loading: 'Subscription usage loading.', paused: 'Subscription usage unknown (read paused offline).',
    failed: 'Could not load subscription usage observations.',
    shown: ['ChatGPT Plus'], emptyShown: USAGE_UNKNOWN,
  },
  'role policy': {
    url: '/api/role-policy', key: rolePolicyKeys.all, surface: 'role-policy-section',
    populated: ROLE_POLICY_FIXTURE, empty: { ...ROLE_POLICY_FIXTURE, roles: [] },
    loading: 'Role policy loading.', paused: 'Role policy unknown (read paused offline).',
    failed: 'Could not load role policy.',
    shown: ['Investigate'], emptyShown: null,
  },
  'runner defaults': {
    url: '/api/runner-defaults', key: runnerDefaultKeys.current, surface: 'runner-defaults-section',
    populated: {
      revision: 1, globalRunnerId: 'server2', kindDefaults: [], updatedAt: '2026-09-25T00:00:00Z',
      lastReason: 'Imported Delegation:DefaultRunnerId.', lastProvenance: 'Migration', lastCallerTaskId: null,
      supportedKinds: ['Grok', 'ClaudeCode', 'Codex'], unresolvedReferences: [],
    },
    empty: null,
    loading: 'Runner defaults loading.', paused: 'Runner defaults unknown (read paused offline).',
    failed: 'Could not load runner defaults. Model routing above is unchanged.',
    shown: ['Revision 1.'], emptyShown: null,
  },
  'runner default revisions': {
    url: '/api/runner-defaults/revisions', key: runnerDefaultKeys.revisions, surface: 'runner-defaults-history',
    populated: {
      revisions: [{ revision: 2, previousRevision: 1, globalRunnerId: null, kindDefaults: [], createdAt: '2026-09-25T00:00:00Z', reason: 'earlier', provenance: 'Human', callerTaskId: null }],
      nextBeforeRevision: null,
    },
    empty: { revisions: [], nextBeforeRevision: null },
    loading: 'History loading.', paused: 'History unknown (read paused offline).',
    failed: 'History unknown (revisions read failed: boom).',
    shown: ['earlier', 'Restore as new revision'], emptyShown: 'No revisions yet.',
  },
  'session runners': {
    url: '/api/session-runners', key: sessionRunnerKeys.all, surface: 'runner-defaults-section',
    populated: [c1270D1Runner('desktop', 'windows'), c1270D1Runner('server2', 'linux')], empty: [],
    loading: 'Runner catalogue loading.', paused: 'Runner catalogue unknown (read paused offline).',
    failed: 'Runner catalogue unknown (session runners read failed: boom).',
    shown: ['server2 · linux'], emptyShown: 'server2 · missing from catalogue',
  },
}

function c1270D1Handler(read: C1270D1Read, respond: () => Response | Promise<Response>) {
  return http.get(read.url, ({ request }) => {
    // Falls through to the next handler for the other complexity-chains read.
    if (read.role !== undefined && new URL(request.url).searchParams.get('role') !== read.role) return undefined
    return respond()
  })
}

const c1270D1Respond = {
  populated: (read: C1270D1Read) => () => HttpResponse.json(read.populated),
  empty: (read: C1270D1Read) => () => HttpResponse.json(read.empty),
  failed: () => () => HttpResponse.json({ title: 'boom' }, { status: 500 }),
  infinite: (read: C1270D1Read) => async () => { await delay('infinite'); return HttpResponse.json(read.populated) },
}

function c1270D1Present(surface: HTMLElement, text: string): boolean {
  return (surface.textContent ?? '').includes(text) || within(surface).queryAllByDisplayValue(text).length > 0
}

describe('C1270 D-1 read state table', () => {
  afterEach(() => {
    cleanup()
    onlineManager.setOnline(true)
  })

  it.each(Object.keys(c1270D1Reads).flatMap(read => c1270D1States
    .filter(state => state !== 'success empty' || c1270D1Reads[read].empty !== null)
    .map(state => ({ read, state }))))(
    'C1270 D-1 $read read $state', async ({ read: name, state }: { read: string; state: C1270D1State }) => {
      const read = c1270D1Reads[name]
      serveRouting({ usage: fullUsage })
      // msw checks the first listed handler first, so the read under test wins over the revisions default.
      server.use(
        c1270D1Handler(read, state === 'loading' ? c1270D1Respond.infinite(read)
          : state === 'failed' ? c1270D1Respond.failed()
            : state === 'success empty' ? c1270D1Respond.empty(read)
              : c1270D1Respond.populated(read)),
        http.get('/api/runner-defaults/revisions', c1270D1Respond.populated(c1270D1Reads['runner default revisions'])),
      )
      const { queryClient } = renderWithProviders(<RoutingSettingsTab />)

      if (state === 'paused' || state === 'refetch failed' || state === 'fetching with stale data') {
        await waitFor(() => expect(c1270D1Present(screen.getByTestId(read.surface), read.shown[0])).toBe(true))
        if (state === 'paused') {
          act(() => {
            onlineManager.setOnline(false)
            void queryClient.resetQueries({ queryKey: read.key, exact: true })
          })
        } else if (state === 'refetch failed') {
          server.use(c1270D1Handler(read, c1270D1Respond.failed()))
          await act(async () => { await queryClient.refetchQueries({ queryKey: read.key, exact: true }) })
        } else {
          server.use(c1270D1Handler(read, c1270D1Respond.infinite(read)))
          act(() => { void queryClient.refetchQueries({ queryKey: read.key, exact: true }) })
        }
      }

      // The query reached the named state.
      await waitFor(() => {
        const query = queryClient.getQueryState(read.key)
        expect(query).toBeDefined()
        expect({ status: query?.status, fetchStatus: query?.fetchStatus, hasData: query?.data !== undefined }).toEqual({
          loading: { status: 'pending', fetchStatus: 'fetching', hasData: false },
          paused: { status: 'pending', fetchStatus: 'paused', hasData: false },
          failed: { status: 'error', fetchStatus: 'idle', hasData: false },
          'refetch failed': { status: 'error', fetchStatus: 'idle', hasData: true },
          'fetching with stale data': { status: 'success', fetchStatus: 'fetching', hasData: true },
          'success empty': { status: 'success', fetchStatus: 'idle', hasData: true },
          success: { status: 'success', fetchStatus: 'idle', hasData: true },
        }[state])
      })

      const expected: string | null = state === 'loading' ? read.loading
        : state === 'paused' ? read.paused
          : state === 'failed' || state === 'refetch failed' ? read.failed
            : null
      const valueShown = state === 'success' || state === 'fetching with stale data'
      await waitFor(() => {
        const current = screen.getByTestId(read.surface)
        for (const label of [read.loading, read.paused, read.failed]) expect(c1270D1Present(current, label)).toBe(label === expected)
        if (valueShown) expect(c1270D1Present(current, read.shown[0])).toBe(true)
        if (state === 'success empty' && read.emptyShown) expect(c1270D1Present(current, read.emptyShown)).toBe(true)
      })
      const surface = screen.getByTestId(read.surface)
      for (const text of read.shown) expect(c1270D1Present(surface, text)).toBe(valueShown)
      if (read.emptyShown) expect(c1270D1Present(surface, read.emptyShown)).toBe(state === 'success empty')
      if (read.configure) {
        expect(within(surface).queryAllByRole('button', { name: /^Configure / }).length > 0).toBe(expected === null)
      }
    },
  )
})

describe('C1270 D-1 offline reads and the cell editor', () => {
  afterEach(() => {
    cleanup()
    onlineManager.setOnline(true)
  })

  it('C1270 D-1 offline from the first render shows no value and sends no PUT after reconnect', async () => {
    const write = vi.fn()
    serveRouting({ usage: fullUsage })
    server.use(
      http.put('/api/complexity-chains/:role/:complexity', ({ params }) => { write(`PUT ${params.role}/${params.complexity}`); return HttpResponse.json(chain('Hard')) }),
      http.delete('/api/complexity-chains/:role/:complexity', ({ params }) => { write(`DELETE ${params.role}/${params.complexity}`); return new HttpResponse(null, { status: 204 }) }),
    )
    onlineManager.setOnline(false)
    renderWithProviders(<RoutingSettingsTab />)

    for (const [testId, text] of [
      ['routing-availability-section', 'Model availability unknown (read paused offline).'],
      ['routing-usage-section', 'Subscription usage unknown (read paused offline).'],
      ['role-policy-section', 'Role policy unknown (read paused offline).'],
      ['runner-defaults-section', 'Runner defaults unknown (read paused offline).'],
      ['runner-defaults-section', 'Runner catalogue unknown (read paused offline).'],
      ['routing-matrix-section', 'Complexity chains unknown (read paused offline).'],
    ] as const) {
      expect(await screen.findByTestId(testId)).toHaveTextContent(text)
    }
    const tab = screen.getByTestId('routing-settings-tab')
    for (const text of ['Unset', 'dispatch blocks', 'No card-specific pins', 'no available aliases', 'No revisions yet', 'missing from catalogue', USAGE_UNKNOWN, 'Could not load', 'Any role rule']) {
      expect(tab).not.toHaveTextContent(text)
    }
    expect(screen.queryByTestId('routing-matrix-row-any')).not.toBeInTheDocument()
    expect(screen.queryAllByRole('button', { name: /^Configure / })).toHaveLength(0)
    expect(screen.queryByTestId('routing-cell-editor')).not.toBeInTheDocument()

    act(() => { onlineManager.setOnline(true) })
    expect(await within(await screen.findByTestId('routing-matrix-cell-Plan-Hard')).findByText('Own rule')).toBeInTheDocument()
    expect(within(screen.getByTestId('routing-matrix-cell-any-Hard')).getByText('Any role rule')).toBeInTheDocument()
    expect(write).not.toHaveBeenCalled()
  })

  it.each(['Save', 'Clear override'] as const)(
    'C1270 D-1 editor whose cell read returns to pending blocks %s until the read loads', async (action) => {
      const write = vi.fn()
      serveRouting()
      server.use(
        http.put('/api/complexity-chains/:role/:complexity', ({ params }) => { write(`PUT ${params.role}/${params.complexity}`); return HttpResponse.json(chain('Hard', { role: 'Plan' })) }),
        http.delete('/api/complexity-chains/:role/:complexity', ({ params }) => { write(`DELETE ${params.role}/${params.complexity}`); return new HttpResponse(null, { status: 204 }) }),
      )
      const { queryClient } = renderWithProviders(<RoutingSettingsTab />)
      const cell = await screen.findByTestId('routing-matrix-cell-Plan-Hard')
      await userEvent.click(within(cell).getByRole('button', { name: 'Configure Plan / Hard' }))
      const editor = await screen.findByTestId('routing-cell-editor')
      const button = within(editor).getByRole('button', { name: action })
      expect(button).toBeEnabled()

      act(() => {
        onlineManager.setOnline(false)
        void queryClient.resetQueries({ queryKey: complexityChainKeys.effective('Plan'), exact: true })
      })
      expect(await within(editor).findByText("Save and Clear are disabled until the cell's read loads: Plan effective cells unknown (read paused offline).")).toBeInTheDocument()
      expect(within(editor).getByRole('button', { name: 'Save' })).toBeDisabled()
      expect(within(editor).getByRole('button', { name: 'Clear override' })).toBeDisabled()
      expect(within(screen.getByTestId('routing-matrix-row-Plan')).queryAllByRole('button', { name: /^Configure / })).toHaveLength(0)

      act(() => { onlineManager.setOnline(true) })
      await waitFor(() => expect(within(editor).getByRole('button', { name: action })).toBeEnabled())
      expect(within(editor).queryByText(/Save and Clear are disabled until/)).not.toBeInTheDocument()
      expect(write).not.toHaveBeenCalled()
    },
  )
})

describe('C1270 D-2 clear confirmation after a failed read', () => {
  it.each([
    { tier: 'Medium' as const, tail: 'Without an own rule this cell falls back to the Any role list.' },
    { tier: 'Easy' as const, tail: 'Without an own rule this cell is Unset — a -Complexity dispatch for this role and tier will block until an operator sets a row.' },
  ])('C1270 D-2 clear confirmation after a chain read failure on $tier', async ({ tier, tail }) => {
    serveRouting({ effective: { Plan: { ...planEffective, chains: [c1270Cell('chain', 'Required', tier)] } } })
    renderWithProviders(<RoutingSettingsTab />)
    const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
    await userEvent.click(within(cell).getByRole('button', { name: `Configure Plan / ${tier}` }))
    const editor = await screen.findByTestId('routing-cell-editor')
    await userEvent.click(within(editor).getByRole('checkbox', { name: c1270EditorConfirm.chain }))
    await userEvent.click(within(editor).getByRole('button', { name: 'Clear override' }))

    const confirm = await within(editor).findByTestId('routing-cell-editor-clear-confirm')
    expect(confirm).toHaveTextContent(`Clear Plan/${tier} override?`)
    expect(confirm).toHaveTextContent(`Clearing Plan/${tier} deletes this cell's own rule, with its reason and expiry, if one exists; its current value is unknown (chain read failed (InvalidOperationException)). ${tail}`)
    expect(confirm).not.toHaveTextContent('currently replaces')
  })

  it.each(['Medium', 'Easy'] as const)('C1270 D-2 clear confirmation after a chain metadata read failure on %s names the unread reason and expiry', async (tier) => {
    serveRouting({ effective: { Plan: { ...planEffective, chains: [c1270Cell('chain metadata', 'Required', tier)] } } })
    renderWithProviders(<RoutingSettingsTab />)
    const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
    await userEvent.click(within(cell).getByRole('button', { name: `Configure Plan / ${tier}` }))
    const editor = await screen.findByTestId('routing-cell-editor')
    await userEvent.click(within(editor).getByRole('button', { name: 'Clear override' }))

    const confirm = await within(editor).findByTestId('routing-cell-editor-clear-confirm')
    expect(confirm).toHaveTextContent('The stored reason and expiry are unknown (chain metadata read failed (InvalidOperationException)); clearing deletes them with the row.')
  })
})

describe('RoutingSettingsTab', () => {
  it.each((['pin', 'pin+chain', 'role', 'any', 'config', 'rolePolicy', 'agent'] as const)
    .flatMap(source => (['Hard', 'Medium', 'Easy'] as const).map(tier => ({ source, tier }))))(
    'C1258 V-2 stage-pin failure suppresses route promises $source/$tier', async ({ source, tier }) => {
      const reason = 'stage pin read failed (InvalidOperationException)'
      serveRouting({ pins: [], effective: { Plan: {
        ...planEffective,
        chains: [chain(tier, {
          role: 'Plan', resolvedFrom: source === 'any' ? 'any' : source === 'config' ? 'config' : 'role',
          candidates: [heldFable],
          effective: { source, candidates: [codex], stagePin: null, governsDefault: tier === 'Medium', withheldReason: reason },
        })],
      } } })
      renderWithProviders(<RoutingSettingsTab />)
      const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
      expect(await within(cell).findByText('Effective routing unknown')).toBeInTheDocument()
      expect(within(cell).getByRole('alert')).toHaveTextContent(reason)
      expect(cell).not.toHaveTextContent('Own rule')
      expect(cell).not.toHaveTextContent('Inherits Any role')
      expect(cell).not.toHaveTextContent('Configuration fallback')
      expect(cell).not.toHaveTextContent('Falls back to RolePolicy')
      expect(cell).not.toHaveTextContent('Pinned to standing agent')
      expect(cell).not.toHaveTextContent('Codex/Frontier')
      expect(cell).not.toHaveTextContent('ClaudeCode/Frontier')
      expect(cell).not.toHaveTextContent('dispatch blocks')
      expect(within(cell).getByRole('button', { name: `Configure Plan / ${tier}` })).toBeEnabled()
    },
  )

  it.each(['Hard', 'Medium', 'Easy'] as const)(
    'C1258 V-2 stage-pin failure on an Unset cell keeps Stored cell: unset %s', async (tier) => {
      const reason = 'stage pin read failed (InvalidOperationException)'
      serveRouting({ pins: [], effective: { Plan: {
        ...planEffective,
        chains: [chain(tier, {
          role: 'Plan', resolvedFrom: 'none', candidates: [],
          effective: { source: 'rolePolicy', candidates: [codex], stagePin: null, governsDefault: tier === 'Medium', withheldReason: reason },
        })],
      } } })
      renderWithProviders(<RoutingSettingsTab />)
      const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${tier}`)
      expect(await within(cell).findByText('Effective routing unknown')).toBeInTheDocument()
      // The chain read succeeded, so an Unset cell keeps its stored origin.
      expect(within(cell).getByText('Stored cell: unset')).toBeInTheDocument()
      expect(within(cell).getByRole('alert')).toHaveTextContent(reason)
      for (const text of ['Own rule', 'Inherits Any role', 'Configuration fallback', 'Falls back to RolePolicy', 'Codex/Frontier', 'dispatch blocks']) {
        expect(cell).not.toHaveTextContent(text)
      }
      expect(within(cell).getByRole('button', { name: `Configure Plan / ${tier}` })).toBeEnabled()
    },
  )

  it('C097M V-C4 pin-overlay-first', async () => {
    serveRouting({ effective: { Plan: {
      ...planEffective,
      chains: [chain('Hard', {
        role: 'Plan', resolvedFrom: 'role', candidates: [codex],
        effective: {
          source: 'pin', candidates: [{ agentKind: 'ClaudeCode', modelLevel: 'High', alias: 'opus', availableNow: true, unavailableReason: null }],
          stagePin: { id: 'pin-plan', cardId: null, cardIdentifier: null, role: 'Plan', provenance: 'Human', strength: 'Required', agentKind: 'ClaudeCode', modelLevel: 'High', notBefore: null, reason: 'operator choice', candidateCount: 1 },
          governsDefault: false, withheldReason: null,
        },
      })],
    } } })
    renderWithProviders(<RoutingSettingsTab />)
    const cell = await screen.findByTestId('routing-matrix-cell-Plan-Hard')
    const pinLabel = await within(cell).findByText('Required Human pin → ClaudeCode/High (opus)')
    expect(cell.textContent?.startsWith('Required Human pin → ClaudeCode/High (opus)')).toBe(true)
    expect(pinLabel.compareDocumentPosition(within(cell).getByText('Own rule')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeGreaterThan(0)
    expect(within(cell).getByText('1. ClaudeCode/High (opus)')).toBeInTheDocument()
    expect(within(cell).getByText('1. Codex/Frontier (gpt-6-astra)')).toBeInTheDocument()
  })

  it('C097M V-C5 default-column-and-fallback', async () => {
    serveRouting({ effective: { Plan: {
      ...planEffective,
      chains: [
        chain('Medium', { role: 'Plan', effective: { source: 'rolePolicy', candidates: [heldFable], stagePin: null, governsDefault: true, withheldReason: null } }),
        chain('Easy', { role: 'Plan', effective: { source: 'rolePolicy', candidates: [heldFable], stagePin: null, governsDefault: false, withheldReason: null } }),
      ],
    } } })
    renderWithProviders(<RoutingSettingsTab />)
    expect(await screen.findByRole('columnheader', { name: 'Medium (default for an ordinary Worker dispatch without -Complexity)' })).toBeInTheDocument()
    const medium = await screen.findByTestId('routing-matrix-cell-Plan-Medium')
    expect(await within(medium).findByText('Falls back to RolePolicy → ClaudeCode/Frontier (fable)')).toBeInTheDocument()
    expect(within(medium).getByText('held until 2026-09-04T00:00:00Z (manual)')).toBeInTheDocument()
    expect(medium).not.toHaveTextContent('dispatch blocks')
    const easy = screen.getByTestId('routing-matrix-cell-Plan-Easy')
    expect(within(easy).getByText('Unset — a -Complexity Easy dispatch blocks')).toBeInTheDocument()
    expect(easy).not.toHaveTextContent('Falls back to RolePolicy')
  })

  it('C097M V-C6 card-pin-count-in-row', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)
    const code = await screen.findByTestId('routing-matrix-row-Code')
    expect(await within(code).findByText('1 card pin')).toBeInTheDocument()
    const plan = await screen.findByTestId('routing-matrix-cell-Plan-Hard')
    expect(plan.closest('tr')).not.toHaveTextContent('card pin')
    expect(code).not.toHaveTextContent('2 card pins')
  })

  it('C097M V-C5 old-server-keeps-the-legacy-boundary', async () => {
    const legacy: ComplexityChainListDto = {
      roles: ['Plan'], complexities: ['Hard', 'Medium', 'Easy'],
      chains: [
        chain('Hard', { role: 'Plan', resolvedFrom: 'role', candidates: [codex], effective: undefined }),
        chain('Medium', { role: 'Plan', effective: undefined }),
        chain('Easy', { role: 'Plan', effective: undefined }),
      ],
    }
    serveRouting({ list: legacy, effective: { Plan: legacy } })
    renderWithProviders(<RoutingSettingsTab />)
    const matrix = await screen.findByTestId('routing-matrix-section')
    const hard = await within(matrix).findByTestId('routing-matrix-cell-Plan-Hard')
    expect(await within(hard).findByText('Own rule')).toBeInTheDocument()
    expect(within(hard).getByText('1. Codex/Frontier (gpt-6-astra)')).toBeInTheDocument()
    expect(within(matrix).queryByRole('columnheader', { name: /\(default/ })).not.toBeInTheDocument()
    expect(within(matrix).getByRole('columnheader', { name: 'Medium' })).toBeInTheDocument()
    expect(within(matrix).getByTestId('routing-boundary')).toHaveTextContent('RolePolicy still resolves work that is not launched with -Complexity, including default kind and level, escalation, timeout, and WIP. This matrix does not govern every dispatch.')
    expect(matrix).not.toHaveTextContent('walks the Medium row')
    expect(within(matrix).getByTestId('routing-matrix-cell-Plan-Medium')).toHaveTextContent('Unset — a -Complexity Medium dispatch blocks')
  })

  it.each([
    { name: 'policy-read-failure', tier: 'Easy', governsDefault: false, candidates: [codex], reason: 'chain read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unknown (read failed)' },
    { name: 'empty-read-failure', tier: 'Easy', governsDefault: false, candidates: [], reason: 'chain read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unknown (read failed)' },
    { name: 'default-policy-read-failure', tier: 'Medium', governsDefault: true, candidates: [codex], reason: 'chain read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unknown (read failed)' },
    { name: 'non-default-availability-failure', tier: 'Easy', governsDefault: false, candidates: [{ ...codex, availableNow: false, unavailableReason: 'availability read failed (NpgsqlException)' }], reason: 'availability read failed (NpgsqlException)', label: 'Unset — a -Complexity Easy dispatch blocks', stored: null },
    { name: 'non-default-pin-read-failure', tier: 'Easy', governsDefault: false, candidates: [codex], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset' },
    { name: 'default-pin-read-failure', tier: 'Medium', governsDefault: true, candidates: [codex], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset' },
    { name: 'default-empty-pin-read-failure', tier: 'Medium', governsDefault: true, candidates: [], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset' },
    { name: 'default-required-pin-banner-read-failure', tier: 'Medium', governsDefault: true, candidates: [codex], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset', pinStrength: 'Required', banner: 'Required Human pin: Plan → Grok/Frontier. Overrides the Plan cells while this pin is active.' },
    { name: 'non-default-required-pin-banner-read-failure', tier: 'Easy', governsDefault: false, candidates: [codex], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset', pinStrength: 'Required', banner: 'Required Human pin: Plan → Grok/Frontier. Overrides the Plan cells while this pin is active.' },
    { name: 'default-preferred-pin-banner-read-failure', tier: 'Medium', governsDefault: true, candidates: [codex], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset', pinStrength: 'Preferred', banner: 'Preferred Human pin: Plan → Grok/Frontier. Tried before each Plan cell.' },
    { name: 'non-default-preferred-pin-banner-read-failure', tier: 'Easy', governsDefault: false, candidates: [codex], reason: 'stage pin read failed (InvalidOperationException)', label: 'Effective routing unknown', stored: 'Stored cell: unset', pinStrength: 'Preferred', banner: 'Preferred Human pin: Plan → Grok/Frontier. Tried before each Plan cell.' },
    { name: 'non-default-unlaunchable-policy', tier: 'Easy', governsDefault: false, candidates: [], reason: 'Plan policy names Raw/Frontier, which delegation cannot launch', label: 'Unset — a -Complexity Easy dispatch blocks', stored: null },
  ] satisfies Array<{ name: string; tier: TaskComplexity; governsDefault: boolean; candidates: ComplexityCandidateDto[]; reason: string; label: string; stored: string | null; pinStrength?: 'Required' | 'Preferred'; banner?: string }>)('C097M V-C4 diagnostic $name', async (row) => {
    serveRouting({ pins: row.pinStrength ? [pin({ role: 'Plan', strength: row.pinStrength, agentKind: 'Grok', modelLevel: 'Frontier' })] : [], effective: { Plan: {
      ...planEffective,
      chains: [chain(row.tier, { role: 'Plan', effective: {
        source: 'rolePolicy', candidates: row.candidates, stagePin: null,
        governsDefault: row.governsDefault, withheldReason: row.reason,
      } })],
    } } })
    renderWithProviders(<RoutingSettingsTab />)
    const cell = await screen.findByTestId(`routing-matrix-cell-Plan-${row.tier}`)
    expect(await within(cell).findByText(row.label)).toBeInTheDocument()
    expect(within(cell).getByRole('alert')).toHaveTextContent(row.reason)
    expect(cell).not.toHaveTextContent('Falls back to RolePolicy')
    expect(cell).not.toHaveTextContent('Codex/Frontier')
    if (row.stored) {
      expect(within(cell).getByText(row.stored)).toBeInTheDocument()
      expect(cell).not.toHaveTextContent(/dispatch blocks/i)
      if (row.stored === 'Stored cell: unknown (read failed)') expect(cell).not.toHaveTextContent(/unset/i)
    }
    if (row.banner) expect(await within(cell.closest('tr')!).findByText(row.banner)).toBeInTheDocument()
  })

  it.each([
    { name: 'required-empty-cell', source: 'pin', candidates: [codex], candidateText: '1. Codex/Frontier (gpt-6-astra)', reason: null, label: 'Required Human pin → Codex/Frontier (gpt-6-astra)' },
    { name: 'preferred-unlaunchable-policy', source: 'pin+chain', candidates: [codex], candidateText: '1. Codex/Frontier (gpt-6-astra)', reason: 'Plan policy names Raw/Frontier, which delegation cannot launch', label: 'Preferred Human pin → Codex/Frontier (gpt-6-astra), then this row' },
    { name: 'pin-read-failure', source: 'pin', candidates: [codex], candidateText: '1. Codex/Frontier (gpt-6-astra)', reason: 'chain read failed (InvalidOperationException)', label: 'Required Human pin → Codex/Frontier (gpt-6-astra)' },
    { name: 'role-metadata-read-failure', source: 'role', candidates: [codex], candidateText: '1. Codex/Frontier (gpt-6-astra)', reason: 'chain metadata read failed (InvalidOperationException)', label: 'Own rule' },
    { name: 'any-read-failure', source: 'any', candidates: [codex], candidateText: '1. Codex/Frontier (gpt-6-astra)', reason: 'chain metadata read failed (InvalidOperationException)', label: 'Inherits Any role' },
    { name: 'config-availability-failure', source: 'config', candidates: [{ ...heldFable, unavailableReason: 'availability read failed (InvalidOperationException)' }], candidateText: '1. ClaudeCode/Frontier (fable)', reason: 'availability read failed (InvalidOperationException)', label: 'Configuration fallback' },
    { name: 'standing-agent', source: 'agent', candidates: [], reason: 'stage pin binds standing agent c097m-standing (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row', label: 'Pinned to standing agent c097m-standing (ClaudeCode) — an ordinary dispatch runs there; this row is not walked' },
    { name: 'standing-agent-read-failure', source: 'agent', candidates: [], reason: 'stage pin binds standing agent c097m-standing (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row; chain read failed (InvalidOperationException)', label: 'Pinned to standing agent c097m-standing (ClaudeCode) — an ordinary dispatch runs there; this row is not walked' },
  ] satisfies Array<{ name: string; source: ComplexityEffectiveDto['source']; candidates: ComplexityCandidateDto[]; reason: string | null; label: string; candidateText?: string }>)('C097M V-C4 diagnostic $name', async (row) => {
    const { source, candidates, reason, label } = row
    serveRouting({ effective: { Plan: {
      ...planEffective,
      chains: [chain('Easy', { role: 'Plan', effective: {
        source, candidates, withheldReason: reason, governsDefault: false,
        stagePin: source === 'pin' || source === 'pin+chain' || source === 'agent'
          ? { id: 'pin-plan', cardId: null, cardIdentifier: null, role: 'Plan', provenance: 'Human', strength: source === 'pin+chain' ? 'Preferred' : 'Required', agentKind: 'Codex', modelLevel: 'Frontier', notBefore: null, reason: 'operator choice', candidateCount: candidates.length }
          : null,
      } })],
    } } })
    renderWithProviders(<RoutingSettingsTab />)
    const cell = await screen.findByTestId('routing-matrix-cell-Plan-Easy')
    expect(await within(cell).findByText(label)).toBeInTheDocument()
    expect(cell).not.toHaveTextContent('dispatch blocks')
    if (reason) expect(cell).toHaveTextContent(reason)
    if (candidates.length > 0) {
      expect(cell).not.toHaveTextContent('not launchable')
      if (!row.candidateText) throw new Error('Candidate case needs a literal text expectation')
      expect(within(cell).getByText(row.candidateText)).toBeInTheDocument()
    }
    if (reason?.includes('read failed')) expect(within(cell).getByRole('alert')).toHaveTextContent(reason)
    if (reason?.includes('chain read failed')) {
      expect(within(cell).getByText('Stored cell: unknown (read failed)')).toBeInTheDocument()
      expect(cell).not.toHaveTextContent('Stored cell: unset')
    }
  })

  it('C470 mutation can be selected and saved independently', async () => {
    const put = vi.fn()
    serveRouting({ list: { ...listDto, roles: [...(listDto.roles ?? []), 'Mutation'] } })
    server.use(http.put('/api/complexity-chains/:role/:complexity', async ({ params, request }) => {
      put(params.role, await request.json())
      return HttpResponse.json(chain('Hard', { role: 'Mutation' }))
    }))
    renderWithProviders(<RoutingSettingsTab />)
    // Wait for the data-loaded test id before clicking; the original C470 failure cause is a hypothesis, not established (CARD-0097 D2).
    const mutationHard = await screen.findByTestId('routing-matrix-cell-Mutation-Hard')
    await userEvent.click(within(mutationHard).getByRole('button', { name: 'Configure Mutation / Hard' }))
    const editor = await screen.findByTestId('routing-cell-editor')
    await userEvent.click(within(editor).getByRole('button', { name: 'Add candidate' }))
    await userEvent.click(within(editor).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put).toHaveBeenCalledWith('Mutation', expect.objectContaining({ provenance: 'Human' }))
    await screen.findByTestId('routing-pin-banner-Code')
    expect(screen.getByTestId('routing-matrix-row-Code')).toHaveTextContent('Required Human pin: Code')
  })

  it('a runner-defaults failure leaves the matrix mounted', async () => {
    serveRouting()
    server.use(http.get('/api/runner-defaults', () => new HttpResponse(null, { status: 500 })))
    renderWithProviders(<RoutingSettingsTab />)
    expect(await screen.findByRole('heading', { name: 'Role × complexity matrix' })).toBeInTheDocument()
    expect(await screen.findByText(/Could not load runner defaults/)).toBeInTheDocument()
    expect(await screen.findByTestId('routing-matrix-row-Code')).toBeInTheDocument()
  })

  it('renders the three headed sections with availability, usage, and the matrix', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    expect(await screen.findByRole('heading', { name: 'Model availability' })).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'Subscription usage observations (best effort)' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Role × complexity matrix' })).toBeInTheDocument()

    const availability = screen.getByTestId('routing-availability-section')
    expect(await within(availability).findByText('weekly cap')).toBeInTheDocument()
    expect(within(availability).getAllByText('Available').length).toBeGreaterThan(0)
    expect(within(availability).getAllByText('Held').length).toBe(2)
    expect(within(availability).getByText('2026-09-04T00:00:00Z')).toBeInTheDocument()
    expect(within(availability).getByText('until cleared')).toBeInTheDocument()
    expect(within(availability).getByText('AutoDetected')).toBeInTheDocument()
    expect(within(availability).getByRole('button', { name: 'Hold' })).toBeInTheDocument()

    expect(await screen.findByText(USAGE_UNKNOWN)).toBeInTheDocument()
    expect(await screen.findByText('Any role')).toBeInTheDocument()
  })

  it('renders Any role first, then the server role order, with effective cells', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const anyRow = await screen.findByTestId('routing-matrix-row-any')
    const planRow = await screen.findByTestId('routing-matrix-row-Plan')
    const codeRow = await screen.findByTestId('routing-matrix-row-Code')
    expect(anyRow.compareDocumentPosition(planRow) & Node.DOCUMENT_POSITION_FOLLOWING).toBeGreaterThan(
      0,
    )
    expect(planRow.compareDocumentPosition(codeRow) & Node.DOCUMENT_POSITION_FOLLOWING).toBeGreaterThan(
      0,
    )

    expect(within(anyRow).getByText('Any role')).toBeInTheDocument()
    expect(within(planRow).getByText('Plan')).toBeInTheDocument()
    expect(within(codeRow).getByText('Code')).toBeInTheDocument()

    const planHard = await screen.findByTestId('routing-matrix-cell-Plan-Hard')
    expect(within(planHard).getByText('Own rule')).toBeInTheDocument()
    expect(within(planHard).getByText(/Codex\/Frontier \(gpt-6-astra\)/)).toBeInTheDocument()

    const anyHard = screen.getByTestId('routing-matrix-cell-any-Hard')
    expect(within(anyHard).getByText('Any role rule')).toBeInTheDocument()
    expect(within(anyHard).getByText(/Grok\/Frontier \(grok-4.7\)/)).toBeInTheDocument()
  })

  it('shows inheritance, configuration fallback, and unset text', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const planMedium = await screen.findByTestId('routing-matrix-cell-Plan-Medium')
    expect(within(planMedium).getByText('Inherits Any role')).toBeInTheDocument()

    const codeHard = await screen.findByTestId('routing-matrix-cell-Code-Hard')
    expect(within(codeHard).getByText('Configuration fallback')).toBeInTheDocument()

    const planEasy = screen.getByTestId('routing-matrix-cell-Plan-Easy')
    expect(within(planEasy).getByText('Unset — a -Complexity Easy dispatch blocks')).toBeInTheDocument()
    const anyEasy = screen.getByTestId('routing-matrix-cell-any-Easy')
    expect(within(anyEasy).getByText('Unset — a -Complexity Easy dispatch blocks')).toBeInTheDocument()
  })

  it('renders an unavailable candidate reason from the server and does not invent a fallback', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const codeHard = await screen.findByTestId('routing-matrix-cell-Code-Hard')
    expect(within(codeHard).getByText('held until 2026-09-04T00:00:00Z (manual)')).toBeInTheDocument()
    expect(within(codeHard).getByText(/Grok\/Frontier \(grok-4.7\)/)).toBeInTheDocument()
    expect(within(codeHard).queryByText(/fallback to/i)).not.toBeInTheDocument()
  })

  it('wording for Required bypass versus Preferred prepend, and names Code → Grok', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const codeRow = await screen.findByTestId('routing-matrix-row-Code')
    expect(await within(codeRow).findByText(/Required Human pin: Code → Grok/)).toBeInTheDocument()
    expect(within(codeRow).getByText(/Overrides the Code cells while this pin is active/)).toBeInTheDocument()

    const planRow = screen.getByTestId('routing-matrix-row-Plan')
    expect(await within(planRow).findByText(/Preferred Human pin: Plan → ClaudeCode\/Frontier/)).toBeInTheDocument()
    expect(within(planRow).getByText(/Tried before each Plan cell/)).toBeInTheDocument()
  })

  it('scopes a card pin to that card and does not present it as a stage-wide banner', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    // The card-pins list renders its empty state before the pins query answers; await the pin.
    const cardPins = await screen.findByTestId('routing-card-pins')
    expect(await within(cardPins).findByText(/CARD-0332/)).toBeInTheDocument()
    expect(within(cardPins).getByText('Card-specific pins')).toBeInTheDocument()
    expect(within(cardPins).getByText(CARD_PIN_SCOPE)).toBeInTheDocument()
    expect(within(cardPins).getByText(/Applies only to this card, not to every Code task/)).toBeInTheDocument()

    // The absence checks below only mean something once the Code row has rendered its pins.
    await screen.findByTestId('routing-pin-banner-Code')
    const codeRow = screen.getByTestId('routing-matrix-row-Code')
    expect(within(codeRow).queryByText(/CARD-0332/)).not.toBeInTheDocument()
    expect(within(codeRow).queryByText(/not to every Code task/)).not.toBeInTheDocument()
  })

  it('says unknown when there is no subscription sample', async () => {
    serveRouting({ usage: [] })
    renderWithProviders(<RoutingSettingsTab />)
    expect(await screen.findByText(USAGE_UNKNOWN)).toBeInTheDocument()
  })

  it('shows a full observed sample with timestamp and no live or per-model claim', async () => {
    serveRouting({ usage: fullUsage })
    renderWithProviders(<RoutingSettingsTab />)

    const usage = await screen.findByTestId('routing-usage-section')
    expect(await within(usage).findByText('Codex')).toBeInTheDocument()
    expect(within(usage).getByText('ChatGPT Plus')).toBeInTheDocument()
    expect(within(usage).getByText('73.5% remaining')).toBeInTheDocument()
    expect(within(usage).getByText('2026-09-06T00:00:00Z')).toBeInTheDocument()
    expect(within(usage).getByText('observed at 2026-09-04T12:00:00Z')).toBeInTheDocument()
    expect(within(usage).getByText('00:15:00')).toBeInTheDocument()
    expect(within(usage).queryByText(/live/i)).not.toBeInTheDocument()
    expect(within(usage).getByText(/not a per-model quota/)).toBeInTheDocument()
  })

  it('shows only observed state and time when a sample has no percent or reset', async () => {
    serveRouting({
      usage: [
        {
          provider: 'Grok',
          planLabel: null,
          remainingPercent: null,
          resetsAt: null,
          observedAt: '2026-09-04T09:00:00Z',
          age: '01:00:00',
        },
      ],
    })
    renderWithProviders(<RoutingSettingsTab />)

    const usage = await screen.findByTestId('routing-usage-section')
    expect(await within(usage).findByText('Grok')).toBeInTheDocument()
    expect(within(usage).getByText('observed at 2026-09-04T09:00:00Z')).toBeInTheDocument()
    expect(within(usage).queryByText(/% remaining/)).not.toBeInTheDocument()
  })

  it('states the D6 propagation boundary and the D7 non-complexity RolePolicy boundary', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const boundary = await screen.findByTestId('routing-boundary')
    expect(within(boundary).getByText(COMPLEXITY_ROUTING_BOUNDARY_TITLE)).toBeInTheDocument()
    expect(within(boundary).getByText(/New complexity-routed dispatches read the saved cell immediately/)).toBeInTheDocument()
    expect(within(boundary).getByText(/Running sessions keep the model they started with/)).toBeInTheDocument()
    expect(within(boundary).getByText(/never interrupts, replaces, or migrates a mid-turn delegate/)).toBeInTheDocument()
    expect(within(boundary).getByText('An ordinary Worker dispatch without -Complexity, -Kind/-Level, -Agent or ignoreModelDisabled walks the Medium row; explicit asks, orchestrators, standing agents and specialist roles keep their existing routing path.')).toBeInTheDocument()
  })

  it('keeps a role row error from blanking the rest of the grid', async () => {
    serveRouting({ effective: { Plan: 'error', Code: codeEffective } })
    renderWithProviders(<RoutingSettingsTab />)

    expect(await screen.findByText('Could not load Plan effective cells.')).toBeInTheDocument()
    expect(screen.getByTestId('routing-matrix-row-any')).toBeInTheDocument()
    const codeRow = await screen.findByTestId('routing-matrix-row-Code')
    expect(await within(codeRow).findByText('Configuration fallback')).toBeInTheDocument()
    expect(screen.queryByTestId('routing-matrix-cell-Plan-Hard')).not.toBeInTheDocument()
  })

  it('opens the cell editor from Configure without leaving the matrix read-only until then', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const planHard = await screen.findByTestId('routing-matrix-cell-Plan-Hard')
    expect(within(planHard).getByRole('button', { name: 'Configure Plan / Hard' })).toBeInTheDocument()
    expect(screen.queryByTestId('routing-cell-editor')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Configure Plan / Hard' }))
    expect(await screen.findByRole('dialog', { name: 'Configure Plan / Hard' })).toBeInTheDocument()
    expect(screen.getByTestId('routing-cell-editor')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Clear override' })).toBeInTheDocument()
  })

  it('C097 V09 role-policy-failure-keeps-siblings', async () => {
    serveRouting()
    server.use(http.get('/api/role-policy', () => new HttpResponse(null, { status: 500 })))
    renderWithProviders(<RoutingSettingsTab />)

    const section = await screen.findByTestId('role-policy-section')
    expect(await within(section).findByText('Could not load role policy.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Model availability' })).toBeInTheDocument()
    expect(screen.getByTestId('runner-defaults-section')).toBeInTheDocument()
    expect(await screen.findByTestId('routing-matrix-row-Code')).toBeInTheDocument()
  })

  it('C097 V10 section-order', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const anyRow = await screen.findByTestId('routing-matrix-row-any')
    const runnerDefaults = screen.getByTestId('runner-defaults-section')
    const rolePolicy = screen.getByTestId('role-policy-section')
    expect(
      runnerDefaults.compareDocumentPosition(rolePolicy) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeGreaterThan(0)
    expect(rolePolicy.compareDocumentPosition(anyRow) & Node.DOCUMENT_POSITION_FOLLOWING).toBeGreaterThan(0)
    expect(within(rolePolicy).getByRole('heading', { name: 'Role policy' })).toBeInTheDocument()
  })
})
