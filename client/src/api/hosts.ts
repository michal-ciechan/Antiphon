import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPut } from './client'

export interface HostBudgetDto {
  hostId: string
  kind: 'local' | 'runner'
  configuredMaxInFlight: number | null
  declaredCapacity: number | null
  effectiveLimit: number | null
  inFlight: number
  occupiedBreakdown: { sessions: number; pendingLaunch: number; inFlightMirrors: number }
  available: boolean
  dispatchEligible: boolean
  source: string
  reason: string | null
  updatedAt: string | null
  revision: number
}

export const hostsKeys = { all: ['hosts'] as const }

export function useHosts() {
  return useQuery({
    queryKey: hostsKeys.all,
    queryFn: () => apiGet<HostBudgetDto[]>('/hosts'),
    refetchInterval: 15_000,
  })
}

export function usePutHostBudget() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ hostId, maxInFlight, reason }: { hostId: string; maxInFlight: number | null; reason: string }) =>
      apiPut<HostBudgetDto>(`/hosts/${encodeURIComponent(hostId)}/budget`, { maxInFlight, reason }),
    onSuccess: () => void client.invalidateQueries({ queryKey: hostsKeys.all }),
  })
}

export function usePutRunnerCapacity() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ runnerId, capacity, reason }: { runnerId: string; capacity: number; reason: string }) =>
      apiPut(`/session-runners/${encodeURIComponent(runnerId)}/capacity`, { capacity, reason }),
    onSuccess: () => void client.invalidateQueries({ queryKey: hostsKeys.all }),
  })
}
