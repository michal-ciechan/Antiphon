import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AgentTaskRole } from '../../api/agentTasks'
import type { RolePolicyDto, RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinDto } from '../../api/routingPins'
import { ROLE_POLICY_FIXTURE } from '../../test/mocks/handlers'
import { server } from '../../test/mocks/server'
import { renderWithProviders, screen, within } from '../../test/utils'
import { RolePolicySection } from './RolePolicySection'

function serveRolePolicy(dto: RolePolicyDto) {
  server.use(http.get('/api/role-policy', () => HttpResponse.json(dto)))
}

const futureRow: RolePolicyRowDto = {
  role: 'FutureRole' as AgentTaskRole,
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
}

const planPin: RoutingPinDto = {
  id: 'pin-plan',
  cardId: null,
  cardIdentifier: null,
  role: 'Plan',
  provenance: 'Human',
  strength: 'Required',
  agentKind: 'Codex',
  modelLevel: 'Frontier',
  modelAlias: 'gpt-6-astra',
  agentId: null,
  forbiddenAliases: [],
  notBefore: null,
  notAfter: null,
  reason: 'plan on sol',
  sourceTaskId: null,
  createdAt: '2026-10-13T16:47:00Z',
  updatedAt: '2026-10-13T16:47:00Z',
  candidates: [
    {
      agentKind: 'Codex',
      modelLevel: 'Frontier',
      alias: 'gpt-6-astra',
      availableNow: false,
      unavailableReason: 'held until 2026-10-14T00:00:00Z (manual)',
    },
    {
      agentKind: 'ClaudeCode',
      modelLevel: 'Frontier',
      alias: 'fable',
      availableNow: true,
      unavailableReason: null,
    },
  ],
  candidateCount: 2,
}

describe('RolePolicySection', () => {
  it('C097 V08 rows-from-dto-not-mirror', async () => {
    serveRolePolicy({ ...ROLE_POLICY_FIXTURE, roles: [...ROLE_POLICY_FIXTURE.roles, futureRow] })
    renderWithProviders(<RolePolicySection />)

    const mutation = await screen.findByTestId('role-policy-row-Mutation')
    expect(screen.getAllByTestId(/^role-policy-row-/)).toHaveLength(15)
    expect(mutation).toHaveTextContent('ClaudeCode/Low (haiku)')
    expect(mutation).not.toHaveTextContent('Frontier')
    expect(screen.getByTestId('role-policy-row-Custom')).toHaveTextContent('not configured')
    expect(screen.getByTestId('role-policy-row-FutureRole')).toBeInTheDocument()
    const testDesign = screen.getByTestId('role-policy-row-TestDesign')
    expect(testDesign.compareDocumentPosition(mutation) & Node.DOCUMENT_POSITION_FOLLOWING).toBeGreaterThan(0)
  })

  it('C097 V25 stage-pin-column-and-effective', async () => {
    serveRolePolicy({
      ...ROLE_POLICY_FIXTURE,
      roles: ROLE_POLICY_FIXTURE.roles.map((row) =>
        row.role === 'Plan'
          ? {
              ...row,
              stagePin: planPin,
              effective: {
                source: 'pin',
                agentKind: 'Codex',
                modelLevel: 'Frontier',
                alias: 'gpt-6-astra',
                strength: 'Required',
              },
            }
          : row,
      ),
    })
    renderWithProviders(<RolePolicySection />)

    const plan = await screen.findByTestId('role-policy-row-Plan')
    expect(within(plan).getByText('Required Human pin')).toBeInTheDocument()
    expect(within(plan).getByText('1. Codex/Frontier (gpt-6-astra)')).toBeInTheDocument()
    expect(within(plan).getByText('held until 2026-10-14T00:00:00Z (manual)')).toBeInTheDocument()
    expect(within(plan).getByText('2. ClaudeCode/Frontier (fable)')).toBeInTheDocument()
    expect(within(plan).getByText('plan on sol')).toBeInTheDocument()
    expect(within(plan).getByText('Required Human pin → Codex/Frontier (gpt-6-astra)')).toBeInTheDocument()

    const code = screen.getByTestId('role-policy-row-Code')
    expect(within(code).getByTestId('role-policy-pin-Code').textContent).toBe('—')
    expect(within(code).getByText('default → ClaudeCode/Frontier (fable)')).toBeInTheDocument()

    expect(within(screen.getByTestId('role-policy-section')).queryByText(/fallback to/i)).not.toBeInTheDocument()
  })
})
