import { Group, Loader, Text } from '@mantine/core'

/** A read with no data and no error, shown with its `pendingReadLabel` text (CARD-1270, CARD-1274). */
export function PendingRead({ label, py = 'sm' }: { label: string; py?: number | string }) {
  return (
    <Group justify="center" gap="xs" py={py}>
      <Loader size="sm" />
      <Text size="sm" c="dimmed">
        {label}
      </Text>
    </Group>
  )
}
