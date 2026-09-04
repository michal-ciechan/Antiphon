import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import type { ComplexityChainListDto } from '../../api/complexityChains'
import { ComplexityChainPanel } from './ComplexityChainPanel'

const threeEmpty: ComplexityChainListDto = {
  chains: [
    { complexity: 'Hard', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
    { complexity: 'Medium', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
    { complexity: 'Easy', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
  ],
}

describe('ComplexityChainPanel', () => {
  it('renders three empty chains as the empty-defaults sentence', async () => {
    server.use(http.get('/api/complexity-chains', () => HttpResponse.json(threeEmpty)))
    renderWithProviders(<ComplexityChainPanel />)
    expect(await screen.findByText(/No chains set/)).toBeInTheDocument()
  })

  it('renders three chains with live available/held reasons', async () => {
    server.use(
      http.get('/api/complexity-chains', () =>
        HttpResponse.json({
          chains: [
            {
              complexity: 'Hard',
              candidates: [
                {
                  agentKind: 'ClaudeCode',
                  modelLevel: 'Frontier',
                  alias: 'fable',
                  availableNow: false,
                  unavailableReason: 'held until 2026-09-04T00:00:00Z (manual)',
                },
                {
                  agentKind: 'Grok',
                  modelLevel: 'Frontier',
                  alias: 'grok-4.6',
                  availableNow: true,
                  unavailableReason: null,
                },
              ],
              provenance: 'Human',
              source: 'pin',
              reason: 'plan-grade',
              notAfter: null,
              updatedAt: '2026-09-02T00:00:00Z',
            },
            { complexity: 'Medium', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
            { complexity: 'Easy', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
          ],
        } satisfies ComplexityChainListDto),
      ),
    )
    renderWithProviders(<ComplexityChainPanel />)
    expect(await screen.findByText('Hard (any role)')).toBeInTheDocument()
    expect(screen.getByText('Medium (any role)')).toBeInTheDocument()
    expect(screen.getByText('Easy (any role)')).toBeInTheDocument()
    expect(screen.getByText(/held until/)).toBeInTheDocument()
    expect(screen.getByText('available')).toBeInTheDocument()
  })

  it('labels a Plan/Hard cell distinctly from the any-role Hard row', async () => {
    server.use(
      http.get('/api/complexity-chains', () =>
        HttpResponse.json({
          roles: ['Plan', 'Code'],
          complexities: ['Hard', 'Medium', 'Easy'],
          chains: [
            {
              complexity: 'Hard',
              role: null,
              resolvedFrom: 'any',
              candidates: [
                {
                  agentKind: 'Grok',
                  modelLevel: 'Frontier',
                  alias: 'grok-4.6',
                  availableNow: true,
                  unavailableReason: null,
                },
              ],
              provenance: 'Human',
              source: 'pin',
              reason: 'any-role Hard',
              notAfter: null,
              updatedAt: '2026-09-04T00:00:00Z',
            },
            { complexity: 'Medium', role: null, resolvedFrom: 'none', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
            { complexity: 'Easy', role: null, resolvedFrom: 'none', candidates: [], provenance: null, source: 'config', reason: null, notAfter: null, updatedAt: null },
            {
              complexity: 'Hard',
              role: 'Plan',
              resolvedFrom: 'role',
              candidates: [
                {
                  agentKind: 'ClaudeCode',
                  modelLevel: 'Frontier',
                  alias: 'fable',
                  availableNow: true,
                  unavailableReason: null,
                },
              ],
              provenance: 'Human',
              source: 'pin',
              reason: 'Plan/Hard',
              notAfter: null,
              updatedAt: '2026-09-04T00:00:00Z',
            },
          ],
        } satisfies ComplexityChainListDto),
      ),
    )
    renderWithProviders(<ComplexityChainPanel />)
    expect(await screen.findByText('Plan/Hard')).toBeInTheDocument()
    expect(screen.getByText('Hard (any role)')).toBeInTheDocument()
  })
})
