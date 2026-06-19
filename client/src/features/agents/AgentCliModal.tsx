import { Button, Group, Modal, Stack, Tabs, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbListDetails, TbPlayerPlay, TbSend, TbTerminal2 } from 'react-icons/tb'
import type { AgentSummaryDto } from '../../api/agents'
import { useAgent, useStartAgent } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { SessionTerminal } from '../board/SessionTerminal'
import { SessionMessageQueue } from './SessionMessageQueue'
import { SessionTranscriptPanel } from './SessionTranscriptPanel'

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

  const handleStart = (fresh = false) => {
    startAgent.mutate(
      { remoteControl, fresh },
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
        <Tabs defaultValue="terminal" keepMounted={false}>
          <Tabs.List mb="sm">
            <Tabs.Tab value="terminal" leftSection={<TbTerminal2 size={14} />}>
              Terminal
            </Tabs.Tab>
            <Tabs.Tab value="messages" leftSection={<TbSend size={14} />}>
              Messages
            </Tabs.Tab>
            <Tabs.Tab value="transcript" leftSection={<TbListDetails size={14} />}>
              Transcript
            </Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="terminal">
            <SessionTerminal session={liveSession} />
          </Tabs.Panel>
          <Tabs.Panel value="messages">
            <SessionMessageQueue sessionId={liveSession.id} />
          </Tabs.Panel>
          <Tabs.Panel value="transcript">
            <SessionTranscriptPanel sessionId={liveSession.id} />
          </Tabs.Panel>
        </Tabs>
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
              Nothing is queued, so this starts a human-driven terminal you can type into directly. It
              resumes the agent&apos;s previous Claude session when one exists — use &quot;Start fresh&quot;
              for a clean conversation.
            </Text>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            {!hasCard && (
              <Button variant="default" loading={startAgent.isPending} onClick={() => handleStart(true)}>
                Start fresh
              </Button>
            )}
            <Button
              leftSection={<TbPlayerPlay size={16} />}
              loading={startAgent.isPending}
              onClick={() => handleStart()}
            >
              Start agent
            </Button>
          </Group>
        </Stack>
      )}
    </Modal>
  )
}
