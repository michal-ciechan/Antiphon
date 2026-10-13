import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPut } from './client'

/**
 * Dispatch concurrency settings (CARD-0505 backend, CARD-0506 UI). Types mirror
 * `server/Application/Dtos/DispatchConcurrencyDtos.cs`; field nullability follows the server record.
 */

export interface DispatchConcurrencyRangeDto {
  parallelMin: number
  parallelMax: number
  queuedMin: number
  queuedMax: number
}

export interface DispatchConcurrencySeedRoleDto {
  role: string
  maxParallel: number | null
  maxQueued: number | null
}

export interface DispatchConcurrencySeedDto {
  origin: string
  importedAt: string
  mode: string
  maxParallel: number
  maxQueued: number | null
  roles: DispatchConcurrencySeedRoleDto[]
}

export interface DispatchConcurrencyRoleDto {
  role: string
  maxParallel: number | null
  maxParallelSource: string
  maxQueued: number | null
  maxQueuedSource: string
  combinedParallel: number | null
}

export interface DispatchConcurrencyEffectiveDto {
  mode: string
  modeSource: string
  maxParallel: number
  maxParallelSource: string
  maxQueued: number | null
  maxQueuedSource: string
  roles: DispatchConcurrencyRoleDto[]
  globalRevision: number
  projectRevision: number
}

export interface DispatchConcurrencyRoleOccupancyDto {
  role: string
  open: number
  parallel: number
  queued: number
  parallelRemaining: number | null
  queuedRemaining: number | null
  parallelOverage: number
  queuedOverage: number
}

export interface DispatchConcurrencyOccupancyDto {
  open: number
  parallel: number
  queued: number
  parallelRemaining: number | null
  queuedRemaining: number | null
  parallelOverage: number
  queuedOverage: number
  exceedsBounds: boolean
  roles: DispatchConcurrencyRoleOccupancyDto[]
}

/**
 * The canonical overrides document exactly as the server stores it: a key is an override only when
 * present, and a present `null` on a nullable field means "specified, unbounded". The client never
 * reads it directly; `dispatchConcurrencyModel.ts` decodes it into a draft.
 */
export type DispatchConcurrencyOverrides = Record<string, unknown>

export interface DispatchConcurrencyGlobalDto {
  seed: DispatchConcurrencySeedDto
  overrides: DispatchConcurrencyOverrides
  effective: DispatchConcurrencyEffectiveDto
  nullProject: DispatchConcurrencyEffectiveDto
  supportedRoles: string[]
  ranges: DispatchConcurrencyRangeDto
  revision: number
  updatedAt: string
  reason: string
  provenance: string
  callerTaskId: string | null
  occupancy: DispatchConcurrencyOccupancyDto
}

export interface DispatchConcurrencyProjectDto {
  projectId: string
  overrides: DispatchConcurrencyOverrides
  inherited: DispatchConcurrencyEffectiveDto
  effective: DispatchConcurrencyEffectiveDto
  supportedRoles: string[]
  ranges: DispatchConcurrencyRangeDto
  revision: number
  globalRevision: number
  updatedAt: string | null
  reason: string | null
  provenance: string | null
  callerTaskId: string | null
  occupancy: DispatchConcurrencyOccupancyDto
}

export type DispatchConcurrencyScopeDto = DispatchConcurrencyGlobalDto | DispatchConcurrencyProjectDto

export function isProjectScope(dto: DispatchConcurrencyScopeDto): dto is DispatchConcurrencyProjectDto {
  return Object.hasOwn(dto, 'projectId')
}

export interface DispatchConcurrencyRevisionDto {
  revision: number
  previousRevision: number | null
  /** Read-only history envelope (`scopeKey`, `revision`, `provenance`, `reason`, `overrides`, …). */
  snapshot: unknown
  createdAt: string
  reason: string
  provenance: string
  callerTaskId: string | null
}

export interface DispatchConcurrencyRevisionPageDto {
  revisions: DispatchConcurrencyRevisionDto[]
  nextBeforeRevision: number | null
}

/**
 * What the editor hands the mutation. The loaded scope's `revision` (0 for a project without a
 * row) and, for a project, its `globalRevision`; the global scope passes its own revision twice.
 */
export interface PutDispatchConcurrencyInput {
  expectedRevision: number
  expectedGlobalRevision: number
  overrides: DispatchConcurrencyOverrides
  reason: string
}

/**
 * The wire body. The endpoint parses it by hand and rejects any extra top-level key, so this is
 * built field by field from the input and never spread from a DTO (test design V-5).
 */
export interface PutDispatchConcurrencyRequest extends PutDispatchConcurrencyInput {
  provenance: 'Human'
}

const HISTORY_LIMIT = 10

function scopePath(projectId: string | null | undefined): string {
  return projectId ? `/projects/${encodeURIComponent(projectId)}/dispatch-concurrency` : '/dispatch-concurrency'
}

/**
 * Every key shares the `dispatchConcurrency` prefix so one invalidation reaches all of them. The
 * global scope is `null`, not a sentinel string, so no project id can share its cache entry.
 */
export const dispatchConcurrencyKeys = {
  all: ['dispatchConcurrency'] as const,
  scope: (projectId: string | null | undefined) => ['dispatchConcurrency', 'scope', projectId || null] as const,
  revisions: (projectId: string | null | undefined) => ['dispatchConcurrency', 'revisions', projectId || null] as const,
}

export function useDispatchConcurrency(projectId?: string | null) {
  return useQuery({
    queryKey: dispatchConcurrencyKeys.scope(projectId),
    queryFn: () => apiGet<DispatchConcurrencyScopeDto>(scopePath(projectId)),
    refetchOnWindowFocus: true,
  })
}

export function useDispatchConcurrencyRevisions(projectId?: string | null) {
  return useQuery({
    queryKey: dispatchConcurrencyKeys.revisions(projectId),
    queryFn: () => apiGet<DispatchConcurrencyRevisionPageDto>(`${scopePath(projectId)}/revisions?limit=${HISTORY_LIMIT}`),
    refetchOnWindowFocus: true,
  })
}

export function toPutDispatchConcurrencyRequest(input: PutDispatchConcurrencyInput): PutDispatchConcurrencyRequest {
  return {
    expectedRevision: input.expectedRevision,
    expectedGlobalRevision: input.expectedGlobalRevision,
    overrides: input.overrides,
    reason: input.reason.trim(),
    provenance: 'Human',
  }
}

export function usePutDispatchConcurrency(projectId?: string | null) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: PutDispatchConcurrencyInput) =>
      apiPut<DispatchConcurrencyScopeDto>(scopePath(projectId), toPutDispatchConcurrencyRequest(input)),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: dispatchConcurrencyKeys.all })
    },
  })
}
