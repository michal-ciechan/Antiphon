import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskRole } from '../../api/agentTasks'
import type {
  ComplexityCandidateDto,
  ComplexityChainDto,
  ComplexityChainListDto,
  ComplexityEffectiveDto,
  ComplexityResolvedFrom,
  TaskComplexity,
} from '../../api/complexityChains'
import type { ModelAvailabilityDto } from '../../api/modelAvailability'
import type { RoutingPinDto } from '../../api/routingPins'
import type { SubscriptionUsageObservationDto } from '../../api/subscriptionUsage'
import { renderWithProviders, screen, userEvent, within, waitFor } from '../../test/utils'
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

describe('RoutingSettingsTab', () => {
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
    { name: 'role-read-failure', source: 'role', candidates: [codex], candidateText: '1. Codex/Frontier (gpt-6-astra)', reason: 'stage pin read failed (InvalidOperationException)', label: 'Own rule' },
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
