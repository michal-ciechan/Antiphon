import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind } from './boards'
import type { AgentModelLevel } from './agents'

export type TaskComplexity = 'Hard' | 'Medium' | 'Easy'

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
}

export interface ComplexityChainListDto {
  chains: ComplexityChainDto[]
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
