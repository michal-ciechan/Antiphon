import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPut } from './client'

export type HostMetric = 'cpu' | 'load' | 'memory'
export type HostWindow = '1m' | '5m' | '15m' | '30m'
export const HOST_WINDOWS: HostWindow[] = ['1m', '5m', '15m', '30m']
export interface HostRollup { avg: number; max: number }
export interface HostWindowRollups {
  cpuPercent: HostRollup | null
  load1: HostRollup | null
  memoryUsedBytes: HostRollup | null
}
export interface HostCurrent {
  cpuPercent: number | null
  load1: number | null
  load5: number | null
  load15: number | null
  memoryUsedBytes: number | null
  memoryAvailableBytes: number | null
  memoryTotalBytes: number | null
  swapUsedBytes: number | null
  swapTotalBytes: number | null
  disks: Array<{ path: string; freeBytes: number; totalBytes: number }>
  processCount: number | null
  processes: Array<{ name: string; sessionId: string | null; cpuPercent: number | null; workingSetBytes: number }>
}
export interface HostStats {
  hostId: string
  displayName: string
  platform: string | null
  state: 'live' | 'stale' | 'offline' | 'unsupported'
  reason: string | null
  observedAt: string | null
  intervalSeconds: number | null
  cores: number | null
  current: HostCurrent | null
  rollups: Record<string, HostWindowRollups> | null
  antiphon: {
    tasksInFlight: number
    byStage: Record<string, number>
    byKind: Record<string, number>
    queued: number
    held: number
    landsPending: number
    sessionsLive: number | null
    seatsDeclared: number | null
    buildSlots: { occupied: number; budget: number; waiters: number; availableMb: number | null } | null
  }
}
export interface HostSeries {
  hostId: string
  metric: HostMetric
  window: HostWindow
  intervalSeconds: number
  points: Array<{ t: string; v: number }>
}
export const hostKeys = {
  stats: ['hosts', 'stats'] as const,
  series: (id: string, metric: HostMetric, window: HostWindow) => ['hosts', id, 'series', metric, window] as const,
}
export function useHostStats() {
  return useQuery({ queryKey: hostKeys.stats, queryFn: () => apiGet<HostStats[]>('/hosts/stats') })
}
export function useHostSeries(hostId: string, metric: HostMetric, window: HostWindow, enabled = true) {
  return useQuery({
    queryKey: hostKeys.series(hostId, metric, window),
    queryFn: () => apiGet<HostSeries>(`/hosts/${encodeURIComponent(hostId)}/stats/series?metric=${metric}&window=${window}`),
    enabled: !!hostId && enabled,
    retry: false,
  })
}

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

export const hostsKeys = { all: ['hosts', 'budgets'] as const }

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
