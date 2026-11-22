import { Stack, Text, Tooltip } from '@mantine/core'
import type { GatePassDto } from '../../../api/reviewEngine'
import { fmtAgo } from '../reviewFormat'

/** "Not me" passes on a gate, oldest first: who, why and when. Passers are excluded from the next pick. */
export function GatePasses({ passes, now, note = true }: { passes: GatePassDto[]; now: number; note?: boolean }) {
  if (passes.length === 0) return null
  return (
    <Stack gap={0}>
      {passes.map((p) => (
        <Text key={`${p.login}:${p.at}`} size="xs" c="warning.4" truncate>
          Passed by{' '}
          <Text span size="xs" fw={600}>
            @{p.login}
          </Text>{' '}
          · {p.reason} ·{' '}
          <Tooltip label={new Date(p.at).toLocaleString()}>
            <Text span size="xs" c="dimmed">
              {fmtAgo(p.at, now)}
            </Text>
          </Tooltip>
        </Text>
      ))}
      {note && (
        <Text size="10px" c="dimmed">
          Passers are excluded from the next random pick.
        </Text>
      )}
    </Stack>
  )
}
