import { useMutation, useQueryClient } from '@tanstack/react-query'
import { boardKeys } from './boards'
import { apiGet, apiPost } from './client'

export interface AgentSessionBufferDto {
  sessionId: string
  buffer: string
  lastSequence: number
}

export type TranscriptKind =
  | 'UserPrompt'
  | 'AssistantText'
  | 'Thinking'
  | 'ToolCall'
  | 'ToolResult'
  | 'TurnTitle'
  | 'TurnEnd'

export interface TranscriptEntryDto {
  sequence: number
  kind: TranscriptKind | string
  uuid: string | null
  parentUuid: string | null
  timestamp: string | null
  role: string | null
  text: string | null
  toolName: string | null
  toolInput: string | null
  toolUseId: string | null
  toolIsError: boolean | null
  stopReason: string | null
}

export interface SessionTranscriptDto {
  sessionId: string
  entries: TranscriptEntryDto[]
  lastSequence: number
}

/** Live SignalR `SessionTranscript` payload — a transcript entry plus its session id. */
export interface SessionTranscriptPayload extends TranscriptEntryDto {
  sessionId: string
}

export async function getSessionTranscript(sessionId: string, since = 0) {
  return apiGet<SessionTranscriptDto>(`/sessions/${sessionId}/transcript?since=${since}`)
}

export interface AgentSessionResumeResult {
  sessionId: string
  cardId: string
}

export type AgentSessionResumeMode = 'Resume' | 'Continue' | 'New'

export async function getSessionBuffer(sessionId: string) {
  return apiGet<AgentSessionBufferDto>(`/sessions/${sessionId}/buffer`)
}

export async function sendSessionInput(sessionId: string, input: string) {
  return apiPost<void>(`/sessions/${sessionId}/input`, { input })
}

export async function resizeSession(sessionId: string, cols: number, rows: number) {
  return apiPost<void>(`/sessions/${sessionId}/resize`, { cols, rows })
}

export async function resumeSession(sessionId: string, mode: AgentSessionResumeMode = 'Resume') {
  return apiPost<AgentSessionResumeResult>(`/sessions/${sessionId}/resume`, { mode })
}

export async function stopSession(sessionId: string) {
  return apiPost<void>(`/sessions/${sessionId}/kill`, {})
}

export function useResumeSession(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ sessionId, mode = 'Resume' }: { sessionId: string; mode?: AgentSessionResumeMode }) =>
      resumeSession(sessionId, mode),
    onSettled: () => {
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
