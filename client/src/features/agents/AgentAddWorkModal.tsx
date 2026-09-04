import { Button, Group, Modal, Select, Stack, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import type { AgentSummaryDto } from '../../api/agents'
import { useAssignAgentCard, useStartAgent } from '../../api/agents'
import { useBoards, useCreateCard, type CardImportance } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'

interface AgentAddWorkModalProps {
  agent: AgentSummaryDto
  opened: boolean
  onClose: () => void
}

/**
 * Add a new piece of work to an agent: create a card and queue it on the agent in one step.
 * The card lands on the agent's DEFAULT board (every agent has one — the server backfills it),
 * pre-selected in the board picker; pick another board to override for this card only.
 */
export function AgentAddWorkModal({ agent, opened, onClose }: AgentAddWorkModalProps) {
  const boards = useBoards()
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [importance, setImportance] = useState<CardImportance>('Normal')
  const [pickedBoardId, setPickedBoardId] = useState<string | null>(agent.boardId)

  // Each open starts a fresh form — adjusted during render, not in an effect, so the previous
  // card's text can never flash before the reset lands. Keyed on the agent's board too: the
  // default board can arrive after the modal is already open, and must still pre-select.
  const [prevKey, setPrevKey] = useState({ opened, boardId: agent.boardId })
  if (opened !== prevKey.opened || agent.boardId !== prevKey.boardId) {
    setPrevKey({ opened, boardId: agent.boardId })
    if (opened) {
      setTitle('')
      setDescription('')
      setImportance('Normal')
      setPickedBoardId(agent.boardId)
    }
  }

  const targetBoardId = pickedBoardId ?? ''
  const createCard = useCreateCard(targetBoardId)
  const assignCard = useAssignAgentCard(agent.id)
  const startAgent = useStartAgent(agent.id)

  const boardOptions = useMemo(
    () => (boards.data ?? []).map((board) => ({ value: board.id, label: `${board.projectName} / ${board.name}` })),
    [boards.data],
  )

  const pending = createCard.isPending || assignCard.isPending || startAgent.isPending
  const canSubmit = title.trim().length > 0 && targetBoardId.length > 0 && !pending

  const handleSubmit = () => {
    if (!canSubmit) return

    createCard.mutate(
      { title: title.trim(), description: description.trim() || null, importance },
      {
        onSuccess: (card) => {
          // Card exists now; queue it on the agent so it becomes a piece of work.
          assignCard.mutate(
            { cardId: card.id },
            {
              onSuccess: () => {
                // Boot the agent process (no-op if it's already running). Remote control comes
                // from the agent's persisted setting; this start does not override it.
                startAgent.mutate(
                  {},
                  {
                    onSuccess: () => {
                      notifications.show({
                        color: 'green',
                        message: 'Work added — agent starting',
                      })
                      onClose()
                    },
                    onError: (error) => {
                      // The card is queued even if the agent can't start right now; let the user know but don't lose the work.
                      notifications.show({
                        color: 'yellow',
                        message: getApiErrorMessage(error, 'Work added, but the agent could not be started'),
                      })
                      onClose()
                    },
                  },
                )
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
        <Select
          label="Importance"
          data={['Low', 'Normal', 'High', 'Critical']}
          value={importance}
          onChange={(value) => setImportance((value as CardImportance) ?? 'Normal')}
        />
        <Select
          label="Board"
          description={
            agent.boardId && pickedBoardId === agent.boardId
              ? "The agent's default board"
              : undefined
          }
          placeholder="Choose a board"
          data={boardOptions}
          value={pickedBoardId}
          onChange={setPickedBoardId}
          disabled={boards.isLoading || boardOptions.length === 0}
          searchable
        />
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
