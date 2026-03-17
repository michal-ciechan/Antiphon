/**
 * Message and timeline types for the ConversationTimeline (Story 2.8).
 */

export type MessageType =
  | 'agent'
  | 'user-prompt'
  | 'user-comment'
  | 'system-event'
  | 'tool-call'

export interface TimelineMessage {
  id: string
  type: MessageType
  content: string
  timestamp: string
  /** Stage this message belongs to */
  stageId: string
  stageName: string
  /** For agent messages, optional model info */
  model?: string
  /** For tool calls */
  toolName?: string
  toolInput?: string
  toolOutput?: string
  /** For system events */
  eventType?: string
  /** For user messages, the author name */
  author?: string
}

export interface StageGroup {
  stageId: string
  stageName: string
  stageOrder: number
  version: number
  messageCount: number
  firstTimestamp: string
  lastTimestamp: string
  gateDecision?: 'approved' | 'rejected' | 'go-back'
  messages: TimelineMessage[]
}

export interface ArtifactDto {
  id: string
  stageId: string
  stageName: string
  fileName: string
  content: string
  version: number
  isPrimary: boolean
  createdAt: string
}
