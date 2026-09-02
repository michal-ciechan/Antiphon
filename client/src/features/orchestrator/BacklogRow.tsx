import { Badge, Box, Group, Text, Tooltip } from '@mantine/core'
import { useNavigate } from 'react-router'
import type { CardDto } from '../../api/boards'
import { displayIdentifier, externalIssueTag } from '../../shared/cardIdentifier'
import { ageInDays } from '../board/boardShapeModel'
import { CardAxisBadges } from '../board/CardAxisBadges'

interface BacklogRowProps {
  card: CardDto
  boardName: string | undefined
  showBoard: boolean
  now: Date
  /** `stacked` drops the title onto its own line — the phone layout. */
  layout?: 'row' | 'stacked'
}

/**
 * One Backlog card as a picking-list line. Labels, stage and live markers stay off: live
 * Backlog rows carry none of those, and the card modal on the board still has them.
 */
export function BacklogRow({
  card,
  boardName,
  showBoard,
  now,
  layout = 'row',
}: BacklogRowProps) {
  const navigate = useNavigate()
  const stacked = layout === 'stacked'
  const age = ageInDays(card.createdAt, now)
  const open = () => navigate(`/boards/${card.boardId}?card=${card.id}`)

  const identifier = (
    <Group gap={4} wrap="nowrap" style={{ flex: 'none' }}>
      <Tooltip label={card.identifier} withArrow openDelay={400}>
        <Text
          size="sm"
          fw={700}
          c="dimmed"
          style={{ width: 42, flex: 'none', fontVariantNumeric: 'tabular-nums' }}
        >
          {displayIdentifier(card.identifier)}
        </Text>
      </Tooltip>
      {card.externalIssue && (
        <Text size="xs" c="dimmed" style={{ flex: 'none' }}>
          {externalIssueTag(card.externalIssue)}
        </Text>
      )}
    </Group>
  )

  const title = (
    <Text size="sm" lineClamp={stacked ? 2 : 1} style={{ minWidth: 0 }}>
      {card.title}
    </Text>
  )

  const meta = (
    <Group gap={6} wrap="nowrap" style={{ flex: 1, minWidth: 0, overflow: 'hidden' }}>
      <CardAxisBadges card={card} now={now} />
      {showBoard && boardName && (
        <Badge size="xs" color="gray" variant="outline" style={{ flex: 'none' }}>
          {boardName}
        </Badge>
      )}
    </Group>
  )

  return (
    <Box
      role="article"
      aria-label={`${card.identifier} ${card.title}`}
      data-testid={`backlog-row-${card.identifier}`}
      tabIndex={0}
      onClick={open}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          open()
        }
      }}
      px="xs"
      py={stacked ? 8 : 5}
      style={{ cursor: 'pointer', borderRadius: 6 }}
    >
      <Group gap="xs" wrap="nowrap" align="baseline">
        {identifier}
        {stacked ? meta : <Box style={{ flex: 1, minWidth: 0 }}>{title}</Box>}
        {!stacked && <Box style={{ flex: 'none', maxWidth: '45%' }}>{meta}</Box>}
        <Text
          size="xs"
          c="dimmed"
          style={{ flex: 'none', fontVariantNumeric: 'tabular-nums' }}
          title={`created ${card.createdAt.slice(0, 10)} — card age, not time in this state`}
        >
          {age}d
        </Text>
      </Group>
      {stacked && <Box pl={46}>{title}</Box>}
    </Box>
  )
}
