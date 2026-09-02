import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Chip,
  CloseButton,
  Group,
  Loader,
  Modal,
  MultiSelect,
  NumberInput,
  Paper,
  Popover,
  ScrollArea,
  Select,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbAlertCircle, TbFileCode, TbFilter, TbPlus, TbRefresh, TbSearch } from 'react-icons/tb'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import { useProjects } from '../../api/projects'
import {
  CARD_LIMITS,
  type BoardColumnDto,
  type BoardDetailDto,
  type CardImportance,
  type CardStatus,
  type CardUrgency,
  formatTrackerSyncSummary,
  useAllBoardDetails,
  useBoard,
  useBoards,
  useCreateBoard,
  useCreateCard,
  useSyncTracker,
} from '../../api/boards'
import { getApiErrorMessage, getApiFieldErrors } from '../../api/client'
import { BoardColumn } from './BoardColumn'
import { LimitCounter } from './LimitCounter'
import { CardListSection } from './CardListSection'
import { CardModal } from './CardModal'
import { ShapeStrip } from './ShapeStrip'
import { StatePager } from './StatePager'
import { WorkflowEditor } from './WorkflowEditor'
import {
  IMPORTANCES,
  type BoardFilter,
  buildBoardShape,
  defaultExpandedState,
  isFilterActive,
} from './boardShapeModel'

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
  { stateKey: 'needs-decision', name: 'Needs decision', cardStatus: 'NeedsDecision', isActive: false, isTerminal: false },
  { stateKey: 'canceled', name: 'Canceled', cardStatus: 'Canceled', isActive: false, isTerminal: true },
]

