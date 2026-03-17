import { useMemo } from 'react'
import { Box, Text, Stack } from '@mantine/core'
import { StageMarker } from './StageMarker'
import {
  AgentMessage,
  UserPromptMessage,
  UserCommentMessage,
  SystemEvent,
  ToolCallBlock,
} from './messages'
import { useAutoScroll } from './useAutoScroll'
import type { TimelineMessage, StageGroup } from './types'

interface ConversationTimelineProps {
  messages: TimelineMessage[]
  /** The stage ID that is currently active (expanded by default) */
  currentStageId?: string
  /** Whether the agent is currently streaming */
  isStreaming?: boolean
}

/**
 * Groups messages by stage, maintaining chronological order within each group.
 */
function groupByStage(messages: TimelineMessage[]): StageGroup[] {
  const stageMap = new Map<string, StageGroup>()
  const stageOrder: string[] = []

  for (const msg of messages) {
    let group = stageMap.get(msg.stageId)
    if (!group) {
      group = {
        stageId: msg.stageId,
        stageName: msg.stageName,
        stageOrder: stageOrder.length,
        version: 1,
        messageCount: 0,
        firstTimestamp: msg.timestamp,
        lastTimestamp: msg.timestamp,
        messages: [],
      }
      stageMap.set(msg.stageId, group)
      stageOrder.push(msg.stageId)
    }
    group.messages.push(msg)
    group.messageCount++
    group.lastTimestamp = msg.timestamp
  }

  return stageOrder.map((id) => stageMap.get(id)!)
}

function renderMessage(message: TimelineMessage) {
  switch (message.type) {
    case 'agent':
      return <AgentMessage key={message.id} message={message} />
    case 'user-prompt':
      return <UserPromptMessage key={message.id} message={message} />
    case 'user-comment':
      return <UserCommentMessage key={message.id} message={message} />
    case 'system-event':
      return <SystemEvent key={message.id} message={message} />
    case 'tool-call':
      return <ToolCallBlock key={message.id} message={message} />
    default:
      return null
  }
}

export function ConversationTimeline({
  messages,
  currentStageId,
  isStreaming = false,
}: ConversationTimelineProps) {
  const stageGroups = useMemo(() => groupByStage(messages), [messages])
  const { containerRef, handleScroll } = useAutoScroll(isStreaming)

  if (messages.length === 0) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Stack align="center" gap="sm">
          <Text size="lg" fw={500} c="dimmed">
            No conversation yet
          </Text>
          <Text size="sm" c="dimmed">
            Messages will appear here as the agent executes.
          </Text>
        </Stack>
      </Box>
    )
  }

  return (
    <Box
      ref={containerRef}
      onScroll={handleScroll}
      style={{
        flex: 1,
        overflow: 'auto',
        padding: 'var(--mantine-spacing-sm)',
      }}
    >
      {stageGroups.map((stage) => (
        <StageMarker
          key={stage.stageId}
          stage={stage}
          defaultExpanded={stage.stageId === currentStageId || stageGroups.length === 1}
        >
          {stage.messages.map(renderMessage)}
        </StageMarker>
      ))}
    </Box>
  )
}
