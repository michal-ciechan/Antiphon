import { Badge } from '@mantine/core'
import type { CardDto } from '../../api/boards'
import { importanceBadgeColor, urgencyBadgeColor, urgencyBadgeText } from './boardVisuals'

export function CardAxisBadges({
  card,
  now,
}: {
  card: Pick<CardDto, 'importance' | 'urgency' | 'effectiveUrgency' | 'dueAt'>
  now?: Date
}) {
  const urgency = urgencyBadgeText(card, now ?? new Date())
  return (
    <>
      {card.importance !== 'Normal' && (
        <Badge size="xs" color={importanceBadgeColor(card.importance)} variant="light" style={{ flex: 'none' }}>
          {card.importance}
        </Badge>
      )}
      {urgency && (
        <Badge size="xs" color={urgencyBadgeColor(card.effectiveUrgency)} variant="light" style={{ flex: 'none' }}>
          {urgency}
        </Badge>
      )}
    </>
  )
}
