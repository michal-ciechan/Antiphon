import { Box, Text, Group, Badge } from '@mantine/core'
import { VscComment } from 'react-icons/vsc'
import type { TimelineMessage } from '../types'

interface UserCommentMessageProps {
  message: TimelineMessage
}

export function UserCommentMessage({ message }: UserCommentMessageProps) {
  return (
    <Box
      style={{
        padding: 'var(--mantine-spacing-sm)',
        borderLeft: '3px solid var(--mantine-color-dark-3)',
        marginBottom: 'var(--mantine-spacing-xs)',
        borderRadius: 'var(--mantine-radius-sm)',
        backgroundColor: 'var(--mantine-color-dark-8)',
        borderStyle: 'dashed',
        borderWidth: 0,
        borderLeftWidth: 3,
        borderLeftStyle: 'dashed',
        borderLeftColor: 'var(--mantine-color-dark-3)',
      }}
    >
      <Group gap="xs" mb={4}>
        <VscComment size={16} color="var(--mantine-color-dark-2)" />
        <Text size="xs" fw={600} c="dimmed">
          {message.author ?? 'User'}
        </Text>
        <Badge size="xs" variant="light" color="gray">
          Comment
        </Badge>
        <Text size="xs" c="dimmed" ml="auto">
          {new Date(message.timestamp).toLocaleTimeString()}
        </Text>
      </Group>
      <Text size="sm" c="dimmed" style={{ whiteSpace: 'pre-wrap', fontStyle: 'italic' }}>
        {message.content}
      </Text>
    </Box>
  )
}
