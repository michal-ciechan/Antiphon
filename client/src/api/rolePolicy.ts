import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind } from './boards'
import type { AgentModelLevel } from './agents'
import type { AgentTaskRole } from './agentTasks'
import type { RoutingPinDto, RoutingPinStrength } from './routingPins'

/** CARD-0097 F-1: where the next create's target comes from. */
export type RolePolicySource = 'pin' | 'rolePolicy'

export interface RolePolicyEffectiveDto {
  source: RolePolicySource
  agentKind: AgentKind | null
  modelLevel: AgentModelLevel | null
  alias: string | null
  strength: RoutingPinStrength | null
}

export interface RolePolicyRowDto {
  role: AgentTaskRole
  configured: boolean
  level: AgentModelLevel
  /** null means ClaudeCode. */
  kind: AgentKind | null
  alias: string
  escalateTo: AgentModelLevel | null
  escalateAfterMinutes: number | null
  timeoutMinutes: number
  /** The active stage-wide pin (cardId null) with its evaluated candidates. */
  stagePin: RoutingPinDto | null
  effective: RolePolicyEffectiveDto
}

export interface RolePolicyDto {
  defaultLevel: AgentModelLevel
  defaultTimeoutMinutes: number
  roles: RolePolicyRowDto[]
}

export const rolePolicyKeys = {
  all: ['role-policy'] as const,
}

export function useRolePolicy() {
  return useQuery({
    queryKey: rolePolicyKeys.all,
    queryFn: () => apiGet<RolePolicyDto>('/role-policy'),
    refetchInterval: 15_000,
  })
}
