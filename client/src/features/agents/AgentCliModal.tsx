import { Button, Group, Modal, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbPlayerPlay, TbTerminal2 } from 'react-icons/tb'
import type { AgentSummaryDto } from '../../api/agents'
import { useAgent, useStartAgent } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { SessionTerminal } from '../board/SessionTerminal'

interface AgentCliModalProps {
  agent: AgentSummaryDto
  remoteControl: boolean
  opened: boolean
  onClose: () => void
}

/**
 * Opens the agent's currently running terminal. If no live session exists, it offers to
 * start the agent — and the moment a session boots (Start succeeds), the live detail query
 * updates and the terminal takes over the same modal.
 */
export function AgentCliModal({ agent, remoteControl, opened, onClose }: AgentCliModalProps) {
  const detail = useAgent(agent.id)
  const startAgent = useStartAgent(agent.id)

  const source = detail.data ?? agent
  const liveSession = source.liveSession
  const hasCard = Boolean(source.currentCardId) || source.queueLength > 0

  const handleStart = () => {
    startAgent.mutate(
      { remoteControl },
      {
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not start the agent') }),
      },
    )
  }

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      size="xl"
      title={
        <Group gap="xs" wrap="nowrap">
          <TbTerminal2 size={18} />
          <Text fw={600} lineClamp={1}>
            {source.name} terminal
          </Text>
        </Group>
      }
    >
      {liveSession ? (
        <SessionTerminal session={liveSession} />
      ) : (
        <Stack gap="md" py="sm">
          <Text>
            No terminal is running for <strong>{source.name}</strong>. Start the agent to open one
            {hasCard
              ? ' on its next queued card'
              : ' as an interactive session in its working directory'}
            {remoteControl ? ' (remote control on)' : ''}?
          </Text>
          {!hasCard && (
            <Text size="sm" c="dimmed">
              Nothing is queued, so this starts a human-driven terminal you can type into directly.
            </Text>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button leftSection={<TbPlayerPlay size={16} />} loading={startAgent.isPending} onClick={handleStart}>
              Start agent
            </Button>
          </Group>
        </Stack>
      )}
    </Modal>
  )
}
