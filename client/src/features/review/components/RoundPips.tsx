import { Box, Group, Text, Tooltip } from '@mantine/core'
import { BOT_ROUND_LIMIT } from '../reviewMeta'

/** R1 R2 [R3] … plus the bot-only counter toward the human gate. */
export function RoundPips({ round, botOnly, compact = false }: { round: number; botOnly: number; compact?: boolean }) {
  const limit = BOT_ROUND_LIMIT
  return (
    <Tooltip
      withArrow
      label={`Round ${round}. Bot-only rounds since a human was last involved: ${botOnly}/${limit}${botOnly >= limit ? ' → the human-review gate opens' : ''}`}
    >
      <Group gap={3} wrap="nowrap" aria-label={`Round ${round}`}>
        {Array.from({ length: Math.max(round, compact ? 0 : 4) }, (_, i) => i + 1).map((r) => (
          <Box
            key={r}
            w={compact ? 7 : 16}
            h={compact ? 7 : 14}
            style={{
              borderRadius: 3,
              border: '1px solid var(--mantine-color-dark-3)',
              background: r < round ? 'var(--mantine-color-dark-3)' : r === round ? 'var(--mantine-color-active-6)' : 'transparent',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {!compact && (
              <Text size="9px" fw={700} c={r <= round ? 'white' : 'dimmed'}>
                {r}
              </Text>
            )}
          </Box>
        ))}
        {!compact && (
          <Text size="xs" c={botOnly >= limit ? 'pink.4' : 'dimmed'} ml={4} style={{ whiteSpace: 'nowrap' }}>
            bot-only {botOnly}/{limit}
          </Text>
        )}
      </Group>
    </Tooltip>
  )
}
