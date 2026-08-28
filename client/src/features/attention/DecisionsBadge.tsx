import { Badge, Anchor } from '@mantine/core'
import { TbHelpCircle } from 'react-icons/tb'
import { Link } from 'react-router'
import { useAttentionSummary } from '../../api/attention'

/** A separate, decision-only signal: the general attention count includes diagnostic rows. */
export function DecisionsBadge() {
  const attention = useAttentionSummary()
  const count = attention.data?.decisions ?? 0

  if (count === 0) return null

  return (
    <Anchor component={Link} to="/orchestrator?tab=decisions" underline="never">
      <Badge size="sm" variant="light" color="danger" leftSection={<TbHelpCircle size={14} />}>
        Decisions ({count})
      </Badge>
    </Anchor>
  )
}
