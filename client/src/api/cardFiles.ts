import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPost, apiPut } from './client'

export type CardFileVisibility = 'Inherit' | 'Private' | 'Public'
export type RepositoryVisibility = 'Unknown' | 'Private' | 'Public'
export interface CardFileStatus {
  boardId: string
  enabled: boolean
  syncCardFiles: boolean
  repositoryVisibility: RepositoryVisibility
  visibilitySource: 'Unknown' | 'Configured'
  repositoryPath: string | null
  directory: string | null
  eligible: boolean
  reason: string | null
  warnings: string[]
  ignored: boolean | null
  workingTreeRemovalPending: boolean | null
  gitRemovalPending: boolean | null
  removalPending: boolean
  autoCommit: boolean
  intervalSeconds: number
  cardFileVisibility?: CardFileVisibility
  relativeFile?: string | null
}
export interface CardFileSyncResult {
  written: number
  deleted: number
  unchanged: number
  eligibleCards: number
  excludedCards: number
  writeSkipReason: string | null
  commitSkipReason: string | null
  error: string | null
  dryRun: boolean
  policy: CardFileStatus
}
export interface CardPrivateNotes {
  cardId: string
  revisionNumber: number | null
  privateNotes: string | null
  concurrencyToken: string
}
export function useCardFileStatus(boardId: string) {
  return useQuery({ queryKey: ['card-file-status', boardId], queryFn: () => apiGet<CardFileStatus>(`/boards/${boardId}/card-files/status`) })
}
export function useCardFileSettings(boardId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (request: { syncCardFiles: boolean; expectedSyncCardFiles: boolean }) => apiPut<CardFileStatus>(`/boards/${boardId}/card-files/settings`, request),
    onSettled: () => { void client.invalidateQueries({ queryKey: ['card-file-status', boardId] }); void client.invalidateQueries({ queryKey: ['boards'] }) },
  })
}
export function useSyncCardFiles(boardId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (dryRun: boolean) => apiPost<CardFileSyncResult>(`/boards/${boardId}/card-files/sync?dryRun=${dryRun}`, {}),
    onSettled: () => { void client.invalidateQueries({ queryKey: ['card-file-status', boardId] }) },
  })
}
// Explicit, memory-only query. It is never mapped into card/board DTO caches.
export function usePrivateNotes(cardId: string, enabled: boolean, revisionNumber?: number) {
  return useQuery({
    queryKey: ['private-notes', cardId, revisionNumber ?? 'current'],
    queryFn: () => apiGet<CardPrivateNotes>(`/cards/${cardId}/private-notes${revisionNumber === undefined ? '' : `?revisionNumber=${revisionNumber}`}`),
    enabled, gcTime: 0, meta: { persist: false },
  })
}
