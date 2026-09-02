import { Alert, Badge, Box, Button, Group, Loader, Paper, Stack, Text } from '@mantine/core'
import { useState } from 'react'
import { TbAlertCircle } from 'react-icons/tb'
import type { BoardColumnDto, CardRevisionDto } from '../../api/boards'
import { useCardRevisions } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'

interface CardHistoryProps {
  cardId: string
  /** The board's states, for naming a move's endpoints. Empty on the all-boards view. */
  columns?: BoardColumnDto[]
}

/**
 * A card's history as one INTERLEAVED timeline, newest first — not a list of text diffs.
 *
 * The server numbers every kind off one monotonic `revisionNumber`, and on most cards the
 * majority of entries are moves: a diff list would show an empty history for a card that has
 * crossed the whole board. So each kind gets its own row shape and they interleave in the order
 * they happened.
 *
 * There is deliberately no computed diff in this slice. A `ContentEdit` carries the values it
 * SUPERSEDED, and that snapshot plus its reason is the record; diffing entry n against entry n-1
 * is a later nicety that would obscure the thing being pinned here.
 */
export function CardHistory({ cardId, columns = [] }: CardHistoryProps) {
  const { data, isLoading, error } = useCardRevisions(cardId)

  if (isLoading) {
    return (
      <Group justify="center" p="xl">
        <Loader size="sm" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
        {getApiErrorMessage(error, 'History failed to load')}
      </Alert>
    )
  }

  if (!data || data.length === 0) {
    return (
      <Text c="dimmed" size="sm" p="sm" data-testid="card-history-empty">
        No history yet. A card gains an entry the first time it is moved, edited or archived.
      </Text>
    )
  }

  return (
    <Stack gap="xs" p="sm" data-testid="card-history">
      {data.map((revision) => (
        <RevisionRow key={revision.id} revision={revision} columns={columns} />
      ))}
    </Stack>
  )
}

const KIND_LABEL: Record<CardRevisionDto['kind'], string> = {
  ContentEdit: 'Edited',
  Move: 'Moved',
  Archive: 'Archived',
  Unarchive: 'Unarchived',
  Reopen: 'Reopened',
}

const KIND_COLOR: Record<CardRevisionDto['kind'], string> = {
  ContentEdit: 'active',
  Move: 'gray',
  Archive: 'red',
  Unarchive: 'green',
  Reopen: 'orange',
}

function RevisionRow({ revision, columns }: { revision: CardRevisionDto; columns: BoardColumnDto[] }) {
  return (
    <Paper withBorder p="xs" data-testid={`revision-${revision.revisionNumber}`}>
      <Group gap={6} wrap="wrap" align="baseline">
        <Badge size="xs" variant="light" color={KIND_COLOR[revision.kind]}>
          {KIND_LABEL[revision.kind]}
        </Badge>
        {(revision.kind === 'Move' || revision.kind === 'Reopen') && (
          <Text size="sm" fw={600}>
            {describeMove(revision, columns)}
          </Text>
        )}
        <Text size="xs" c="dimmed" ml="auto" title={revision.createdAt}>
          {formatStamp(revision.createdAt)}
        </Text>
      </Group>

      <Stack gap={4} mt={4}>
        {revision.reason && <Text size="sm">{revision.reason}</Text>}
        {revision.editedBy && (
          // Self-reported by whoever made the change — the server has no principals, so this is
          // never presented as an authenticated actor.
          <Text size="xs" c="dimmed">by {revision.editedBy} (self-reported)</Text>
        )}
        {revision.kind === 'ContentEdit' && <SupersededContent revision={revision} />}
        {revision.kind === 'Reopen' && <SupersededClose revision={revision} />}
      </Stack>
    </Paper>
  )
}

/**
 * Column NAMES where they can be resolved, statuses otherwise. Both fallbacks are real: the
 * all-boards modal passes `columns=[]`, and a column can be deleted out from under old revisions.
 */
function describeMove(revision: CardRevisionDto, columns: BoardColumnDto[]): string {
  const name = (columnId: string | null, status: string | null) =>
    columns.find((column) => column.id === columnId)?.name ?? status ?? '?'
  return `${name(revision.fromColumnId, revision.fromStatus)} → ${name(revision.toColumnId, revision.toStatus)}`
}

/** The close this reopen undid — timestamp and the reason that was on the card. */
function SupersededClose({ revision }: { revision: CardRevisionDto }) {
  const when = revision.completedAt ? formatStamp(revision.completedAt) : '?'
  const why = revision.terminalReason ?? '?'
  return (
    <Text size="xs" c="dimmed" data-testid={`superseded-close-${revision.revisionNumber}`}>
      was closed {when}: {why}
    </Text>
  )
}

/**
 * The superseded text, COLLAPSED. A superseded description can be 20,000 characters and there can
 * be one per edit — rendering them open would bury the timeline in the text it exists to replace.
 */
function SupersededContent({ revision }: { revision: CardRevisionDto }) {
  const [shown, setShown] = useState(false)
  const hasText = !!revision.title || !!revision.description
  const hasMeta = revision.importance !== null || revision.urgency !== null
    || revision.dueAt !== null || (revision.labels?.length ?? 0) > 0

  if (!hasText && !hasMeta) return null

  return (
    <Box>
      {hasMeta && (
        <Group gap={6} wrap="wrap" mb={4}>
          <Text size="xs" c="dimmed">was</Text>
          {revision.importance !== null && (
            <Badge size="xs" color="gray" variant="outline">{revision.importance}</Badge>
          )}
          {revision.urgency !== null && revision.urgency !== 'Normal' && (
            <Badge size="xs" color="gray" variant="outline">{revision.urgency}</Badge>
          )}
          {revision.dueAt !== null && (
            <Badge size="xs" color="gray" variant="outline">due {revision.dueAt.slice(0, 10)}</Badge>
          )}
          {(revision.labels ?? []).map((label) => (
            <Badge key={label} size="xs" color="gray" variant="outline">{label}</Badge>
          ))}
        </Group>
      )}
      {hasText && (
        <>
          {/*
            A hand-rolled toggle rather than Mantine's Spoiler: Spoiler decides whether to collapse
            by MEASURING the content, and 20,000 characters that measure zero (jsdom, a hidden tab,
            a font that has not loaded) would render open.
          */}
          <Button
            size="compact-xs"
            variant="subtle"
            onClick={() => setShown((current) => !current)}
          >
            {shown ? 'Hide superseded text' : 'Show superseded text'}
          </Button>
          {shown && (
            <Stack gap={4} pt={4}>
              {revision.title && (
                <Text size="sm" fs="italic" data-testid={`superseded-title-${revision.revisionNumber}`}>
                  {revision.title}
                </Text>
              )}
              {revision.description && (
                <Text
                  size="sm"
                  c="dimmed"
                  style={{ whiteSpace: 'pre-wrap' }}
                  data-testid={`superseded-description-${revision.revisionNumber}`}
                >
                  {revision.description}
                </Text>
              )}
            </Stack>
          )}
        </>
      )}
    </Box>
  )
}

/** UTC, sliced straight off the ISO string — a history reads better when every row agrees. */
function formatStamp(iso: string): string {
  return `${iso.slice(0, 10)} ${iso.slice(11, 16)}Z`
}
