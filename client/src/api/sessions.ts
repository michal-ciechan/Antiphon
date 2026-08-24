import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { boardKeys } from './boards'
import { apiDelete, apiGet, apiPost } from './client'

export interface AgentSessionBufferDto {
  sessionId: string
  buffer: string
  lastSequence: number
}

export type TranscriptKind =
  | 'UserPrompt'
  | 'QueuedUserPrompt'
  | 'AssistantText'
  | 'Thinking'
  | 'ToolCall'
  | 'ToolResult'
  | 'TurnTitle'
  | 'TurnEnd'
  | 'CompactBoundary'

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
  /**
   * API-call attribution (assistant entries only): entries of one API call share apiCallId and
   * repeat IDENTICAL usage numbers — group by apiCallId and count usage once per call.
   */
  apiCallId?: string | null
  inputTokens?: number | null
  outputTokens?: number | null
  cacheReadTokens?: number | null
  cacheCreationTokens?: number | null
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
  /** How many times this has been typed into the terminal (CARD-0055). */
  deliveryAttempts: number
  /** Who enqueued it: `Ui`, `Channel`, `Check`, `Delegation`… */
  origin: string
  /**
   * Pending AND out of delivery attempts. Parking is not a status — a parked message looks exactly
   * like a pending one on `status` — so this flag is the ONLY thing that tells a human nothing
   * automatic will ever type it again. The server computes it against the same setting the
   * attention projection reads, so the queue and that view cannot disagree.
   */
  parked: boolean
}

/** CARD-0180 S3: how a Mode:Now delivery was confirmed. */
export interface DeliveryReceiptDto {
  verdict: string
  confirmedBy: 'transcript' | 'screen' | 'none' | string
  degraded: boolean
  reason: string | null
  at: string
}

/** Pending messages for a session, plus whether the agent is currently working. */
export interface SessionQueueDto {
  sessionId: string
  messages: QueuedMessageDto[]
  working: boolean
  /** Populated only on the Mode:Now response. */
  lastDelivery?: DeliveryReceiptDto | null
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

/** A slash-command/skill suggestion for the composer's `/` autocomplete. */
export interface SlashCommandDto {
  name: string
  description: string
  source: string
  scope: string
}

export async function getSessionCommands(sessionId: string) {
  return apiGet<SlashCommandDto[]>(`/sessions/${sessionId}/commands`)
}

/**
 * Available slash-commands + skills for a session's `/` autocomplete. `staleTime` matches the
 * server's catalog cache (~10s) so typing doesn't re-fetch; only enabled once the user types `/`.
 */
export function useSessionCommands(sessionId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['session', sessionId, 'commands'],
    queryFn: () => getSessionCommands(sessionId),
    enabled,
    staleTime: 10_000,
    gcTime: 60_000,
  })
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
