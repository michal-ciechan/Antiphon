import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind } from './boards'
import type { AgentModelLevel } from './agents'
import type { AgentTaskRole } from './agentTasks'

export type RoutingPinProvenance = 'Human' | 'Auto'
export type RoutingPinStrength = 'Preferred' | 'Required'

export interface RoutingPinDto {
  id: string
  cardId: string | null
  cardIdentifier: string | null
  role: AgentTaskRole
  provenance: RoutingPinProvenance
  strength: RoutingPinStrength
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  modelAlias: string | null
  agentId: string | null
  forbiddenAliases: string[]
  notBefore: string | null
  notAfter: string | null
  reason: string
  sourceTaskId: string | null
  createdAt: string
  updatedAt: string
}

export interface RoutingPinListDto {
  pins: RoutingPinDto[]
}

export const routingPinKeys = {
  all: ['routing-pins'] as const,
}

export function useRoutingPins() {
  return useQuery({
    queryKey: routingPinKeys.all,
    queryFn: () => apiGet<RoutingPinListDto>('/routing-pins'),
    refetchInterval: 15_000,
  })
}
