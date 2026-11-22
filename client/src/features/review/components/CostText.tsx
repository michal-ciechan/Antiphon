import { Group, Stack, Text, Tooltip } from '@mantine/core'
import { fmtCost, fmtCostExact, fmtCostTotal, type CostSplit } from '../reviewFormat'

type Size = 'xs' | 'sm' | 'md' | 'lg' | 'xl'

function SplitRow({ label, usd, approx }: { label: string; usd: number; approx?: boolean }) {
  return (
    <Group gap={8} justify="space-between" wrap="nowrap">
      <Text size="xs">{label}</Text>
      <Text size="xs" ff="monospace" style={{ whiteSpace: 'nowrap' }}>
        {approx && usd > 0 ? '≈' : ''}
        {fmtCost(usd)} · {fmtCostExact(usd)}
      </Text>
    </Group>
  )
}

/**
 * Dollars first (D-20): under $1 rounded cents, otherwise rounded dollars. "≈" marks a total that
 * includes an API-equivalent estimate for seats; the tooltip splits actual from estimated.
 */
export function CostText({ usd, split, size = 'xs', dimmed = false }: { usd: number; split?: CostSplit; size?: Size; dimmed?: boolean }) {
  const estimated = !!split && split.estimate > 0
  return (
    <Tooltip
      withArrow
      multiline
      w={320}
      label={
        <Stack gap={2}>
          {split ? (
            <>
              <SplitRow label="Actual: metered API calls" usd={split.actual} />
              <SplitRow label="Estimated: seat API-equivalent, not billed" usd={split.estimate} approx />
              <SplitRow label="Total" usd={usd} approx={estimated} />
            </>
          ) : (
            <>
              <SplitRow label="Total" usd={usd} />
              <Text size="xs" c="dimmed">
                Actual API spend plus the API-equivalent estimate for seat models.
              </Text>
            </>
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
        {usd === 0 ? '—' : fmtCostTotal(usd, split?.estimate ?? 0)}
      </Text>
    </Tooltip>
  )
}
