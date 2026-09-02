import { Box, Loader, Modal, Text } from '@mantine/core'
import { useBoardColumns, useCard } from '../../../api/boards'
import type { HomeTaskItemDto } from '../../../api/homeTasks'
import { CardModal } from '../../board/CardModal'
import { DelegationTaskModal } from '../../delegations/DelegationTaskModal'

/**
 * Routes a rail item to the existing card or delegation modal. `CardModal` with `card: null` is
 * the create form, so the card branch waits for `useCard` before mounting it.
 */
export function HomeTaskModal({
  item,
  onClose,
}: {
  item: HomeTaskItemDto | null
  onClose: () => void
}) {
  if (!item) return null
  if (item.source === 'Delegation') {
    return <DelegationTaskModal taskId={item.id} onClose={onClose} />
  }
  return <CardTaskModal item={item} onClose={onClose} />
}

function CardTaskModal({ item, onClose }: { item: HomeTaskItemDto; onClose: () => void }) {
  const fullCard = useCard(item.id)
  const boardId = item.boardId ?? fullCard.data?.boardId ?? undefined
  const columns = useBoardColumns(boardId)

  if (fullCard.isError) {
    return (
      <Modal opened onClose={onClose} title={item.identifier}>
        <Text size="sm" c="dimmed">
          Card is unavailable.
        </Text>
      </Modal>
    )
  }

  if (!fullCard.data) {
    return (
      <Modal opened onClose={onClose} withCloseButton={false} centered>
        <Box aria-label="Loading card" ta="center" p="md">
          <Loader />
        </Box>
      </Modal>
    )
  }

  return (
    <CardModal
      boardId={boardId ?? fullCard.data.boardId}
      card={fullCard.data}
      columns={columns.data ?? []}
      opened
      onClose={onClose}
    />
  )
}
