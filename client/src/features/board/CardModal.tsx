import { ActionIcon, Anchor, Badge, Box, Button, Group, Modal, ScrollArea, Stack, Tabs, Text, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbHistory, TbInfoCircle, TbMessage, TbPencil, TbPlayerPlay, TbTerminal2, TbTimeline, TbX } from 'react-icons/tb'
import type { BoardColumnDto, CardDto } from '../../api/boards'
import { useCardDiscussion, useSpawnCard } from '../../api/boards'
import { displayIdentifier, externalIssueTag } from '../../shared/cardIdentifier'
import { AgentPicker } from './AgentPicker'
import { CardDiscussionPanel } from './CardDiscussionPanel'
import { CardEditModal } from './CardEditModal'
import { CardHistory } from './CardHistory'
import { DiffReview } from './DiffReview'
import { MoveMenu } from './MoveMenu'
import { SessionTabs } from './SessionTabs'
import { CardThreadPanel } from '../thread/CardThreadPanel'
import './CardModal.css'

interface CardModalProps {
  boardId: string
  card: CardDto | null
  /** The board's states, so the card can be moved from here. Empty on the all-boards view. */
  columns?: BoardColumnDto[]
  opened: boolean
  onClose: () => void
}

export function CardModal({ boardId, card, columns = [], opened, onClose }: CardModalProps) {
  const [definitionName, setDefinitionName] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  // On a phone the thread IS the card's landing view (spec §4): what happened, what's stuck, and
  // the one-tap verbs — the terminal is the desktop's default, not the thumb's.
  const isMobile = useMediaQuery('(max-width: 48em)') ?? false
  const spawnCard = useSpawnCard(boardId)
  const discussion = useCardDiscussion(card?.id, opened && !!card)
  const discussionCount = discussion.data?.length ?? 0
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
  const archived = !!card.archivedAt
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
              <Badge color="gray" variant="outline" title={card.identifier}>
                {displayIdentifier(card.identifier)}
              </Badge>
              {card.externalIssue && (
                card.externalIssue.url ? (
                  <Anchor
                    href={card.externalIssue.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    size="sm"
                    c="dimmed"
                  >
                    {externalIssueTag(card.externalIssue)} ↗
                  </Anchor>
                ) : (
                  <Text size="sm" c="dimmed">{externalIssueTag(card.externalIssue)}</Text>
                )
              )}
              <Title order={3} className="card-page__title">
                {card.title}
              </Title>
            </Group>
            <Group gap={6} wrap="nowrap" className="card-page__badges">
              {archived && (
                <Badge color="gray" variant="filled" title={card.archivedReason ?? undefined}>
                  archived
                </Badge>
              )}
              <Badge variant="light">{card.status}</Badge>
              <Badge color="gray" variant="outline">P{card.priority}</Badge>
              {activeSessionCount > 0 && (
                <Badge color="green" variant="light">
                  {activeSessionCount} active
                </Badge>
              )}
              {discussionCount > 0 && (
                <Badge color="blue" variant="light" leftSection={<TbMessage size={12} />}>
                  {discussionCount}
                </Badge>
              )}
              {card.labels.map((label) => (
                <Badge key={label} color="active" variant="light">{label}</Badge>
              ))}
            </Group>
          </Stack>
          <Group gap="xs" wrap="nowrap" className="card-page__actions">
            {/*
              Not a pencil beside the title: identifier, title and badges already share one line
              (and collapse further on a phone), and this group is where every other card-level
              action sits.
            */}
            <ActionIcon variant="subtle" aria-label="Edit card" onClick={() => setEditing(true)}>
              <TbPencil size={18} />
            </ActionIcon>
            {columns.length > 0 && (
              <MoveMenu
                boardId={boardId}
                card={card}
                columns={columns}
                variant="button"
                // The archived card leaves the default board payload, so this page would resolve
                // to nothing anyway — closing explicitly is the difference between a clean exit
                // and a modal that blinks out mid-refetch.
                onArchived={onClose}
              />
            )}
            <AgentPicker value={definitionName} onChange={setDefinitionName} compact />
            <Button
              size="xs"
              leftSection={<TbPlayerPlay size={16} />}
              onClick={handleSpawn}
              loading={spawnCard.isPending}
              disabled={hasActiveSession || archived}
              title={archived ? 'Unarchive this card before starting work on it' : undefined}
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
            <Tabs defaultValue={isMobile ? 'thread' : 'sessions'} keepMounted={false} className="card-page__tabs">
              <Tabs.List className="card-page__tabsList">
                <Tabs.Tab value="sessions" leftSection={<TbTerminal2 size={14} />}>
                  Sessions
                </Tabs.Tab>
                <Tabs.Tab value="thread" leftSection={<TbTimeline size={14} />}>
                  Thread
                </Tabs.Tab>
                {showDiffReview && (
                  <Tabs.Tab value="diff">
                    Diff
                  </Tabs.Tab>
                )}
                {/*
                  `revisionCount` counts MOVES as well as edits, so it is a count and nothing
                  else — never an "edited" badge.
                */}
                <Tabs.Tab value="history" leftSection={<TbHistory size={14} />}>
                  History ({card.revisionCount})
                </Tabs.Tab>
                <Tabs.Tab value="discussion" leftSection={<TbMessage size={14} />}>
                  Discussion{discussionCount > 0 ? ` (${discussionCount})` : ''}
                </Tabs.Tab>
                <Tabs.Tab value="details" leftSection={<TbInfoCircle size={14} />} className="card-page__detailsTab">
                  Details
                </Tabs.Tab>
              </Tabs.List>

              <Tabs.Panel value="sessions" className="card-page__panel">
                <Box className="card-page__panelInner">
                  <SessionTabs boardId={boardId} sessions={card.sessions} compact fill />
                </Box>
              </Tabs.Panel>

              {/* Lazy like History: `keepMounted={false}` means the thread projection is not
                  fetched until the tab is opened (or is the mobile default). */}
              <Tabs.Panel value="thread" className="card-page__panel">
                <ScrollArea h="100%" type="auto" offsetScrollbars>
                  <CardThreadPanel identifier={card.identifier} boardId={boardId} columns={columns} />
                </ScrollArea>
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

              {/* `keepMounted={false}` above is what makes this lazy: the revisions query does
                  not fire until the tab is first opened. */}
              <Tabs.Panel value="history" className="card-page__panel">
                <ScrollArea h="100%" type="auto" offsetScrollbars>
                  <CardHistory cardId={card.id} columns={columns} />
                </ScrollArea>
              </Tabs.Panel>

              <Tabs.Panel value="discussion" className="card-page__panel">
                <ScrollArea h="100%" type="auto" offsetScrollbars>
                  <CardDiscussionPanel cardId={card.id} />
                </ScrollArea>
              </Tabs.Panel>

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

      {editing && (
        <CardEditModal boardId={boardId} card={card} onClose={() => setEditing(false)} />
      )}
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
