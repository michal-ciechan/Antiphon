import { Button, Group, Modal, NumberInput, Select, Stack, Text, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState } from 'react'
import type { AgentSummaryDto } from '../../api/agents'
import { useAssignAgentCard } from '../../api/agents'
import { useBoards, useCreateCard } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'

interface AgentAddWorkModalProps {
  agent: AgentSummaryDto
  opened: boolean
  onClose: () => void
}

/**
 * Add a new piece of work to an agent: create a card (on the agent's own board, or a chosen board
 * when the agent has none) and queue it on the agent in one step.
 */
export function AgentAddWorkModal({ agent, opened, onClose }: AgentAddWorkModalProps) {
  // Only needed as a fallback when the agent has no board of its own.
  const boards = useBoards()
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState(0)
  const [pickedBoardId, setPickedBoardId] = useState<string | null>(null)

  useEffect(() => {
    if (!opened) return
    setTitle('')
    setDescription('')
    setPriority(0)
    setPickedBoardId(null)
  }, [opened])

  const targetBoardId = agent.boardId ?? pickedBoardId ?? ''
  const createCard = useCreateCard(targetBoardId)
  const assignCard = useAssignAgentCard(agent.id)

  const boardOptions = useMemo(
    () => (boards.data ?? []).map((board) => ({ value: board.id, label: `${board.projectName} / ${board.name}` })),
    [boards.data],
  )

  const pending = createCard.isPending || assignCard.isPending
  const canSubmit = title.trim().length > 0 && targetBoardId.length > 0 && !pending

  const handleSubmit = () => {
    if (!canSubmit) return

    createCard.mutate(
      { title: title.trim(), description: description.trim() || null, priority },
      {
        onSuccess: (card) => {
          // Card exists now; queue it on the agent so it becomes a piece of work.
          assignCard.mutate(
            { cardId: card.id },
            {
              onSuccess: () => {
                notifications.show({ color: 'green', message: 'Work added' })
                onClose()
              },
              onError: (error) => {
                notifications.show({
                  color: 'red',
                  message: getApiErrorMessage(error, 'Card created but could not be queued'),
                })
              },
            },
          )
        },
        onError: (error) => {
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not create the card') })
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={onClose} title="Add work" size="lg">
      <Stack>
        <TextInput
          label="Title"
          placeholder="What needs doing?"
          value={title}
          onChange={(event) => setTitle(event.currentTarget.value)}
          data-autofocus
        />
        <Textarea
          label="Description"
          autosize
          minRows={3}
          value={description}
          onChange={(event) => setDescription(event.currentTarget.value)}
        />
        <NumberInput
          label="Priority"
          value={priority}
          onChange={(value) => setPriority(typeof value === 'number' ? value : 0)}
          min={0}
        />
        {agent.boardId ? (
          <Text size="sm" c="dimmed">
            Board: {agent.boardName ?? 'agent board'}
          </Text>
        ) : (
          <Select
            label="Board"
            placeholder="Choose a board"
            data={boardOptions}
            value={pickedBoardId}
            onChange={setPickedBoardId}
            disabled={boards.isLoading || boardOptions.length === 0}
            searchable
          />
        )}
        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} loading={pending} disabled={!canSubmit}>
            Add
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
