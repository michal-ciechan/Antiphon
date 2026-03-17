import { create } from 'zustand'
import type { TimelineMessage } from '../features/workflow/types'

export interface ToolCallEntry {
  id: string
  toolName: string
  toolInput: string
  toolOutput?: string
  timestamp: string
  stageId: string
  stageName: string
}

export interface ActivityStatus {
  currentAction: string
  tokensIn: number
  tokensOut: number
  toolCallCount: number
  elapsedMs: number
  timestamp: string
}

interface StreamingState {
  /** Whether the agent is currently streaming output */
  isStreaming: boolean

  /** Accumulated text buffer from AgentTextDelta events */
  textBuffer: string

  /** Messages built from streaming events for the timeline */
  messages: TimelineMessage[]

  /** Active tool calls (collapsible in UI) */
  toolCalls: ToolCallEntry[]

  /** Current activity status from AgentActivityUpdate */
  activity: ActivityStatus | null

  /** The workflow ID currently being streamed */
  activeWorkflowId: string | null

  /** The stage ID currently being streamed */
  activeStageId: string | null

  /** The stage name currently being streamed */
  activeStageName: string | null

  // Actions
  startStreaming: (workflowId: string, stageId: string, stageName: string) => void
  stopStreaming: () => void
  appendTextDelta: (text: string) => void
  addToolCall: (entry: Omit<ToolCallEntry, 'id'>) => void
  updateToolResult: (toolName: string, output: string) => void
  updateActivity: (activity: ActivityStatus) => void
  addMessage: (message: TimelineMessage) => void
  clearMessages: () => void
  reset: () => void
}

let nextToolCallId = 0

export const useStreamingStore = create<StreamingState>((set) => ({
  isStreaming: false,
  textBuffer: '',
  messages: [],
  toolCalls: [],
  activity: null,
  activeWorkflowId: null,
  activeStageId: null,
  activeStageName: null,

  startStreaming: (workflowId, stageId, stageName) =>
    set({
      isStreaming: true,
      textBuffer: '',
      toolCalls: [],
      activity: null,
      activeWorkflowId: workflowId,
      activeStageId: stageId,
      activeStageName: stageName,
    }),

  stopStreaming: () =>
    set((state) => {
      // Flush any remaining text buffer as a final agent message
      const messages = [...state.messages]
      if (state.textBuffer.length > 0 && state.activeStageId && state.activeStageName) {
        messages.push({
          id: `agent-final-${Date.now()}`,
          type: 'agent',
          content: state.textBuffer,
          timestamp: new Date().toISOString(),
          stageId: state.activeStageId,
          stageName: state.activeStageName,
        })
      }
      return {
        isStreaming: false,
        textBuffer: '',
        messages,
        activity: null,
      }
    }),

  appendTextDelta: (text) =>
    set((state) => ({
      textBuffer: state.textBuffer + text,
    })),

  addToolCall: (entry) =>
    set((state) => {
      const id = `tool-${nextToolCallId++}`
      const toolCall: ToolCallEntry = { ...entry, id }

      // Also add as a timeline message
      const message: TimelineMessage = {
        id,
        type: 'tool-call',
        content: `${entry.toolName}`,
        timestamp: entry.timestamp,
        stageId: entry.stageId,
        stageName: entry.stageName,
        toolName: entry.toolName,
        toolInput: entry.toolInput,
      }

      // Flush accumulated text as an agent message before the tool call
      const messages = [...state.messages]
      if (state.textBuffer.length > 0 && state.activeStageId && state.activeStageName) {
        messages.push({
          id: `agent-${Date.now()}`,
          type: 'agent',
          content: state.textBuffer,
          timestamp: entry.timestamp,
          stageId: state.activeStageId,
          stageName: state.activeStageName,
        })
      }
      messages.push(message)

      return {
        toolCalls: [...state.toolCalls, toolCall],
        messages,
        textBuffer: '',
      }
    }),

  updateToolResult: (toolName, output) =>
    set((state) => {
      const toolCalls = state.toolCalls.map((tc) =>
        tc.toolName === toolName && !tc.toolOutput ? { ...tc, toolOutput: output } : tc,
      )

      // Update the corresponding timeline message
      const messages = state.messages.map((msg) =>
        msg.type === 'tool-call' && msg.toolName === toolName && !msg.toolOutput
          ? { ...msg, toolOutput: output }
          : msg,
      )

      return { toolCalls, messages }
    }),

  updateActivity: (activity) => set({ activity }),

  addMessage: (message) =>
    set((state) => ({
      messages: [...state.messages, message],
    })),

  clearMessages: () => set({ messages: [], toolCalls: [] }),

  reset: () =>
    set({
      isStreaming: false,
      textBuffer: '',
      messages: [],
      toolCalls: [],
      activity: null,
      activeWorkflowId: null,
      activeStageId: null,
      activeStageName: null,
    }),
}))
