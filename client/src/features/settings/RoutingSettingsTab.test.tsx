import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { AgentTaskRole } from '../../api/agentTasks'
import type {
  ComplexityCandidateDto,
  ComplexityChainDto,
  ComplexityChainListDto,
  ComplexityResolvedFrom,
  TaskComplexity,
} from '../../api/complexityChains'
import type { ModelAvailabilityDto } from '../../api/modelAvailability'
import type { RoutingPinDto } from '../../api/routingPins'
import type { SubscriptionUsageObservationDto } from '../../api/subscriptionUsage'
import { renderWithProviders, screen, userEvent, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { RoutingSettingsTab } from './RoutingSettingsTab'
import {
  CARD_PIN_SCOPE,
  COMPLEXITY_ROUTING_BOUNDARY_TITLE,
  NON_COMPLEXITY_BOUNDARY,
  USAGE_UNKNOWN,
} from './routingSettingsModel'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const grok: ComplexityCandidateDto = {
  agentKind: 'Grok',
  modelLevel: 'Frontier',
  alias: 'grok-4.6',
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
    modelAlias: 'grok-4.6',
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
      modelAlias: 'grok-4.6',
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

    const planHard = screen.getByTestId('routing-matrix-cell-Plan-Hard')
    expect(within(planHard).getByText('Own rule')).toBeInTheDocument()
    expect(within(planHard).getByText(/Codex\/Frontier \(gpt-6-astra\)/)).toBeInTheDocument()

    const anyHard = screen.getByTestId('routing-matrix-cell-any-Hard')
    expect(within(anyHard).getByText('Any role rule')).toBeInTheDocument()
    expect(within(anyHard).getByText(/Grok\/Frontier \(grok-4.6\)/)).toBeInTheDocument()
  })

  it('shows inheritance, configuration fallback, and unset text', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const planMedium = await screen.findByTestId('routing-matrix-cell-Plan-Medium')
    expect(within(planMedium).getByText('Inherits Any role')).toBeInTheDocument()

    const codeHard = screen.getByTestId('routing-matrix-cell-Code-Hard')
    expect(within(codeHard).getByText('Configuration fallback')).toBeInTheDocument()

    const planEasy = screen.getByTestId('routing-matrix-cell-Plan-Easy')
    expect(within(planEasy).getByText('Unset — dispatch blocks')).toBeInTheDocument()
    const anyEasy = screen.getByTestId('routing-matrix-cell-any-Easy')
    expect(within(anyEasy).getByText('Unset — dispatch blocks')).toBeInTheDocument()
  })

  it('renders an unavailable candidate reason from the server and does not invent a fallback', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const codeHard = await screen.findByTestId('routing-matrix-cell-Code-Hard')
    expect(within(codeHard).getByText('held until 2026-09-04T00:00:00Z (manual)')).toBeInTheDocument()
    expect(within(codeHard).getByText(/Grok\/Frontier \(grok-4.6\)/)).toBeInTheDocument()
    expect(within(codeHard).queryByText(/fallback to/i)).not.toBeInTheDocument()
  })

  it('wording for Required bypass versus Preferred prepend, and names Code → Grok', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const codeRow = await screen.findByTestId('routing-matrix-row-Code')
    expect(await within(codeRow).findByText(/Required Human pin: Code → Grok/)).toBeInTheDocument()
    expect(within(codeRow).getByText(/bypasses the matrix cells for Code/)).toBeInTheDocument()

    const planRow = screen.getByTestId('routing-matrix-row-Plan')
    expect(within(planRow).getByText(/Preferred Human pin: Plan → ClaudeCode\/Frontier/)).toBeInTheDocument()
    expect(within(planRow).getByText(/prepends to the matrix candidates/)).toBeInTheDocument()
    expect(within(planRow).getByText(/falls through to this row/)).toBeInTheDocument()
  })

  it('scopes a card pin to that card and does not present it as a stage-wide banner', async () => {
    serveRouting()
    renderWithProviders(<RoutingSettingsTab />)

    const cardPins = await screen.findByTestId('routing-card-pins')
    expect(within(cardPins).getByText('Card-specific pins')).toBeInTheDocument()
    expect(within(cardPins).getByText(CARD_PIN_SCOPE)).toBeInTheDocument()
    expect(within(cardPins).getByText(/CARD-0332/)).toBeInTheDocument()
    expect(within(cardPins).getByText(/Applies only to this card, not to every Code task/)).toBeInTheDocument()

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
    expect(within(boundary).getByText(NON_COMPLEXITY_BOUNDARY)).toBeInTheDocument()
    expect(within(boundary).getByText(/RolePolicy/)).toBeInTheDocument()
  })

  it('keeps a role row error from blanking the rest of the grid', async () => {
    serveRouting({ effective: { Plan: 'error', Code: codeEffective } })
    renderWithProviders(<RoutingSettingsTab />)

    expect(await screen.findByText('Could not load Plan effective cells.')).toBeInTheDocument()
    expect(screen.getByTestId('routing-matrix-row-any')).toBeInTheDocument()
    const codeRow = await screen.findByTestId('routing-matrix-row-Code')
    expect(within(codeRow).getByText('Configuration fallback')).toBeInTheDocument()
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
})
