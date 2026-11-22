import { Group, Text } from '@mantine/core'
import type { ReactNode } from 'react'

export function SectionLabel({ children, right }: { children: ReactNode; right?: ReactNode }) {
  return (
    <Group justify="space-between" mb={4} wrap="nowrap">
      <Text size="xs" fw={700} c="dimmed" tt="uppercase" style={{ letterSpacing: 0.8 }}>
        {children}
      </Text>
      {right}
    </Group>
  )
}
