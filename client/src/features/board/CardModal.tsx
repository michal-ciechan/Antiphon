import { Badge, Button, Divider, Group, Modal, Stack, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbPlayerPlay } from 'react-icons/tb'
import type { CardDto } from '../../api/boards'
import { useSpawnCard } from '../../api/boards'
import { AgentPicker } from './AgentPicker'
import { SessionTabs } from './SessionTabs'

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

  if (!card) return null

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
    <Modal opened={opened} onClose={onClose} title={card.identifier} size="85rem">
      <Stack gap="md">
        <Group justify="space-between" align="flex-start">
          <Stack gap={4}>
            <Title order={3}>{card.title}</Title>
            <Group gap={6}>
              <Badge variant="light">{card.status}</Badge>
              <Badge color="gray" variant="outline">P{card.priority}</Badge>
              {card.labels.map((label) => (
                <Badge key={label} color="active" variant="light">{label}</Badge>
              ))}
            </Group>
          </Stack>
          <Group align="end">
            <AgentPicker value={definitionName} onChange={setDefinitionName} />
            <Button
              leftSection={<TbPlayerPlay size={16} />}
              onClick={handleSpawn}
              loading={spawnCard.isPending}
              disabled={hasActiveSession}
            >
              Spawn
            </Button>
          </Group>
        </Group>

        <Text size="sm" c={card.description ? undefined : 'dimmed'} style={{ whiteSpace: 'pre-wrap' }}>
          {card.description || 'No description'}
        </Text>

        <Divider />

        <SessionTabs sessions={card.sessions} />
      </Stack>
    </Modal>
  )
}
