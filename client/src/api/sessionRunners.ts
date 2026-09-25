import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'

export interface SessionRunnerCatalogueEntry {
  runnerId: string
  displayName: string
  platform: string | null
  platformObservedAt: string | null
  available: boolean
  dispatchEligible: boolean
  unavailableReason: string | null
  capacity: number | null
  occupied: number | null
  capacityKind: string
  capacityObservedAt: string | null
  stale: boolean
  features: string[]
}

export const sessionRunnerKeys = {
  all: ['sessionRunners'] as const,
}

export function useSessionRunners(enabled = true) {
  return useQuery({
    queryKey: sessionRunnerKeys.all,
    queryFn: () => apiGet<SessionRunnerCatalogueEntry[]>('/session-runners'),
    enabled,
    refetchInterval: enabled ? 15_000 : false,
  })
}
