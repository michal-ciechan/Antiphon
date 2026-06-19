import { useMutation, useQueryClient } from '@tanstack/react-query'
import { boardKeys } from './boards'
import { apiDelete, apiGet, apiPost } from './client'

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

export type MessageSendMode = 'Now' | 'WhenIdle'

export interface QueuedMessageDto {
  id: string
  sequence: number
  body: string
  status: string
  createdAt: string
}

/** Pending messages for a session, plus whether the agent is currently working. */
export interface SessionQueueDto {
  sessionId: string
  messages: QueuedMessageDto[]
  working: boolean
}

/** Global SignalR `SessionFinished` payload — broadcast when an agent finishes with an empty queue. */
export interface SessionFinishedPayload {
  sessionId: string
  cardId: string | null
  boardId: string | null
  agentId: string | null
  label: string
}

export async function getSessionQueue(sessionId: string) {
  return apiGet<SessionQueueDto>(`/sessions/${sessionId}/messages`)
}

export async function enqueueSessionMessage(sessionId: string, body: string, mode: MessageSendMode) {
  return apiPost<SessionQueueDto>(`/sessions/${sessionId}/messages`, { body, mode })
}

export async function cancelQueuedMessage(sessionId: string, messageId: string) {
  return apiDelete<SessionQueueDto>(`/sessions/${sessionId}/messages/${messageId}`)
}

export async function sendQueuedMessageNow(sessionId: string, messageId: string) {
  return apiPost<SessionQueueDto>(`/sessions/${sessionId}/messages/${messageId}/send-now`, {})
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