export function BoardPage() {
  const { id } = useParams() as BoardRouteParams
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { data: boards, isLoading: boardsLoading } = useBoards()
  // Archived cards are opt-in per board and the opt-in lives in the URL, so a link to an archived
  // card's board carries the toggle with it. The chip that sets it lives in `BoardShapeView`,
  // which reads the same search params.
  const showArchived = searchParams.get('archived') === '1'
  const { data: board, isLoading: boardLoading, error } = useBoard(id, {
    includeArchived: showArchived,
    view: 'summary',
  })
  const boardIds = useMemo(() => (boards ?? []).map((item) => item.id), [boards])
  const allBoards = useAllBoardDetails(boardIds, !id && !boardsLoading)
  const selectedBoardLoading = !!id && boardLoading
  const selectedCardId = searchParams.get('card')
  const [newBoardOpen, setNewBoardOpen] = useState(false)
  const [newCardOpen, setNewCardOpen] = useState(false)
  const [workflowOpen, setWorkflowOpen] = useState(false)
  const isMobile = useMediaQuery('(max-width: 48em)') ?? false

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
            columns={board.columns}
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
          columns={[]}
          opened
          onClose={closeCard}
        />
      )}

      {/*
        On a phone the chrome has to earn its height: the pager needs the screen, so the title
        block, the board picker and the actions collapse into one row of icons rather than three
        rows of labelled buttons.
      */}
      <Group justify="space-between" mb="md" align="flex-end" wrap="nowrap">
        {!isMobile && (
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
        )}
        <Group gap="xs" wrap="nowrap" style={{ flex: isMobile ? 1 : undefined }}>
          <Select
            placeholder="Select board"
            data={boardOptions}
            value={id ?? ALL_BOARDS_VALUE}
            onChange={handleBoardSelection}
            w={isMobile ? undefined : 320}
            style={isMobile ? { flex: 1, minWidth: 0 } : undefined}
            searchable
            maxDropdownHeight={420}
          />
          {isMobile
            ? (
              <>
                <ActionIcon
                  variant="light"
                  size="lg"
                  aria-label="New Board"
                  onClick={() => setNewBoardOpen(true)}
                >
                  <TbPlus size={18} />
                </ActionIcon>
                {board && (
                  <>
                    <ActionIcon
                      variant="light"
                      size="lg"
                      aria-label="Workflow"
                      onClick={() => setWorkflowOpen(true)}
                    >
                      <TbFileCode size={18} />
                    </ActionIcon>
                    {board.trackerKind !== 'Internal' && (
                      <SyncTrackerButton boardId={board.id} compact />
                    )}
                    <ActionIcon size="lg" aria-label="New Card" onClick={() => setNewCardOpen(true)}>
                      <TbPlus size={18} />
                    </ActionIcon>
                  </>
                )}
              </>
            )
            : (
              <>
                <Button leftSection={<TbPlus size={16} />} variant="light" onClick={() => setNewBoardOpen(true)}>
                  New Board
                </Button>
                {board && (
                  <>
                    <Button leftSection={<TbFileCode size={16} />} variant="light" onClick={() => setWorkflowOpen(true)}>
                      Workflow
                    </Button>
                    {board.trackerKind !== 'Internal' && (
                      <SyncTrackerButton boardId={board.id} />
                    )}
                    <Button leftSection={<TbPlus size={16} />} onClick={() => setNewCardOpen(true)}>
                      New Card
                    </Button>
                  </>
                )}
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

      {board && <BoardShapeView board={board} isMobile={isMobile} onOpenCard={openCard} />}

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

/** CARD-0166 S7: on-demand sync for boards with TrackerKind != Internal. */
function SyncTrackerButton({ boardId, compact = false }: { boardId: string; compact?: boolean }) {
  const sync = useSyncTracker(boardId)

  const run = () => {
    sync.mutate(undefined, {
      onSuccess: (result) => {
        notifications.show({
          color: 'green',
          title: 'Tracker sync',
          message: formatTrackerSyncSummary(result),
        })
      },
      onError: (error) => {
        notifications.show({
          color: 'red',
          title: 'Tracker sync failed',
          message: getApiErrorMessage(error, 'Tracker sync failed'),
        })
      },
    })
  }

  if (compact) {
    return (
      <ActionIcon
        variant="light"
        size="lg"
        aria-label="Sync tracker now"
        loading={sync.isPending}
        onClick={run}
      >
        <TbRefresh size={18} />
      </ActionIcon>
    )
  }

  return (
    <Button
      leftSection={<TbRefresh size={16} />}
      variant="light"
      loading={sync.isPending}
      onClick={run}
    >
      Sync tracker now
    </Button>
  )
}

/**
 * The single-board surface: a graph of the board's states with the card list beneath it on
 * desktop, one state at a time on a phone. Everything below is derived client-side from the
 * `BoardDetailDto` the page already holds — no server projection exists or is needed at this
 * scale (feature 011 §5).
 */
function BoardShapeView({
  board,
  isMobile,
  onOpenCard,
}: {
  board: BoardDetailDto
  isMobile: boolean
  onOpenCard: (cardId: string) => void
}) {
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState(() => searchParams.get('q') ?? '')
  const [toggledStates, setToggledStates] = useState<Record<string, boolean>>({})
  const [expandedBands, setExpandedBands] = useState<ReadonlySet<string>>(() => new Set<string>())

  const selectedState = searchParams.get('state')
  const showArchived = searchParams.get('archived') === '1'
  const importances = useMemo(
    () => (searchParams.get('imp') ?? '')
      .split(',')
      .map((value) => value.trim())
      .filter((value): value is typeof IMPORTANCES[number] =>
        (IMPORTANCES as readonly string[]).includes(value)),
    [searchParams],
  )
  const urgentOnly = searchParams.get('urgent') === '1'
  const labels = useMemo(
    () => (searchParams.get('labels') ?? '').split(',').map((value) => value.trim()).filter(Boolean),
    [searchParams],
  )

  const filter: BoardFilter = useMemo(
    () => ({ query, state: selectedState, importances, urgentOnly, labels }),
    [query, selectedState, importances, urgentOnly, labels],
  )
  // One clock for the whole render, so every age on screen is measured from the same instant.
  // Deriving the shape per render is deliberate and cheap: it is a single pass over the board's
  // cards (45 today; the deferred server projection in §5 is what a thousand-card board needs).
  const now = new Date()
  const shape = buildBoardShape(board, filter, now)
  const filtered = isFilterActive(filter)
  const defaultKey = defaultExpandedState(shape.states)

  const setParam = (key: string, value: string | null) => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current)
      if (value) next.set(key, value)
      else next.delete(key)
      return next
    }, { replace: true })
  }

  const toggleState = (stateKey: string) => {
    setToggledStates((current) => ({
      ...current,
      [stateKey]: !(current[stateKey] ?? stateKey === defaultKey),
    }))
  }

  const toggleBand = (bandKey: string) => {
    setExpandedBands((current) => {
      const next = new Set(current)
      if (next.has(bandKey)) next.delete(bandKey)
      else next.add(bandKey)
      return next
    })
  }

  const pagerState = selectedState && shape.states.some((state) => state.stateKey === selectedState)
    ? selectedState
    : defaultKey ?? shape.states[0]?.stateKey ?? ''

  const clearFilters = () => {
    setQuery('')
    setSearchParams((current) => {
      const next = new URLSearchParams(current)
      for (const key of ['q', 'imp', 'urgent', 'labels', 'state', 'archived']) next.delete(key)
      return next
    }, { replace: true })
  }

  const importanceChips = (
    <Group gap={4}>
      <Chip.Group
        multiple
        value={importances}
        onChange={(value) => setParam('imp', value.length ? [...value].join(',') : null)}
      >
        <Group gap={4}>
          {IMPORTANCES.map((importance) => (
            <Chip key={importance} value={importance} size="xs" variant="outline">
              {importance}
            </Chip>
          ))}
        </Group>
      </Chip.Group>
      <Chip
        size="xs"
        variant="outline"
        checked={urgentOnly}
        onChange={() => setParam('urgent', urgentOnly ? null : '1')}
      >
        Urgent
      </Chip>
    </Group>
  )

  /**
   * Archived cards are absent from the board payload unless asked for, so this is a REFETCH under
   * a different cache key, not a client-side filter. It stays a per-board opt-in: the all-boards
   * aggregate is a workload view, not an archaeology view.
   */
  const archivedChip = (
    <Chip
      size="xs"
      variant="outline"
      checked={showArchived}
      onChange={(checked) => setParam('archived', checked ? '1' : null)}
    >
      Archived
    </Chip>
  )

  const labelSelect = (
    <MultiSelect
      placeholder={labels.length ? undefined : 'Labels'}
      aria-label="Filter by label"
      data={shape.labels}
      value={labels}
      onChange={(value) => setParam('labels', value.length ? value.join(',') : null)}
      searchable
      clearable
      w={isMobile ? '100%' : 240}
      maxDropdownHeight={280}
    />
  )

  const filterControls = (
    <Group gap="xs" wrap={isMobile ? 'nowrap' : 'wrap'} mb="xs">
      <TextInput
        placeholder="Search cards…"
        aria-label="Search cards"
        leftSection={<TbSearch size={14} />}
        value={query}
        w={isMobile ? undefined : 260}
        style={isMobile ? { flex: 1, minWidth: 0 } : undefined}
        onChange={(event) => {
          setQuery(event.currentTarget.value)
          setParam('q', event.currentTarget.value || null)
        }}
        rightSection={query
          ? <CloseButton size="sm" aria-label="Clear search" onClick={() => { setQuery(''); setParam('q', null) }} />
          : null}
      />
      {/* On a phone the priority and label controls fold into a popover so the pager keeps the screen. */}
      {isMobile
        ? (
          <Popover position="bottom-end" withinPortal shadow="md">
            <Popover.Target>
              <ActionIcon variant="light" size="lg" aria-label="Filters">
                <TbFilter size={18} />
              </ActionIcon>
            </Popover.Target>
            <Popover.Dropdown>
              <Stack gap="xs" w={260}>
                {importanceChips}
                {labelSelect}
                {archivedChip}
                {(filtered || selectedState) && (
                  <Button size="xs" variant="subtle" onClick={clearFilters}>Clear filters</Button>
                )}
                <Text size="xs" c="dimmed">
                  {filtered ? `${shape.filteredCount} of ${shape.totalCount} cards` : `${shape.totalCount} cards`}
                </Text>
              </Stack>
            </Popover.Dropdown>
          </Popover>
        )
        : (
          <>
            {importanceChips}
            {labelSelect}
            {archivedChip}
            {(filtered || selectedState) && (
              <Button size="xs" variant="subtle" onClick={clearFilters}>Clear filters</Button>
            )}
            <Text size="xs" c="dimmed">
              {filtered ? `${shape.filteredCount} of ${shape.totalCount} cards` : `${shape.totalCount} cards`}
            </Text>
          </>
        )}
    </Group>
  )

  const emptyResult = shape.filteredCount === 0 && filtered && (
    <Paper withBorder p="lg" mt="sm">
      <Stack gap="xs" align="center">
        <Text c="dimmed">No cards match the current filter.</Text>
        <Button size="xs" variant="light" onClick={clearFilters}>Clear filters</Button>
      </Stack>
    </Paper>
  )

  if (isMobile) {
    return (
      <Box>
        {filterControls}
        <StatePager
          states={shape.states}
          stateKey={pagerState}
          boardId={board.id}
          columns={board.columns}
          now={now}
          filtered={filtered}
          onSelectState={(stateKey) => setParam('state', stateKey)}
          onOpenCard={onOpenCard}
          expandedBands={expandedBands}
          onToggleBand={toggleBand}
        />
      </Box>
    )
  }

  const selected = shape.states.find((state) => state.stateKey === selectedState) ?? null

  return (
    <Box>
      {filterControls}
      <ShapeStrip
        states={shape.states}
        selected={selectedState}
        filtered={filtered}
        onSelect={(stateKey) => setParam('state', stateKey === selectedState ? null : stateKey)}
      />

      {selected
        ? (
          <Box>
            <Group gap="xs" mb={4}>
              <Text size="sm" fw={700} tt="uppercase">{selected.name}</Text>
              <Badge size="sm" variant="outline" color="gray">
                {filtered ? `${selected.filteredCount} of ${selected.totalCount}` : selected.filteredCount}
              </Badge>
              <Button
                size="compact-xs"
                variant="subtle"
                onClick={() => setParam('state', null)}
              >
                clear state filter
              </Button>
            </Group>
            <CardListSection
              state={selected}
              boardId={board.id}
              columns={board.columns}
              now={now}
              filtered={filtered}
              collapsible={false}
              expanded
              onToggle={toggleState}
              onOpenCard={onOpenCard}
              expandedBands={expandedBands}
              onToggleBand={toggleBand}
            />
          </Box>
        )
        : (
          <Stack gap={0}>
            {shape.states.map((state) => (
              <CardListSection
                key={state.columnId}
                state={state}
                boardId={board.id}
                columns={board.columns}
                now={now}
                filtered={filtered}
                collapsible
                expanded={toggledStates[state.stateKey] ?? state.stateKey === defaultKey}
                onToggle={toggleState}
                onOpenCard={onOpenCard}
                expandedBands={expandedBands}
                onToggleBand={toggleBand}
              />
            ))}
          </Stack>
        )}

      {emptyResult}
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
    <ScrollArea type="auto" offsetScrollbars>
      <Group align="flex-start" wrap="nowrap" gap="md" pb="sm">
        {columns.map((column) => (
          <BoardColumn key={column.id} column={column} onOpenCard={onOpenCard} />
        ))}
      </Group>
    </ScrollArea>
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
  const [importance, setImportance] = useState<CardImportance>('Normal')
  const [urgency, setUrgency] = useState<CardUrgency>('Normal')
  const [dueAt, setDueAt] = useState('')
  const [labels, setLabels] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  // Same limits as the edit dialog, checked the same way: the counter is the UX and the server's
  // 422 is the backstop, whose message is shown verbatim on the input that caused it.
  const titleOverLimit = title.length > CARD_LIMITS.title
  const descriptionOverLimit = description.length > CARD_LIMITS.description
  const canSubmit = !!title.trim() && !titleOverLimit && !descriptionOverLimit

  const handleSubmit = () => {
    if (!canSubmit) return
    setFieldErrors({})
    createCard.mutate(
      {
        title,
        description,
        importance,
        urgency,
        dueAt: dueAt || null,
        labels: labels.split(',').map((label) => label.trim()).filter(Boolean),
      },
      {
        onSuccess: () => {
          setTitle('')
          setDescription('')
          setImportance('Normal')
          setUrgency('Normal')
          setDueAt('')
          setLabels('')
          onClose()
        },
        onError: (error) => {
          const fields = getApiFieldErrors(error)
          setFieldErrors(fields)
          if (Object.keys(fields).length === 0) {
            notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Create failed') })
          }
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={onClose} title="New Card">
      <Stack>
        <TextInput
          label="Title"
          value={title}
          error={fieldErrors.Title
            ?? (titleOverLimit
              ? `Title must be at most ${CARD_LIMITS.title.toLocaleString()} characters.`
              : undefined)}
          onChange={(event) => setTitle(event.currentTarget.value)}
        />
        <Textarea
          label="Description"
          value={description}
          autosize
          minRows={3}
          maxRows={12}
          inputWrapperOrder={['label', 'input', 'description', 'error']}
          description={<LimitCounter value={description.length} limit={CARD_LIMITS.description} />}
          error={fieldErrors.Description}
          onChange={(event) => setDescription(event.currentTarget.value)}
        />
        <Select
          label="Importance"
          data={['Low', 'Normal', 'High', 'Critical']}
          value={importance}
          onChange={(value) => setImportance((value as CardImportance) ?? 'Normal')}
        />
        <Select
          label="Urgency"
          data={['Normal', 'Soon', 'Now']}
          value={urgency}
          onChange={(value) => setUrgency((value as CardUrgency) ?? 'Normal')}
        />
        <TextInput
          label="Due"
          type="date"
          value={dueAt}
          onChange={(event) => setDueAt(event.currentTarget.value)}
        />
        <TextInput label="Labels" value={labels} onChange={(event) => setLabels(event.currentTarget.value)} />
        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button onClick={handleSubmit} loading={createCard.isPending} disabled={!canSubmit}>
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
