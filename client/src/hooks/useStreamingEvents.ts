import { useEffect, type RefObject } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { useStreamingStore } from '../stores/streamingStore'

/**
 * Subscribes to SignalR streaming events (AgentTextDelta, AgentToolCall,
 * AgentToolResult, AgentActivityUpdate) and pushes them into the streaming store.
 *
 * This hook should be called once at the app level alongside useSignalR.
 */
export function useStreamingEvents(connectionRef: RefObject<HubConnection | null>) {
  const appendTextDelta = useStreamingStore((s) => s.appendTextDelta)
  const addToolCall = useStreamingStore((s) => s.addToolCall)
  const updateToolResult = useStreamingStore((s) => s.updateToolResult)
  const updateActivity = useStreamingStore((s) => s.updateActivity)
  const startStreaming = useStreamingStore((s) => s.startStreaming)
  const stopStreaming = useStreamingStore((s) => s.stopStreaming)

  useEffect(() => {
    const connection = connectionRef.current
    if (!connection) return

    type TextDeltaPayload = {
      workflowId: string
      stageId: string
      text: string
      timestamp: string
    }

    type ToolCallPayload = {
      workflowId: string
      stageId: string
      toolName: string
      toolInput: string
      timestamp: string
    }

    type ToolResultPayload = {
      workflowId: string
      stageId: string
      toolName: string
      toolOutput: string
      timestamp: string
    }

    type ActivityPayload = {
      workflowId: string
      stageId: string
      currentAction: string
      tokensIn: number
      tokensOut: number
      toolCallCount: number
      elapsedMs: number
      timestamp: string
    }

    type StageStartedPayload = {
      workflowId: string
      stageId: string
      stageName: string
    }

    const onStageStarted = (payload: StageStartedPayload) => {
      startStreaming(payload.workflowId, payload.stageId, payload.stageName)
    }

    const onTextDelta = (payload: TextDeltaPayload) => {
      appendTextDelta(payload.text)
    }

    const onToolCall = (payload: ToolCallPayload) => {
      const store = useStreamingStore.getState()
      addToolCall({
        toolName: payload.toolName,
        toolInput: payload.toolInput,
        timestamp: payload.timestamp,
        stageId: store.activeStageId ?? payload.stageId,
        stageName: store.activeStageName ?? 'Unknown',
      })
    }

    const onToolResult = (payload: ToolResultPayload) => {
      updateToolResult(payload.toolName, payload.toolOutput)
    }

    const onActivity = (payload: ActivityPayload) => {
      updateActivity({
        currentAction: payload.currentAction,
        tokensIn: payload.tokensIn,
        tokensOut: payload.tokensOut,
        toolCallCount: payload.toolCallCount,
        elapsedMs: payload.elapsedMs,
        timestamp: payload.timestamp,
      })
    }

    const onStageCompleted = () => {
      stopStreaming()
    }

    connection.on('StageStarted', onStageStarted)
    connection.on('AgentTextDelta', onTextDelta)
    connection.on('AgentToolCall', onToolCall)
    connection.on('AgentToolResult', onToolResult)
    connection.on('AgentActivityUpdate', onActivity)
    connection.on('StageCompleted', onStageCompleted)

    return () => {
      connection.off('StageStarted', onStageStarted)
      connection.off('AgentTextDelta', onTextDelta)
      connection.off('AgentToolCall', onToolCall)
      connection.off('AgentToolResult', onToolResult)
      connection.off('AgentActivityUpdate', onActivity)
      connection.off('StageCompleted', onStageCompleted)
    }
  }, [
    connectionRef,
    appendTextDelta,
    addToolCall,
    updateToolResult,
    updateActivity,
    startStreaming,
    stopStreaming,
  ])
}
