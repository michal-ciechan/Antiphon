import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  Loader,
  Paper,
  SimpleGrid,
  Stack,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useMemo, useState } from 'react'
import { TbAlertCircle, TbRefresh } from 'react-icons/tb'
import { useBoards, useCards } from '../../api/boards'
import { SortableCardList } from '../board/SortableCardList'
import { BACKLOG_BOX_CAP, boardsPresent, groupBacklog, type BacklogBox as BacklogBoxModel } from './backlogModel'
import { BacklogRow } from './BacklogRow'

const TRUNCATION_SENTENCE = 'Showing the 500 most recently updated cards; open the board for the rest.'

/**
 * Outstanding Backlog across every board, grouped by quadrant. Lives next to OrchestratorPanel
 * rather than inside it so a cards-list failure cannot blank Running Sessions.
 */
export function BacklogSection() {
  const cards = useCards({ status: 'Backlog' })
  const boards = useBoards()
  const isMobile = useMediaQuery('(max-width: 48em)') ?? false
  const [now] = useState(() => new Date())

  const list = useMemo(() => cards.data?.cards ?? [], [cards.data?.cards])
  const boxes = useMemo(() => groupBacklog(list), [list])
  const boardCount = boardsPresent(list)
  const showBoard = boardCount > 1
  const boardNameById = useMemo(
    () => new Map((boards.data ?? []).map((board) => [board.id, board.name])),
    [boards.data],
  )

  if (cards.error || boards.error) {
    const error = cards.error ?? boards.error
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Could not load backlog" data-testid="backlog-error">
        {error instanceof Error ? error.message : 'No response from the server.'}
      </Alert>
    )
  }

  if (cards.isLoading || boards.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <Title order={4}>Backlog</Title>
          <Badge variant="light">{list.length} outstanding</Badge>
          <Text size="sm" c="dimmed">
            on {boardCount} {boardCount === 1 ? 'board' : 'boards'}
          </Text>
        </Group>
        <Tooltip label="Refresh backlog">
          <ActionIcon
            variant="subtle"
            onClick={() => {
              void cards.refetch()
              void boards.refetch()
            }}
            loading={cards.isFetching || boards.isFetching}
          >
            <TbRefresh />
          </ActionIcon>
        </Tooltip>
      </Group>
      {cards.data?.truncated && (
        <Text size="sm" c="dimmed">{TRUNCATION_SENTENCE}</Text>
      )}
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="sm">
        {boxes.map((box) => (
          <BacklogBox
            key={box.quadrant}
            box={box}
            showBoard={showBoard}
            boardNameById={boardNameById}
            now={now}
            stacked={isMobile}
            reorderable={!showBoard}
          />
        ))}
      </SimpleGrid>
    </Stack>
  )
}

function BacklogBox({
  box,
  showBoard,
  boardNameById,
  now,
  stacked,
  reorderable,
}: {
  box: BacklogBoxModel
  showBoard: boolean
  boardNameById: Map<string, string>
  now: Date
  stacked: boolean
  reorderable: boolean
}) {
  const [expanded, setExpanded] = useState(false)
  const overCap = box.cards.length > BACKLOG_BOX_CAP
  const visible = expanded || !overCap ? box.cards : box.cards.slice(0, BACKLOG_BOX_CAP)

  return (
    <Paper withBorder p="xs" data-testid={`backlog-box-${box.quadrant}`}>
      <Group justify="space-between" mb={4} wrap="nowrap">
        <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
          <Text size="xs" tt="uppercase" fw={700}>{box.label}</Text>
          <Badge size="xs" variant="default">{box.cards.length}</Badge>
        </Group>
      </Group>
      <Text size="xs" c="dimmed" mb={6}>
        {box.hint}
        {showBoard ? ' · reorder on the board' : ''}
      </Text>
      {box.cards.length === 0 ? (
        <Text size="sm" c="dimmed" px="xs" py="sm">Nothing here.</Text>
      ) : reorderable ? (
        <SortableCardList
          cards={visible}
          boardId={visible[0].boardId}
          columns={[]}
          now={now}
          enabled
          renderItem={(card, canReorder) => (
            <BacklogRow
              card={card}
              boardName={boardNameById.get(card.boardId)}
              showBoard={false}
              now={now}
              layout={stacked ? 'stacked' : 'row'}
              reorderable={canReorder}
            />
          )}
        />
      ) : (
        <Stack gap={0} style={expanded ? { maxHeight: 560, overflowY: 'auto' } : undefined}>
          {visible.map((card) => (
            <BacklogRow
              key={card.id}
              card={card}
              boardName={boardNameById.get(card.boardId)}
              showBoard={showBoard}
              now={now}
              layout={stacked ? 'stacked' : 'row'}
            />
          ))}
        </Stack>
      )}
      {overCap && (
        <Button size="xs" variant="subtle" mt={6} onClick={() => setExpanded((current) => !current)}>
          {expanded ? 'Show fewer' : `Show all ${box.cards.length}`}
        </Button>
      )}
    </Paper>
  )
}
