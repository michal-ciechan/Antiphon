import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind } from './boards'

export interface AgentRegistryDto {
  defaultDefinition: string
  definitions: AgentDefinitionDto[]
}

export interface AgentDefinitionDto {
  name: string
  kind: AgentKind
  isDefault: boolean
}

export function useAgents() {
  return useQuery({
    queryKey: ['agents'],
    queryFn: () => apiGet<AgentRegistryDto>('/agents'),
  })
}
