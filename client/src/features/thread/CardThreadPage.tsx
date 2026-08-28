import { useParams } from 'react-router'
import { Alert, Box } from '@mantine/core'
import { useBoardColumns } from '../../api/boards'
import { useCardThread } from '../../api/cardThread'
import { CardThreadPanel } from './CardThreadPanel'

/**
 * The full-screen thread at `/thread/:cardId` (mobile-thread spec §4, M4) — where taps from the
 * home bands land. The param takes any identifier form the card routes take (`CARD-0067`,
 * `card-67`, `67`, the guid); `#67` cannot appear in a path segment un-encoded, so links here use
 * the `card-67` form.
 *
 * <p>The panel owns the thread fetch; this page only resolves the board's columns (for Approve —
 * a move needs move targets) once the thread has said which board the card is on.</p>
 */
export function CardThreadPage() {
  const { cardId } = useParams<{ cardId: string }>()
  const thread = useCardThread(cardId ?? null)
  const columns = useBoardColumns(thread.data?.card.boardId)

  if (!cardId) {
    return (
      <Alert color="yellow" title="No card">
        This thread link names no card.
      </Alert>
    )
  }

  return (
    <Box maw={640} mx="auto">
      <CardThreadPanel
        identifier={cardId}
        boardId={thread.data?.card.boardId}
        columns={columns.data ?? []}
        showCardHeader
      />
    </Box>
  )
}
