import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
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

export const complexityChainKeys = {
  all: ['complexity-chains'] as const,
}

export function useComplexityChains() {
  return useQuery({
    queryKey: complexityChainKeys.all,
    queryFn: () => apiGet<ComplexityChainListDto>('/complexity-chains'),
    refetchInterval: 15_000,
  })
}
