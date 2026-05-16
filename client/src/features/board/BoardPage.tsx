import {
  Alert,
  Box,
  Button,
  Group,
  Loader,
  Modal,
  NumberInput,
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
import { useEffect, useMemo, useState } from 'react'
import { TbAlertCircle, TbPlus } from 'react-icons/tb'
import { Navigate, useNavigate, useParams } from 'react-router'
import { useProjects } from '../../api/projects'
import {
  type CardDto,
  useBoard,
  useBoards,
  useCreateBoard,
  useCreateCard,
  useMoveCard,
} from '../../api/boards'
import { BoardColumn } from './BoardColumn'
import { CardModal } from './CardModal'

interface BoardRouteParams {
  id?: string
}

export function BoardPage() {
  const { id } = useParams() as BoardRouteParams
  const navigate = useNavigate()
  const { data: boards, isLoading: boardsLoading } = useBoards()
  const { data: board, isLoading: boardLoading, error } = useBoard(id)
  const moveCard = useMoveCard(id ?? '')
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))
  const [selectedCardId, setSelectedCardId] = useState<string | null>(null)
  const [newBoardOpen, setNewBoardOpen] = useState(false)
  const [newCardOpen, setNewCardOpen] = useState(false)

  useEffect(() => {
    setSelectedCardId(null)
    setNewBoardOpen(false)
    setNewCardOpen(false)
  }, [id])

  const selectedCard = useMemo(() => {
    if (!board || !selectedCardId) return null
    return board.columns.flatMap((column) => column.cards).find((card) => card.id === selectedCardId) ?? null
  }, [board, selectedCardId])

  if (!id && boards && boards.length > 0) {
    return <Navigate to={`/boards/${boards[0].id}`} replace />
  }

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
            onClose={() => setSelectedCardId(null)}
          />
        </>
      )}

      <Group justify="space-between" mb="md" align="flex-end">
        <Stack gap={2}>
          <Title order={2}>Boards</Title>
          {board && (
            <Text size="sm" c="dimmed">
              {board.projectName} / {board.name}
            </Text>
          )}
        </Stack>
        <Group>
          <Select
            placeholder="Select board"
            data={(boards ?? []).map((item) => ({ value: item.id, label: `${item.projectName} / ${item.name}` }))}
            value={id ?? null}
            onChange={(value) => value && navigate(`/boards/${value}`)}
            w={320}
            searchable
          />
          <Button leftSection={<TbPlus size={16} />} variant="light" onClick={() => setNewBoardOpen(true)}>
            New Board
          </Button>
          {board && (
            <Button leftSection={<TbPlus size={16} />} onClick={() => setNewCardOpen(true)}>
              New Card
            </Button>
          )}
        </Group>
      </Group>

      {(boardsLoading || boardLoading) && (
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
                <BoardColumn key={column.id} column={column} onOpenCard={setSelectedCardId} />
              ))}
            </Group>
          </ScrollArea>
        </DndContext>
      )}
    </Box>
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
        onSuccess: (board) => {
          onClose()
          setName('')
          setDescription('')
          navigate(`/boards/${board.id}`)
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
