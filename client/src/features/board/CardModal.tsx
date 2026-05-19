import { ActionIcon, Badge, Box, Button, Group, Modal, ScrollArea, Stack, Tabs, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbInfoCircle, TbPlayerPlay, TbTerminal2, TbX } from 'react-icons/tb'
import type { CardDto } from '../../api/boards'
import { useSpawnCard } from '../../api/boards'
import { AgentPicker } from './AgentPicker'
import { DiffReview } from './DiffReview'
import { SessionTabs } from './SessionTabs'
import './CardModal.css'

interface CardModalProps {
  boardId: string
  card: CardDto | null
  opened: boolean
  onClose: () => void
}

export function CardModal({ boardId, card, opened, onClose }: CardModalProps) {
  const [definitionName, setDefinitionName] = useState<string | null>(null)
  const spawnCard = useSpawnCard(boardId)
  const hasActiveSession = useMemo(
    () => card?.sessions.some((session) =>
      session.status === 'Starting'
      || session.status === 'Running'
      || session.status === 'Stopping') ?? false,
    [card?.sessions],
  )
  const showDiffReview = !!card?.currentWorktreeId && (card.status === 'Review' || card.status === 'Done')

  if (!card) return null
  const description = card.description.trim()
  const activeSessionCount = card.sessions.filter((session) =>
    session.status === 'Starting' || session.status === 'Running' || session.status === 'Stopping',
  ).length

  const handleSpawn = () => {
    spawnCard.mutate(
      {
        cardId: card.id,
        request: {
          definitionName,
          cols: 120,
          rows: 30,
        },
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: 'Session queued' })
        },
        onError: (error) => {
          notifications.show({ color: 'red', message: error instanceof Error ? error.message : 'Spawn failed' })
        },
      },
    )
  }

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      fullScreen
      padding={0}
      withCloseButton={false}
      styles={{
        content: { backgroundColor: 'var(--mantine-color-body)' },
        body: { height: '100vh', padding: 0 },
      }}
    >
      <Box className="card-page" data-testid="card-detail-page">
        <Group className="card-page__header" justify="space-between" wrap="nowrap">
          <Stack gap={2} className="card-page__titleBlock">
            <Group gap={6} wrap="nowrap" className="card-page__titleLine">
              <Badge color="gray" variant="outline">{card.identifier}</Badge>
              <Title order={3} className="card-page__title">
                {card.title}
              </Title>
            </Group>
            <Group gap={6} wrap="nowrap" className="card-page__badges">
              <Badge variant="light">{card.status}</Badge>
              <Badge color="gray" variant="outline">P{card.priority}</Badge>
              {activeSessionCount > 0 && (
                <Badge color="green" variant="light">
                  {activeSessionCount} active
                </Badge>
              )}
              {card.labels.map((label) => (
                <Badge key={label} color="active" variant="light">{label}</Badge>
              ))}
            </Group>
          </Stack>
          <Group gap="xs" wrap="nowrap" className="card-page__actions">
            <AgentPicker value={definitionName} onChange={setDefinitionName} compact />
            <Button
              size="xs"
              leftSection={<TbPlayerPlay size={16} />}
              onClick={handleSpawn}
              loading={spawnCard.isPending}
              disabled={hasActiveSession}
            >
              Spawn
            </Button>
            <ActionIcon variant="subtle" aria-label="Close card" onClick={onClose}>
              <TbX size={18} />
            </ActionIcon>
          </Group>
        </Group>

        <Box className="card-page__body">
          <Box className="card-page__workspace">
            <Tabs defaultValue="sessions" keepMounted={false} className="card-page__tabs">
              <Tabs.List className="card-page__tabsList">
                <Tabs.Tab value="sessions" leftSection={<TbTerminal2 size={14} />}>
                  Sessions
                </Tabs.Tab>
                {showDiffReview && (
                  <Tabs.Tab value="diff">
                    Diff
                  </Tabs.Tab>
                )}
                <Tabs.Tab value="details" leftSection={<TbInfoCircle size={14} />} className="card-page__detailsTab">
                  Details
                </Tabs.Tab>
              </Tabs.List>

              <Tabs.Panel value="sessions" className="card-page__panel">
                <Box className="card-page__panelInner">
                  <SessionTabs boardId={boardId} sessions={card.sessions} compact fill />
                </Box>
              </Tabs.Panel>

              {showDiffReview && (
                <Tabs.Panel value="diff" className="card-page__panel">
                  <ScrollArea h="100%" type="auto" offsetScrollbars>
                    <Box p="xs">
                      <DiffReview boardId={boardId} card={card} />
                    </Box>
                  </ScrollArea>
                </Tabs.Panel>
              )}

              <Tabs.Panel value="details" className="card-page__panel card-page__detailsPanel">
                <ScrollArea h="100%" type="auto" offsetScrollbars>
                  <CardDetails card={card} description={description} />
                </ScrollArea>
              </Tabs.Panel>
            </Tabs>
          </Box>

          <Box component="aside" className="card-page__sidebar" data-testid="card-detail-sidebar">
            <ScrollArea h="100%" type="auto" offsetScrollbars>
              <CardDetails card={card} description={description} />
            </ScrollArea>
          </Box>
        </Box>
      </Box>
    </Modal>
  )
}

function CardDetails({ card, description }: { card: CardDto; description: string }) {
  return (
    <Stack gap="sm" p="sm">
      <Stack gap={4}>
        <Text size="xs" c="dimmed" fw={700} tt="uppercase">Description</Text>
        <Text
          size="sm"
          c={description ? undefined : 'dimmed'}
          className="card-page__description"
        >
          {description || 'No description'}
        </Text>
      </Stack>

      <Box className="card-page__metaGrid">
        <Text size="xs" c="dimmed">Status</Text>
        <Text size="xs" fw={600}>{card.status}</Text>
        <Text size="xs" c="dimmed">Priority</Text>
        <Text size="xs" fw={600}>P{card.priority}</Text>
        <Text size="xs" c="dimmed">Sessions</Text>
        <Text size="xs" fw={600}>{card.sessions.length}</Text>
        {card.assignedAgentName && (
          <>
            <Text size="xs" c="dimmed">Agent</Text>
            <Text size="xs" fw={600} truncate>{card.assignedAgentName}</Text>
          </>
        )}
        {card.currentWorkflowStageName && (
          <>
            <Text size="xs" c="dimmed">Stage</Text>
            <Text size="xs" fw={600} truncate>{card.currentWorkflowStageName}</Text>
          </>
        )}
      </Box>
    </Stack>
  )
}
