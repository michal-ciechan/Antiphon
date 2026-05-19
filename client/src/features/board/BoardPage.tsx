import {
  Alert,
  Box,
  Button,
  Group,
  Loader,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { DndContext, type DragEndEvent, PointerSensor, useSensor, useSensors } from '@dnd-kit/core'
import { useMemo, useState } from 'react'
import { TbAlertCircle, TbFileCode, TbPlus } from 'react-icons/tb'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import { useProjects } from '../../api/projects'
import {
  type BoardColumnDto,
  type BoardDetailDto,
  type CardDto,
  type CardStatus,
  useAllBoardDetails,
  useBoard,
  useBoards,
  useCreateBoard,
  useCreateCard,
  useMoveCard,
} from '../../api/boards'
import { BoardColumn } from './BoardColumn'
import { CardModal } from './CardModal'
import { WorkflowEditor } from './WorkflowEditor'

interface BoardRouteParams {
  id?: string
}

const ALL_BOARDS_VALUE = '__all__'
const ALL_CARD_COLUMNS: Array<{
  stateKey: string
  name: string
  cardStatus: CardStatus
  isActive: boolean
  isTerminal: boolean
}> = [
  { stateKey: 'backlog', name: 'Backlog', cardStatus: 'Backlog', isActive: false, isTerminal: false },
  { stateKey: 'in-progress', name: 'In Progress', cardStatus: 'InProgress', isActive: true, isTerminal: false },
  { stateKey: 'review', name: 'Review', cardStatus: 'Review', isActive: false, isTerminal: false },
  { stateKey: 'done', name: 'Done', cardStatus: 'Done', isActive: false, isTerminal: true },
  { stateKey: 'blocked', name: 'Blocked', cardStatus: 'Blocked', isActive: false, isTerminal: false },
  { stateKey: 'canceled', name: 'Canceled', cardStatus: 'Canceled', isActive: false, isTerminal: true },
]

export function BoardPage() {
  const { id } = useParams() as BoardRouteParams
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { data: boards, isLoading: boardsLoading } = useBoards()
  const { data: board, isLoading: boardLoading, error } = useBoard(id)
  const boardIds = useMemo(() => (boards ?? []).map((item) => item.id), [boards])
  const allBoards = useAllBoardDetails(boardIds, !id && !boardsLoading)
  const selectedBoardLoading = !!id && boardLoading
  const moveCard = useMoveCard(id ?? '')
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))
  const selectedCardId = searchParams.get('card')
  const [newBoardOpen, setNewBoardOpen] = useState(false)
  const [newCardOpen, setNewCardOpen] = useState(false)
  const [workflowOpen, setWorkflowOpen] = useState(false)

  const selectedCard = useMemo(() => {
    if (!board || !selectedCardId) return null
    return board.columns.flatMap((column) => column.cards).find((card) => card.id === selectedCardId) ?? null
  }, [board, selectedCardId])
  const selectedAllCard = useMemo(() => {
    if (!selectedCardId) return null
    return (allBoards.data ?? [])
      .flatMap((item) => item.columns.flatMap((column) => column.cards))
      .find((card) => card.id === selectedCardId) ?? null
  }, [allBoards.data, selectedCardId])

  const boardOptions = useMemo(
    () => [
      { value: ALL_BOARDS_VALUE, label: 'All' },
      ...(boards ?? []).map((item) => ({ value: item.id, label: `${item.projectName} / ${item.name}` })),
    ],
    [boards],
  )

  const handleDragEnd = (event: DragEndEvent) => {
    if (!board || !event.over || event.active.id === event.over.id) return
    const card = findCard(board.columns.flatMap((column) => column.cards), String(event.active.id))
    if (!card || card.boardColumnId === event.over.id) return

    moveCard.mutate(
      {
        cardId: card.id,
        request: {
          boardColumnId: String(event.over.id),
          concurrencyToken: card.concurrencyToken,
        },
      },
      {
        onError: (mutationError) => {
          notifications.show({
            color: 'red',
            message: mutationError instanceof Error ? mutationError.message : 'Move failed',
          })
        },
      },
    )
  }

  const handleBoardSelection = (value: string | null) => {
    if (!value) return
    setNewCardOpen(false)
    setWorkflowOpen(false)
    navigate(value === ALL_BOARDS_VALUE ? '/boards' : `/boards/${value}`)
  }

  const openCard = (cardId: string) => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current)
      next.set('card', cardId)
      return next
    })
  }

  const closeCard = () => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current)
      next.delete('card')
      return next
    }, { replace: true })
  }

  return (
    <Box p="md">
      <BoardCreateModal opened={newBoardOpen} onClose={() => setNewBoardOpen(false)} />
      {board && (
        <>
          <CardCreateModal
            boardId={board.id}
            opened={newCardOpen}
            onClose={() => setNewCardOpen(false)}
          />
          <CardModal
            boardId={board.id}
            card={selectedCard}
            opened={!!selectedCard}
            onClose={closeCard}
          />
          <WorkflowEditor
            boardId={board.id}
            opened={workflowOpen}
            onClose={() => setWorkflowOpen(false)}
          />
        </>
      )}
      {!board && selectedAllCard && (
        <CardModal
          boardId={selectedAllCard.boardId}
          card={selectedAllCard}
          opened
          onClose={closeCard}
        />
      )}

      <Group justify="space-between" mb="md" align="flex-end">
        <Stack gap={2}>
          <Title order={2}>Boards</Title>
          {board && (
            <Text size="sm" c="dimmed">
              {board.projectName} / {board.name}
            </Text>
          )}
          {!board && !boardsLoading && boards && boards.length > 0 && (
            <Text size="sm" c="dimmed">
              All cards
            </Text>
          )}
        </Stack>
        <Group>
          <Select
            placeholder="Select board"
            data={boardOptions}
            value={id ?? ALL_BOARDS_VALUE}
            onChange={handleBoardSelection}
            w={320}
            searchable
            maxDropdownHeight={420}
          />
          <Button leftSection={<TbPlus size={16} />} variant="light" onClick={() => setNewBoardOpen(true)}>
            New Board
          </Button>
          {board && (
            <>
              <Button leftSection={<TbFileCode size={16} />} variant="light" onClick={() => setWorkflowOpen(true)}>
                Workflow
              </Button>
              <Button leftSection={<TbPlus size={16} />} onClick={() => setNewCardOpen(true)}>
                New Card
              </Button>
            </>
          )}
        </Group>
      </Group>

      {(boardsLoading || selectedBoardLoading) && (
        <Group justify="center" p="xl">
          <Loader />
        </Group>
      )}

      {!boardsLoading && boards?.length === 0 && (
        <Alert icon={<TbAlertCircle size={18} />} color="yellow" variant="light">
          Create a board from a project to start card-driven agent work.
        </Alert>
      )}

      {error && (
        <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
          {error instanceof Error ? error.message : 'Board failed to load'}
        </Alert>
      )}

      {board && (
        <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
          <ScrollArea type="auto" offsetScrollbars>
            <Group align="flex-start" wrap="nowrap" gap="md" pb="sm">
              {board.columns.map((column) => (
                <BoardColumn key={column.id} column={column} onOpenCard={openCard} />
              ))}
            </Group>
          </ScrollArea>
        </DndContext>
      )}

      {!id && !boardsLoading && boards && boards.length > 0 && (
        <AllCardsBoard
          boards={allBoards.data ?? []}
          loading={allBoards.isLoading}
          error={allBoards.error}
          onOpenCard={openCard}
        />
      )}
    </Box>
  )
}

