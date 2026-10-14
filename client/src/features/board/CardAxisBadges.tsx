import { Badge } from '@mantine/core'
import type { CardDto } from '../../api/boards'
import { importanceBadgeColor, urgencyBadgeColor, urgencyBadgeText } from './boardVisuals'

export function CardAxisBadges({
  card,
  now,
}: {
  card: Pick<CardDto, 'importance' | 'urgency' | 'effectiveUrgency' | 'dueAt' | 'openBlockerCount' | 'blockers'>
  now?: Date
}) {
  const urgency = urgencyBadgeText(card, now ?? new Date())
  return (
    <>
      {(card.openBlockerCount ?? 0) > 0 && (
        <Badge
          size="xs"
          color="red"
          variant="outline"
          title={`Blocked by ${(card.blockers ?? []).filter((blocker) => !blocker.resolved).map((blocker) => blocker.identifier).join(', ')}`}
          style={{ flex: 'none' }}
        >
          Blocked
        </Badge>
      )}
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
