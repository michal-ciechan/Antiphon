import { Button, Checkbox, Group, Modal, NumberInput, Select, Stack, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState } from 'react'
import type { AgentSummaryDto } from '../../api/agents'
import { useAssignAgentCard, useStartAgent } from '../../api/agents'
import { useBoards, useCreateCard } from '../../api/boards'
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
  const [priority, setPriority] = useState(0)
  const [pickedBoardId, setPickedBoardId] = useState<string | null>(null)
  const [remoteControl, setRemoteControl] = useState(true)

  useEffect(() => {
    if (!opened) return
    setTitle('')
    setDescription('')
    setPriority(0)
    setPickedBoardId(agent.boardId)
    setRemoteControl(true)
  }, [opened, agent.boardId])

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
      { title: title.trim(), description: description.trim() || null, priority },
      {
        onSuccess: (card) => {
          // Card exists now; queue it on the agent so it becomes a piece of work.
          assignCard.mutate(
            { cardId: card.id },
            {
              onSuccess: () => {
                // Boot the agent process (no-op if it's already running). When remote control is
                // ticked a freshly booted agent is renamed + put into /remote-control first.
                startAgent.mutate(
                  { remoteControl },
                  {
                    onSuccess: () => {
                      notifications.show({
                        color: 'green',
                        message: remoteControl ? 'Work added — agent starting (remote control)' : 'Work added — agent starting',
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
        <NumberInput
          label="Priority"
          value={priority}
          onChange={(value) => setPriority(typeof value === 'number' ? value : 0)}
          min={0}
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
        <Checkbox
          label="Remote control"
          description="Rename the agent and put it into /remote-control before the work, so you can monitor it."
          checked={remoteControl}
          onChange={(event) => setRemoteControl(event.currentTarget.checked)}
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
