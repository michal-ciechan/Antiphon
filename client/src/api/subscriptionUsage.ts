import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind } from './boards'

/**
 * Display-safe observation from GET /api/subscription-usage (CARD-0333 S1).
 * `age` is the .NET TimeSpan string (e.g. `00:15:00`). Nulls are preserved.
 */
export interface SubscriptionUsageObservationDto {
  provider: AgentKind
  planLabel: string | null
  remainingPercent: number | null
  resetsAt: string | null
  observedAt: string
  age: string
}

export const subscriptionUsageKeys = {
  all: ['subscription-usage'] as const,
}

export function useSubscriptionUsage() {
  return useQuery({
    queryKey: subscriptionUsageKeys.all,
    queryFn: () => apiGet<SubscriptionUsageObservationDto[]>('/subscription-usage'),
    refetchInterval: 15_000,
  })
}
