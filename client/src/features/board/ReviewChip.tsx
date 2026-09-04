import { Badge } from '@mantine/core'

/** CARD-0327: derived needsHumanReview marker, shown next to the tracker key. */
export function ReviewChip() {
  return (
    <Badge size="xs" color="warning" variant="light" style={{ flex: 'none' }}>
      review
    </Badge>
  )
}
