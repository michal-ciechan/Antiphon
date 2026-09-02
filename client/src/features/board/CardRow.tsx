import { Badge, Box, Group, Text, Tooltip } from '@mantine/core'
import { TbTerminal2 } from 'react-icons/tb'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import { displayIdentifier, externalIssueTag } from '../../shared/cardIdentifier'
import { ageInDays, hasLiveSession } from './boardShapeModel'
import { CardAxisBadges } from './CardAxisBadges'
import { MoveMenu } from './MoveMenu'

interface CardRowProps {
  card: CardDto
  boardId: string
  columns: BoardColumnDto[]
  now: Date
  onOpen: (cardId: string) => void
  /** `stacked` drops the title onto its own line — the phone layout. */
  layout?: 'row' | 'stacked'
}

/**
 * A card as one line of the record. The description is gone from the tile deliberately: two
 * clamped lines of it were noise at this density, and it is one click away in the card modal.
 *
 * The age shown is CARD AGE since creation, not time in this state — nothing records when a card
 * entered its state (feature 011 §3).
 */
export function CardRow({ card, boardId, columns, now, onOpen, layout = 'row' }: CardRowProps) {
  const live = hasLiveSession(card)
  const age = ageInDays(card.createdAt, now)
  const stacked = layout === 'stacked'
  // Only ever on screen under the board's Archived filter, so it needs to read as "not part of
  // the live board" without becoming unreadable — the row is still the record.
  const archived = !!card.archivedAt

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

  // Priority, labels, stage and the live-session marker share one lane that TRUNCATES rather than
  // wrapping — a long label must not push the age and the kebab onto a line of their own.
  const meta = (
    <Group gap={6} wrap="nowrap" style={{ flex: 1, minWidth: 0, overflow: 'hidden' }}>
      {archived && (
        <Badge
          size="xs"
          color="gray"
          variant="filled"
          style={{ flex: 'none' }}
          title={card.archivedReason ?? undefined}
        >
          archived
        </Badge>
      )}
      <CardAxisBadges card={card} now={now} />
      {card.labels.slice(0, stacked ? 1 : 2).map((label) => (
        <Badge key={label} size="xs" color="gray" variant="outline" style={{ flex: 'none' }}>
          {label}
        </Badge>
      ))}
      {card.currentWorkflowStageName && (
        <Badge size="xs" color="active" variant="light" style={{ flex: 'none' }}>
          {card.currentWorkflowStageName}
        </Badge>
      )}
      {live && (
        <Group gap={4} wrap="nowrap" style={{ flex: 'none' }}>
          <TbTerminal2 size={14} color="var(--mantine-color-success-5)" />
          <Text size="xs" c="success">{card.assignedAgentName ?? 'running'}</Text>
        </Group>
      )}
    </Group>
  )

  const trailing = (
    <>
      <Text
        size="xs"
        c="dimmed"
        style={{ flex: 'none', fontVariantNumeric: 'tabular-nums' }}
        title={`created ${card.createdAt.slice(0, 10)} — card age, not time in this state`}
      >
        {age}d
      </Text>
      <Box style={{ flex: 'none' }} onClick={(event) => event.stopPropagation()}>
        <MoveMenu boardId={boardId} card={card} columns={columns} />
      </Box>
    </>
  )

  return (
    <Box
      role="article"
      aria-label={`${card.identifier} ${card.title}`}
      data-testid={`card-row-${card.identifier}`}
      tabIndex={0}
      onClick={() => onOpen(card.id)}
      onKeyDown={(event) => {
        // React events bubble through the COMPONENT tree, so a keypress inside the move menu's
        // portalled modal arrives here too — typing a space in its reason field would otherwise
        // swallow the space and open the card.
        if (event.target !== event.currentTarget) return
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onOpen(card.id)
        }
      }}
      px="xs"
      py={stacked ? 8 : 5}
      style={{
        cursor: 'pointer',
        borderRadius: 6,
        borderTop: stacked ? '1px solid var(--mantine-color-dark-5)' : undefined,
        borderLeft: live ? '3px solid var(--mantine-color-success-5)' : '3px solid transparent',
        opacity: archived ? 0.55 : undefined,
      }}
    >
      <Group gap="xs" wrap="nowrap" align="baseline">
        {identifier}
        {stacked ? meta : <Box style={{ flex: 1, minWidth: 0 }}>{title}</Box>}
        {!stacked && <Box style={{ flex: 'none', maxWidth: '45%' }}>{meta}</Box>}
        {trailing}
      </Group>
      {stacked && <Box pl={46}>{title}</Box>}
    </Box>
  )
}
