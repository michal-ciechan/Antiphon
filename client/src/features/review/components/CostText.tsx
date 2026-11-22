import { Stack, Text, Tooltip } from '@mantine/core'
import { fmtCost, fmtCostExact } from '../reviewFormat'

type Size = 'xs' | 'sm' | 'md' | 'lg' | 'xl'

/**
 * Dollars first (D-20). "≈" marks a total that includes an API-equivalent estimate for seats; the
 * tooltip gives the exact amount and, when known, the split between actual and estimate.
 */
export function CostText({
  usd,
  split,
  size = 'xs',
  dimmed = false,
}: {
  usd: number
  split?: { actual: number; estimate: number }
  size?: Size
  dimmed?: boolean
}) {
  const estimated = !!split && split.estimate > 0
  return (
    <Tooltip
      withArrow
      multiline
      w={300}
      label={
        <Stack gap={2}>
          <Text size="xs">
            <b>{fmtCostExact(usd)}</b> total
          </Text>
          {split ? (
            <>
              <Text size="xs">
                {fmtCostExact(split.actual)} actual · metered API (HTTP)
              </Text>
              <Text size="xs">≈{fmtCostExact(split.estimate)} API-equivalent estimate · subscription seats, not billed</Text>
            </>
          ) : (
            <Text size="xs" c="dimmed">
              Actual API spend plus the API-equivalent estimate for seat models.
            </Text>
          )}
        </Stack>
      }
    >
      <Text
        size={size}
        ff="monospace"
        span
        c={dimmed ? 'dimmed' : undefined}
        style={{ whiteSpace: 'nowrap', textDecoration: estimated ? 'underline dotted' : undefined, textUnderlineOffset: 3, cursor: 'help' }}
      >
        {usd === 0 ? '—' : `${estimated ? '≈' : ''}${fmtCost(usd)}`}
      </Text>
    </Tooltip>
  )
}
