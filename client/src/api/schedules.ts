import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPatch, apiPost } from './client'
import type { CardStatus } from './boards'

export type ScheduleKind = 'Prompt' | 'Card'
export type ScheduleRepeat = 'Once' | 'Interval' | 'Daily'
export type ScheduleStart = 'None' | 'Release' | 'Spawn'
export type ScheduleWhenTargetDown = 'Queue' | 'Skip'
export type ScheduleFireOutcome =
  | 'Claimed'
  | 'Delivered'
  | 'Enqueued'
  | 'QueuedForRelaunch'
  | 'SkippedNoSession'
  | 'SkippedLate'
  | 'SkippedTargetGone'
  | 'Moved'
  | 'Released'
  | 'Spawned'
  | 'Refused'
  | 'Failed'

export interface ScheduleFireDto {
  id: string
  fireNumber: number
  dueAt: string
  claimedAt: string
  completedAt: string | null
  outcome: ScheduleFireOutcome
  detail: string | null
  queuedMessageId: string | null
  spawnedSessionId: string | null
  manual: boolean
}

export interface ScheduleDto {
  id: string
  name: string
  kind: ScheduleKind
  repeat: ScheduleRepeat
  repeatDescription: string
  timeZoneId: string
  nextFireAt: string | null
  nextFireAtLocal: string | null
  enabled: boolean
  missedGraceMinutes: number | null
  fireCount: number
  lastFiredAt: string | null
  lastOutcome: ScheduleFireOutcome | null
  lastOutcomeDetail: string | null
  createdBy: string | null
  createdAt: string
  updatedAt: string
  concurrencyToken: string
  agentId: string | null
  agentName: string | null
  agentSlug: string | null
  promptText: string | null
  whenTargetDown: ScheduleWhenTargetDown
  cardId: string | null
  cardIdentifier: string | null
  targetStatus: CardStatus | null
  start: ScheduleStart
  spendAcceptedAt: string | null
  spendAcceptedBy: string | null
  fireAt: string | null
  everyMinutes: number | null
  anchorAt: string | null
  atLocal: string | null
  daysOfWeek: number
  fires?: ScheduleFireDto[] | null
}

export interface ScheduleListDto {
  schedules: ScheduleDto[]
}

export interface CreateScheduleRequest {
  name: string
  kind?: ScheduleKind
  repeat?: ScheduleRepeat
  timeZoneId?: string
  agent?: string
  promptText?: string
  whenTargetDown?: ScheduleWhenTargetDown
  fireAt?: string
  everyMinutes?: number
  atLocal?: string
  daysOfWeek?: number
  createdBy?: string
  cardId?: string
  targetStatus?: CardStatus
  start?: ScheduleStart
  acceptSpend?: boolean
}

export const scheduleKeys = {
  all: ['schedules'] as const,
  agent: (agentId: string) => ['schedules', 'agent', agentId] as const,
  card: (cardId: string) => ['schedules', 'card', cardId] as const,
  detail: (id: string) => ['schedules', 'detail', id] as const,
}

export function useAgentSchedules(agentId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: agentId ? scheduleKeys.agent(agentId) : scheduleKeys.all,
    queryFn: () => apiGet<ScheduleListDto>(`/schedules?agentId=${agentId}`),
    enabled: Boolean(agentId) && enabled,
  })
}

export function useCardSchedules(cardId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: cardId ? scheduleKeys.card(cardId) : scheduleKeys.all,
    queryFn: () => apiGet<ScheduleListDto>(`/schedules?cardId=${cardId}`),
    enabled: Boolean(cardId) && enabled,
  })
}

export function useCreateSchedule(agentId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateScheduleRequest) => apiPost<ScheduleDto>('/schedules', body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: scheduleKeys.all })
      if (agentId) void queryClient.invalidateQueries({ queryKey: scheduleKeys.agent(agentId) })
    },
  })
}

export function usePatchSchedule(agentId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: { concurrencyToken: string; enabled?: boolean } }) =>
      apiPatch<ScheduleDto>(`/schedules/${id}`, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: scheduleKeys.all })
      if (agentId) void queryClient.invalidateQueries({ queryKey: scheduleKeys.agent(agentId) })
    },
  })
}

export function useDeleteSchedule(agentId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/schedules/${id}`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: scheduleKeys.all })
      if (agentId) void queryClient.invalidateQueries({ queryKey: scheduleKeys.agent(agentId) })
    },
  })
}

export function useFireScheduleNow(agentId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiPost<void>(`/schedules/${id}/fire-now`, {}),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: scheduleKeys.all })
      if (agentId) void queryClient.invalidateQueries({ queryKey: scheduleKeys.agent(agentId) })
    },
  })
}

export function spendModeInWords(schedule: ScheduleDto): string {
  const when = formatNextLocal(schedule)
  if (schedule.kind !== 'Card') {
    return when ? `next fire ${when}` : 'no next fire'
  }
  if (schedule.start === 'None') {
    return when
      ? `bookkeeping move on ${when} — will not start a session`
      : 'bookkeeping move — will not start a session'
  }
  if (schedule.start === 'Release') {
    return when
      ? `will start a session on ${when} (orchestrator, under cap)`
      : 'will start a session (orchestrator, under cap)'
  }
  return when
    ? `will start a session on ${when} (immediate, bypassing caps)`
    : 'will start a session (immediate, bypassing caps)'
}

export function formatNextLocal(schedule: ScheduleDto): string | null {
  if (!schedule.nextFireAtLocal && !schedule.nextFireAt) return null
  const raw = schedule.nextFireAtLocal ?? schedule.nextFireAt
  if (!raw) return null
  const date = new Date(raw)
  if (Number.isNaN(date.getTime())) return raw
  return date.toLocaleString(undefined, {
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short',
  })
}
