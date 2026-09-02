import { Stack, Text } from '@mantine/core'
import { spendModeInWords, useCardSchedules } from '../../api/schedules'

export function CardSchedulesList({ cardId }: { cardId: string }) {
  const list = useCardSchedules(cardId)
  const rows = list.data?.schedules ?? []
  if (list.isLoading || rows.length === 0) return null

  return (
    <Stack gap={4} data-testid="card-schedules-list">
      <Text size="xs" c="dimmed" fw={700} tt="uppercase">
        Schedules
      </Text>
      {rows.map((row) => (
        <Stack key={row.id} gap={0}>
          <Text size="sm" fw={600}>
            {row.name}
          </Text>
          <Text size="xs" c="dimmed" data-testid={`card-schedule-spend-${row.id}`}>
            {spendModeInWords(row)}
          </Text>
        </Stack>
      ))}
    </Stack>
  )
}
