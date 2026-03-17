import { Box, Loader, Text, Stack } from '@mantine/core'
import { ConversationTimeline } from './ConversationTimeline'
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
  // When streaming with no messages yet, show loading state
  if (isStreaming && messages.length === 0) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: 0,
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
    )
  }

  return (
    <ConversationTimeline
      messages={messages}
      currentStageId={currentStageId}
      isStreaming={isStreaming}
    />
  )
}
