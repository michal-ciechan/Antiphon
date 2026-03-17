import { Box, Text, Group } from '@mantine/core'
import { VscInfo } from 'react-icons/vsc'
import type { TimelineMessage } from '../types'

interface SystemEventProps {
  message: TimelineMessage
}

export function SystemEvent({ message }: SystemEventProps) {
  return (
    <Box
      style={{
        padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
        marginBottom: 'var(--mantine-spacing-xs)',
        textAlign: 'center',
      }}
    >
      <Group gap="xs" justify="center">
        <VscInfo size={14} color="var(--mantine-color-dimmed)" />
        <Text size="xs" c="dimmed" fs="italic">
          {message.content}
        </Text>
        <Text size="xs" c="dimmed">
          {new Date(message.timestamp).toLocaleTimeString()}
        </Text>
      </Group>
    </Box>
  )
}
