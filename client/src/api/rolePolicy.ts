import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind } from './boards'
import type { AgentModelLevel } from './agents'
import type { AgentTaskRole } from './agentTasks'
import type { RoutingPinDto, RoutingPinStrength } from './routingPins'

/** CARD-0097 F-1: effective projects the stage pin head filled from the policy, or the policy; not a create result. */
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
  /** null when the configured level is not a defined level (S1 R3); no tier is invented. */
  level: AgentModelLevel | null
  /** null when no kind is configured or the configured kind is undefined (Compose_resolves_alias_from_kind, policy-kind-999). */
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
  /** null when the configured DefaultLevel is not a defined level. */
  defaultLevel: AgentModelLevel | null
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
