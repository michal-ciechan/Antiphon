import { Box, Text, Group } from '@mantine/core'
import { VscAccount } from 'react-icons/vsc'
import type { TimelineMessage } from '../types'

interface UserPromptMessageProps {
  message: TimelineMessage
}

export function UserPromptMessage({ message }: UserPromptMessageProps) {
  return (
    <Box
      style={{
        padding: 'var(--mantine-spacing-sm)',
        borderLeft: '3px solid var(--mantine-color-active-4)',
        marginBottom: 'var(--mantine-spacing-xs)',
        borderRadius: 'var(--mantine-radius-sm)',
        backgroundColor: 'var(--mantine-color-dark-6)',
      }}
    >
      <Group gap="xs" mb={4}>
        <VscAccount size={16} color="var(--mantine-color-active-4)" />
        <Text size="xs" fw={600}>
          {message.author ?? 'User'}
        </Text>
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
