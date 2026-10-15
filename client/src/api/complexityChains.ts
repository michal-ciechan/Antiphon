import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ApiError, apiDelete, apiGet, apiPut } from './client'
import type { AgentKind } from './boards'
import type { AgentModelLevel } from './agents'
import type { AgentTaskRole, RoutingPinRefDto } from './agentTasks'

export type TaskComplexity = 'Hard' | 'Medium' | 'Easy'

export type ComplexityResolvedFrom = 'role' | 'any' | 'config' | 'none'

export interface ComplexityCandidateDto {
  agentKind: AgentKind
  modelLevel: AgentModelLevel
  alias: string
  availableNow: boolean
  unavailableReason: string | null
}

export interface ComplexityEffectiveDto {
  source: 'pin' | 'pin+chain' | 'role' | 'any' | 'config' | 'rolePolicy' | 'agent'
  candidates: ComplexityCandidateDto[]
  stagePin: RoutingPinRefDto | null
  governsDefault: boolean
  withheldReason: string | null
}

export interface ComplexityChainDto {
  complexity: TaskComplexity
  candidates: ComplexityCandidateDto[]
  provenance: 'Human' | 'Auto' | null
  source: 'pin' | 'config'
  reason: string | null
  notAfter: string | null
  updatedAt: string | null
  /** Null/omitted = any-role row. Set on a cell and on the `?role=` effective view. */
  role?: AgentTaskRole | null
  /** Where this entry was resolved from. List view: `role` for a cell, `any`/`config`/`none` for any-role. */
  resolvedFrom?: ComplexityResolvedFrom
  effective?: ComplexityEffectiveDto | null
  /**
   * CARD-1275: this cell's own row version; null when the cell has no row (a role cell that
   * resolves through Any role or config has none).
   */
  concurrencyToken?: string | null
}

export interface ComplexityChainListDto {
  chains: ComplexityChainDto[]
  /** Routable roles CARD-0333 renders as grid rows. */
  roles?: AgentTaskRole[]
  complexities?: TaskComplexity[]
  defaultComplexity?: TaskComplexity
}

export interface PutComplexityChainRequest {
  candidates: Array<{ agentKind: AgentKind; modelLevel: AgentModelLevel }>
  provenance: 'Human'
  reason?: string | null
  notAfter?: string | null
  /** Omitted = unconditional (legacy); {@link EMPTY_CONCURRENCY_TOKEN} = expect no row. */
  concurrencyToken?: string
}

/** CARD-1275 wire value for "the editor saw no row in this cell". */
export const EMPTY_CONCURRENCY_TOKEN = '00000000-0000-0000-0000-000000000000'

export const COMPLEXITY_CHAIN_STALE_CODE = 'complexity_chain_stale'

/** The stored row a 409 `complexity_chain_stale` carries, or null for any other error. */
export function staleComplexityChain(error: unknown): ComplexityChainDto | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null
  const body = error.body
  if (!body || typeof body !== 'object') return null
  if (!('code' in body) || body.code !== COMPLEXITY_CHAIN_STALE_CODE) return null
  const current = 'current' in body ? body.current : undefined
  return current && typeof current === 'object' ? (current as ComplexityChainDto) : null
}

export const complexityChainKeys = {
  all: ['complexity-chains'] as const,
  list: () => [...complexityChainKeys.all, 'list'] as const,
  effective: (role: AgentTaskRole) => [...complexityChainKeys.all, 'effective', role] as const,
}

/** Any-role writes use the three-segment `/any/{complexity}` path, matching the server alias. */
export function complexityChainPath(
  role: AgentTaskRole | null,
  complexity: TaskComplexity,
): string {
  const roleSegment = role ?? 'any'
  return `/complexity-chains/${encodeURIComponent(roleSegment)}/${encodeURIComponent(complexity)}`
}

export function useComplexityChains() {
  return useQuery({
    queryKey: complexityChainKeys.list(),
    queryFn: () => apiGet<ComplexityChainListDto>('/complexity-chains'),
    refetchInterval: 15_000,
  })
}

export function useComplexityChainEffective(role: AgentTaskRole) {
  return useQuery({
    queryKey: complexityChainKeys.effective(role),
    queryFn: () =>
      apiGet<ComplexityChainListDto>(`/complexity-chains?role=${encodeURIComponent(role)}`),
    refetchInterval: 15_000,
  })
}

function invalidateComplexityChains(queryClient: QueryClient) {
  // Prefix match: list and every per-role effective row.
  return queryClient.invalidateQueries({ queryKey: complexityChainKeys.all })
}

export function usePutComplexityChain() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      role,
      complexity,
      candidates,
      reason,
      notAfter,
      concurrencyToken,
    }: {
      role: AgentTaskRole | null
      complexity: TaskComplexity
      candidates: PutComplexityChainRequest['candidates']
      reason?: string | null
      notAfter?: string | null
      concurrencyToken?: string
    }) =>
      apiPut<ComplexityChainDto>(complexityChainPath(role, complexity), {
        candidates,
        provenance: 'Human',
        reason: reason ?? null,
        notAfter: notAfter ?? null,
        ...(concurrencyToken === undefined ? {} : { concurrencyToken }),
      } satisfies PutComplexityChainRequest),
    onSuccess: () => {
      void invalidateComplexityChains(queryClient)
    },
  })
}

export function useClearComplexityChain() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      role,
      complexity,
      concurrencyToken,
    }: {
      role: AgentTaskRole | null
      complexity: TaskComplexity
      concurrencyToken?: string
    }) =>
      apiDelete(
        concurrencyToken === undefined
          ? complexityChainPath(role, complexity)
          : `${complexityChainPath(role, complexity)}?concurrencyToken=${encodeURIComponent(concurrencyToken)}`,
      ),
    onSuccess: () => {
      void invalidateComplexityChains(queryClient)
    },
  })
}
