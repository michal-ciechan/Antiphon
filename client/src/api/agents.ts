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

export const agentKeys = {
  definitions: ['agents', 'definitions'] as const,
  all: ['agents', 'list'] as const,
  detail: (id: string) => ['agents', 'detail', id] as const,
  queue: (id: string) => ['agents', 'queue', id] as const,
}

export function useAgentDefinitions() {
  return useQuery({
    queryKey: agentKeys.definitions,
    queryFn: () => apiGet<AgentRegistryDto>('/agents/definitions'),
  })
}
