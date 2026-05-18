import { Alert, Button, Group, Loader, Modal, Select, Stack } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState } from 'react'
import { TbAlertCircle } from 'react-icons/tb'
import { useAssignAgentCard } from '../../api/agents'
import { useBoard, useBoards } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'

interface AgentQueueAssignModalProps {
  agentId: string
  opened: boolean
  onClose: () => void
}

export function AgentQueueAssignModal({ agentId, opened, onClose }: AgentQueueAssignModalProps) {
  const boards = useBoards()
  const [selectedBoardId, setSelectedBoardId] = useState<string | null>(null)
  const [selectedCardId, setSelectedCardId] = useState<string | null>(null)
  const board = useBoard(selectedBoardId ?? undefined)
  const assignCard = useAssignAgentCard(agentId)

  useEffect(() => {
    if (!opened) return
    if (boards.data?.length && !boards.data.some((item) => item.id === selectedBoardId)) {
      setSelectedBoardId(boards.data[0].id)
      setSelectedCardId(null)
    }
  }, [boards.data, opened, selectedBoardId])

  const boardOptions = useMemo(
    () => (boards.data ?? []).map((item) => ({ value: item.id, label: `${item.projectName} / ${item.name}` })),
    [boards.data],
  )

  const availableCards = useMemo(
    () => board.data?.columns.flatMap((column) => column.cards).filter((card) => !card.assignedAgentId) ?? [],
    [board.data],
  )

  const cardOptions = useMemo(
    () => availableCards.map((card) => ({ value: card.id, label: `${card.identifier} - ${card.title}` })),
    [availableCards],
  )

  const handleBoardChange = (value: string | null) => {
    setSelectedBoardId(value)
    setSelectedCardId(null)
  }

  const handleClose = () => {
    setSelectedBoardId(null)
    setSelectedCardId(null)
    onClose()
  }

  const handleAssign = () => {
    if (!selectedCardId) return

    assignCard.mutate(
      { cardId: selectedCardId },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: 'Card assigned' })
          handleClose()
        },
        onError: (error) => {
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Card assignment failed'),
          })
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={handleClose} title="Add Card">
      <Stack>
        <Select
          label="Board"
          data={boardOptions}
          value={selectedBoardId}
          onChange={handleBoardChange}
          disabled={boards.isLoading || boardOptions.length === 0}
          searchable
        />

        {board.isLoading && (
          <Group justify="center" p="md">
            <Loader size="sm" />
          </Group>
        )}

        {board.error && (
          <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
            {board.error instanceof Error ? board.error.message : 'Board failed to load'}
          </Alert>
        )}

        <Select
          label="Card"
          data={cardOptions}
          value={selectedCardId}
          onChange={setSelectedCardId}
          disabled={!selectedBoardId || board.isLoading || cardOptions.length === 0}
          searchable
        />

        <Group justify="flex-end">
          <Button variant="subtle" onClick={handleClose}>
            Cancel
          </Button>
          <Button onClick={handleAssign} loading={assignCard.isPending} disabled={!selectedCardId}>
            Assign
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
