import { apiGet, apiPost } from './client'

export interface AgentSessionBufferDto {
  sessionId: string
  buffer: string
  lastSequence: number
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
