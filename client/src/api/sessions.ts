import { useMutation, useQueryClient } from '@tanstack/react-query'
import { boardKeys } from './boards'
import { apiGet, apiPost } from './client'

export interface AgentSessionBufferDto {
  sessionId: string
  buffer: string
  lastSequence: number
}

export interface AgentSessionResumeResult {
  sessionId: string
  cardId: string
}

export async function getSessionBuffer(sessionId: string) {
  return apiGet<AgentSessionBufferDto>(`/sessions/${sessionId}/buffer`)
}

export async function sendSessionInput(sessionId: string, input: string) {
  return apiPost<void>(`/sessions/${sessionId}/input`, { input })
}

export async function resizeSession(sessionId: string, cols: number, rows: number) {
  return apiPost<void>(`/sessions/${sessionId}/resize`, { cols, rows })
}

export async function resumeSession(sessionId: string) {
  return apiPost<AgentSessionResumeResult>(`/sessions/${sessionId}/resume`, {})
}

export async function stopSession(sessionId: string) {
  return apiPost<void>(`/sessions/${sessionId}/kill`, {})
}

export function useResumeSession(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: resumeSession,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useStopSession(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: stopSession,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}
