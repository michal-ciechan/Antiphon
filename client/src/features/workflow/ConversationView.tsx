import { useMemo } from 'react'
import { Box, Loader, Text, Stack } from '@mantine/core'
import { ConversationTimeline } from './ConversationTimeline'
import { AgentActivityStatus } from './AgentActivityStatus'
import { useStreamingStore } from '../../stores/streamingStore'
import type { TimelineMessage } from './types'

interface ConversationViewProps {
  messages: TimelineMessage[]
  currentStageId?: string
  isStreaming?: boolean
}

export function ConversationView({
  messages,
  currentStageId,
  isStreaming = false,
}: ConversationViewProps) {
  // Merge passed-in messages with streaming store messages
  const streamingMessages = useStreamingStore((s) => s.messages)
  const storeIsStreaming = useStreamingStore((s) => s.isStreaming)
  const textBuffer = useStreamingStore((s) => s.textBuffer)
  const activeStageId = useStreamingStore((s) => s.activeStageId)
  const activeStageName = useStreamingStore((s) => s.activeStageName)

  const effectiveIsStreaming = isStreaming || storeIsStreaming

  // Build the combined message list: props messages + streaming messages + live text buffer
  const allMessages = useMemo(() => {
    const combined = [...messages, ...streamingMessages]

    // If there is text being streamed, add it as a live agent message
    if (textBuffer.length > 0 && activeStageId && activeStageName) {
      combined.push({
        id: 'streaming-live',
        type: 'agent',
        content: textBuffer,
        timestamp: new Date().toISOString(),
        stageId: activeStageId,
        stageName: activeStageName,
      })
    }

    return combined
  }, [messages, streamingMessages, textBuffer, activeStageId, activeStageName])

  // When streaming with no messages yet, show loading state
  if (effectiveIsStreaming && allMessages.length === 0) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          minHeight: 0,
        }}
      >
        <Box
          style={{
            flex: 1,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <Stack align="center" gap="md">
            <Loader size="lg" color="active" />
            <Text size="lg" fw={500}>
              Agent executing...
            </Text>
            <Text size="sm" c="dimmed">
              Streaming output will appear here.
            </Text>
          </Stack>
        </Box>
        <AgentActivityStatus />
      </Box>
    )
  }

  return (
    <Box style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <ConversationTimeline
        messages={allMessages}
        currentStageId={currentStageId}
        isStreaming={effectiveIsStreaming}
      />
      {effectiveIsStreaming && <AgentActivityStatus />}
    </Box>
  )
}