function AllCardsBoard({
  boards,
  loading,
  error,
  onOpenCard,
}: {
  boards: BoardDetailDto[]
  loading: boolean
  error: Error | null
  onOpenCard: (cardId: string) => void
}) {
  const columns = useMemo<BoardColumnDto[]>(() => {
    const allCards = boards.flatMap((item) =>
      item.columns.flatMap((column) =>
        column.cards.map((card) => ({
          ...card,
          labels: [item.name, ...card.labels.filter((label) => label !== item.name)],
        })),
      ),
    )

    return ALL_CARD_COLUMNS.map((column, index) => ({
      id: `all-${column.stateKey}`,
      boardId: 'all',
      stateKey: column.stateKey,
      name: column.name,
      columnOrder: index,
      cardStatus: column.cardStatus,
      isActive: column.isActive,
      isTerminal: column.isTerminal,
      maxConcurrentSessions: null,
      cards: allCards.filter((card) => card.status === column.cardStatus),
    }))
  }, [boards])

  if (loading) {
    return (
      <Group justify="center" p="xl">
        <Loader />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
        {error.message}
      </Alert>
    )
  }

  const cardCount = columns.reduce((total, column) => total + column.cards.length, 0)

  if (cardCount === 0) {
    return (
      <Paper withBorder p="xl">
        <Text ta="center" c="dimmed">
          No cards across any board.
        </Text>
      </Paper>
    )
  }

  return (
    <DndContext>
      <ScrollArea type="auto" offsetScrollbars>
        <Group align="flex-start" wrap="nowrap" gap="md" pb="sm">
          {columns.map((column) => (
            <BoardColumn key={column.id} column={column} onOpenCard={onOpenCard} />
          ))}
        </Group>
      </ScrollArea>
    </DndContext>
  )
}

function BoardCreateModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const { data: projects } = useProjects()
  const createBoard = useCreateBoard()
  const [projectId, setProjectId] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [maxConcurrentSessions, setMaxConcurrentSessions] = useState<number | string>(1)

  const handleSubmit = () => {
    if (!projectId || !name.trim()) return
    createBoard.mutate(
      {
        projectId,
        name,
        description,
        maxConcurrentSessions: Number(maxConcurrentSessions) || 1,
      },
      {
        onSuccess: () => {
          onClose()
          setName('')
          setDescription('')
          navigate('/boards')
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={onClose} title="New Board">
      <Stack>
        <Select
          label="Project"
          data={(projects ?? []).map((project) => ({ value: project.id, label: project.name }))}
          value={projectId}
          onChange={setProjectId}
          searchable
        />
        <TextInput label="Name" value={name} onChange={(event) => setName(event.currentTarget.value)} />
        <Textarea label="Description" value={description} onChange={(event) => setDescription(event.currentTarget.value)} />
        <NumberInput
          label="Max sessions"
          min={1}
          value={maxConcurrentSessions}
          onChange={setMaxConcurrentSessions}
        />
        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button onClick={handleSubmit} loading={createBoard.isPending} disabled={!projectId || !name.trim()}>
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

function CardCreateModal({
  boardId,
  opened,
  onClose,
}: {
  boardId: string
  opened: boolean
  onClose: () => void
}) {
  const createCard = useCreateCard(boardId)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState<number | string>(1)
  const [labels, setLabels] = useState('')

  const handleSubmit = () => {
    if (!title.trim()) return
    createCard.mutate(
      {
        title,
        description,
        priority: Number(priority) || 0,
        labels: labels.split(',').map((label) => label.trim()).filter(Boolean),
      },
      {
        onSuccess: () => {
          setTitle('')
          setDescription('')
          setPriority(1)
          setLabels('')
          onClose()
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={onClose} title="New Card">
      <Stack>
        <TextInput label="Title" value={title} onChange={(event) => setTitle(event.currentTarget.value)} />
        <Textarea label="Description" value={description} onChange={(event) => setDescription(event.currentTarget.value)} />
        <NumberInput label="Priority" min={0} value={priority} onChange={setPriority} />
        <TextInput label="Labels" value={labels} onChange={(event) => setLabels(event.currentTarget.value)} />
        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button onClick={handleSubmit} loading={createCard.isPending} disabled={!title.trim()}>
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

function findCard(cards: CardDto[], cardId: string) {
  return cards.find((card) => card.id === cardId)
}
