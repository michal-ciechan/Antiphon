import { Group, Box, Tooltip, Text } from '@mantine/core'

interface PipelineIndicatorProps {
  completed: number
  total: number
}

export function PipelineIndicator({ completed, total }: PipelineIndicatorProps) {
  if (total === 0) return null

  return (
    <Tooltip label={`${completed} / ${total} stages completed`}>
      <Group gap={3} wrap="nowrap" align="center">
        {Array.from({ length: total }, (_, i) => (
          <Box
            key={i}
            style={{
              width: 16,
              height: 8,
              borderRadius: 2,
              backgroundColor: i < completed
                ? 'var(--mantine-color-green-6)'
                : 'var(--mantine-color-dark-4)',
              transition: 'background-color 200ms ease',
            }}
          />
        ))}
        <Text size="xs" c="dimmed" ml={4}>
          {completed}/{total}
        </Text>
      </Group>
    </Tooltip>
  )
}
