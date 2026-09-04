import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPut } from './client'
import type { AgentKind } from './boards'
import type { AgentModelLevel } from './agents'
import type { AgentTaskRole } from './agentTasks'

export type TaskComplexity = 'Hard' | 'Medium' | 'Easy'

export type ComplexityResolvedFrom = 'role' | 'any' | 'config' | 'none'

export interface ComplexityCandidateDto {
  agentKind: AgentKind
  modelLevel: AgentModelLevel
  alias: string
  availableNow: boolean
  unavailableReason: string | null
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
}

export interface ComplexityChainListDto {
  chains: ComplexityChainDto[]
  /** Routable roles CARD-0333 renders as grid rows. */
  roles?: AgentTaskRole[]
  complexities?: TaskComplexity[]
}

export interface PutComplexityChainRequest {
  candidates: Array<{ agentKind: AgentKind; modelLevel: AgentModelLevel }>
  provenance: 'Human'
  reason?: string | null
  notAfter?: string | null
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
    }: {
      role: AgentTaskRole | null
      complexity: TaskComplexity
      candidates: PutComplexityChainRequest['candidates']
      reason?: string | null
      notAfter?: string | null
    }) =>
      apiPut<ComplexityChainDto>(complexityChainPath(role, complexity), {
        candidates,
        provenance: 'Human',
        reason: reason ?? null,
        notAfter: notAfter ?? null,
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
    }: {
      role: AgentTaskRole | null
      complexity: TaskComplexity
    }) => apiDelete(complexityChainPath(role, complexity)),
    onSuccess: () => {
      void invalidateComplexityChains(queryClient)
    },
  })
}
