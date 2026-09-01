import { Anchor, Badge, Group } from '@mantine/core'
import { Link } from 'react-router'
import { useAttention } from '../../api/attention'
import { homeBucketCounts } from './attentionVisuals'

const GLANCE_BADGES = [
  { key: 'blocked', label: 'Blocked', color: 'danger' },
  { key: 'broken', label: 'Broken', color: 'danger' },
  { key: 'review', label: 'Review', color: 'warning' },
] as const

/**
 * Three counts on Home, one tap to `/attention`. Quiet (all three zero, or still pending with
 * nothing loaded) renders nothing: a permanent "Blocked 0" chip is a control nobody sees after a
 * week, and omitting a zero bucket keeps a single blocker as one chip rather than three.
 */
export function AttentionGlance() {
  const attention = useAttention()
  const counts = homeBucketCounts(attention.data?.items ?? [])
  if (counts.blocked === 0 && counts.broken === 0 && counts.review === 0) return null

  return (
    <Anchor component={Link} to="/attention" underline="never" data-testid="attention-glance">
      <Group gap="xs" wrap="wrap">
        {GLANCE_BADGES.map(
          ({ key, label, color }) =>
            counts[key] > 0 && (
              <Badge key={key} size="sm" variant="light" color={color} style={{ textTransform: 'none' }}>
                {label} {counts[key]}
              </Badge>
            ),
        )}
      </Group>
    </Anchor>
  )
}
