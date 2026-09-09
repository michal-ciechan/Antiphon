import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiPut } from './client'
import { agentKeys, type AgentModelLevel } from './agents'
import type { AgentKind } from './boards'

export interface SpecialistPair { agentKind: AgentKind; modelLevel: AgentModelLevel }
export interface SpecialistRouting {
  agentId: string
  concurrencyToken: string | null
  enabled: boolean | null
  primaryKind: AgentKind
  primaryLevel: AgentModelLevel
  primaryModelAlias: string
  candidates: SpecialistPair[]
  candidateStates: Array<SpecialistPair & {
    id: string; modelAlias: string; physicalAgentId: string | null; enabled: boolean
    status: string; reason: string | null; declaredAt: string; unprovisionedAt: string | null
    lastAdmissionRefusedAt: string | null; nextEligibleAt: string | null; transientFailures: number
  }>
  health?: { status: string; reason: string | null; lastValidCheckAt: string | null; consecutiveFailedRequests: number } | null
}

export const specialistRoutingKey = (id: string) => [...agentKeys.detail(id), 'specialist-routing'] as const
export function useSpecialistRouting(id: string) {
  return useQuery({ queryKey: specialistRoutingKey(id), queryFn: () => apiGet<SpecialistRouting>(`/agents/${id}/specialist-routing`), enabled: !!id })
}
export function useSaveSpecialistRouting(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (request: { concurrencyToken: string | null; enabled: boolean; candidates: SpecialistPair[] }) => apiPut<SpecialistRouting>(`/agents/${id}/specialist-routing`, request),
    onSuccess: result => { client.setQueryData(specialistRoutingKey(id), result); client.invalidateQueries({ queryKey: agentKeys.all }); client.invalidateQueries({ queryKey: ['attention'] }) },
  })
}
export function useRevalidateSpecialistRouting(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (concurrencyToken: string) => apiPost<SpecialistRouting>(`/agents/${id}/specialist-routing/revalidate`, { concurrencyToken }),
    onSuccess: result => { client.setQueryData(specialistRoutingKey(id), result); client.invalidateQueries({ queryKey: ['attention'] }) },
  })
}
