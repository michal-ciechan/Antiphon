import { Box, Text, Group, Badge } from '@mantine/core'
import { VscRobot } from 'react-icons/vsc'
import type { TimelineMessage } from '../types'

interface AgentMessageProps {
  message: TimelineMessage
}

export function AgentMessage({ message }: AgentMessageProps) {
  return (
    <Box
      style={{
        padding: 'var(--mantine-spacing-sm)',
        borderLeft: '3px solid var(--mantine-color-active-5)',
        marginBottom: 'var(--mantine-spacing-xs)',
        borderRadius: 'var(--mantine-radius-sm)',
        backgroundColor: 'var(--mantine-color-dark-7)',
      }}
    >
      <Group gap="xs" mb={4}>
        <VscRobot size={16} color="var(--mantine-color-active-5)" />
        <Text size="xs" fw={600} c="active">
          Agent
        </Text>
        {message.model && (
          <Badge size="xs" variant="outline" color="active">
            {message.model}
          </Badge>
        )}
        <Text size="xs" c="dimmed" ml="auto">
          {new Date(message.timestamp).toLocaleTimeString()}
        </Text>
      </Group>
      <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
        {message.content}
      </Text>
    </Box>
  )
}
