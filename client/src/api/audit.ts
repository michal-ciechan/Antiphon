import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet } from './client'

// --- Types ---

export interface AuditRecordDto {
  id: string
  workflowId: string | null
  stageId: string | null
  stageExecutionId: string | null
  eventType: string
  modelName: string | null
  tokensIn: number
  tokensOut: number
  costUsd: number
  durationMs: number
  clientIp: string | null
  gitTagName: string | null
  userId: string | null
  summary: string
  fullContent: string | null
  createdAt: string
}

export interface CostByModelDto {
  modelName: string
  costUsd: number
  tokensIn: number
  tokensOut: number
  callCount: number
}

export interface CostSummaryDto {
  totalCostUsd: number
  totalTokensIn: number
  totalTokensOut: number
  totalLlmCalls: number
  totalToolCalls: number
  byModel: CostByModelDto[]
}

export interface AuditQueryResult {
  records: AuditRecordDto[]
  totalCount: number
  costSummary: CostSummaryDto
}

export interface CostLedgerEntryDto {
  id: string
  workflowId: string
  stageId: string
  stageExecutionId: string | null
  modelName: string
  tokensIn: number
  tokensOut: number
  costUsd: number
  durationMs: number
  createdAt: string
}

export interface ArchiveResultDto {
  archivedCount: number
  olderThan: string
}

// --- Query params ---

interface AuditQueryParams {
  workflowId?: string
  stageId?: string
  from?: string
  to?: string
  minCost?: number
  maxCost?: number
  skip?: number
  take?: number
}

// --- Helpers ---

function buildQueryString(params: Record<string, string | number | undefined>): string {
  const entries = Object.entries(params).filter(
    ([, v]) => v !== undefined && v !== null && v !== '',
  )
  if (entries.length === 0) return ''
  return '?' + entries.map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join('&')
}

// --- Query hooks ---

export function useAuditQuery(params: AuditQueryParams) {
  const qs = buildQueryString(params as Record<string, string | number | undefined>)
  return useQuery({
    queryKey: ['audit', params],
    queryFn: () => apiGet<AuditQueryResult>(`/audit${qs}`),
    enabled: !!(params.workflowId || params.stageId),
  })
}

export function useCostSummary(params: { workflowId?: string; stageId?: string }) {
  const qs = buildQueryString(params as Record<string, string | undefined>)
  return useQuery({
    queryKey: ['audit', 'cost-summary', params],
    queryFn: () => apiGet<CostSummaryDto>(`/audit/cost-summary${qs}`),
    enabled: !!(params.workflowId || params.stageId),
  })
}

export function useCostLedger(params: AuditQueryParams) {
  const qs = buildQueryString(params as Record<string, string | number | undefined>)
  return useQuery({
    queryKey: ['audit', 'cost-ledger', params],
    queryFn: () => apiGet<CostLedgerEntryDto[]>(`/audit/cost-ledger${qs}`),
    enabled: !!(params.workflowId || params.stageId),
  })
}

export function useArchiveAudit() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (olderThanDays: number): Promise<ArchiveResultDto> => {
      const response = await fetch(`/api/audit/archive?olderThanDays=${olderThanDays}`, {
        method: 'DELETE',
      })
      if (!response.ok) {
        throw new Error(`Archive failed: ${response.statusText}`)
      }
      return response.json() as Promise<ArchiveResultDto>
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['audit'] })
    },
  })
}
