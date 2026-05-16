import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPatch, apiPost, apiPut } from './client'

export type TrackerKind = 'Internal' | 'Linear' | 'GitHubIssues' | 'Jira'
export type CardStatus = 'Backlog' | 'InProgress' | 'Review' | 'Done' | 'Blocked' | 'Canceled'
export type AgentKind = 'Raw' | 'ClaudeCode'
export type SessionStatus = 'Created' | 'Starting' | 'Running' | 'Stopping' | 'Stopped' | 'Failed'

export interface BoardSummaryDto {
  id: string
  projectId: string
  projectName: string
  name: string
  description: string
  trackerKind: TrackerKind
  maxConcurrentSessions: number
  cardCount: number
  createdAt: string
  updatedAt: string
}

export interface BoardDetailDto {
  id: string
  projectId: string
  projectName: string
  name: string
  description: string
  trackerKind: TrackerKind
  maxConcurrentSessions: number
  columns: BoardColumnDto[]
  createdAt: string
  updatedAt: string
}

export interface BoardColumnDto {
  id: string
  stateKey: string
  name: string
  columnOrder: number
  cardStatus: CardStatus
  isActive: boolean
  isTerminal: boolean
  maxConcurrentSessions: number | null
  cards: CardDto[]
}

export interface CardDto {
  id: string
  boardId: string
  boardColumnId: string
  ownerSessionId: string | null
  currentWorktreeId: string | null
  identifier: string
  title: string
  description: string
  priority: number
  labels: string[]
  status: CardStatus
  concurrencyToken: string
  createdAt: string
  updatedAt: string
  startedAt: string | null
  completedAt: string | null
  terminalReason: string | null
  sessions: AgentSessionSummaryDto[]
}

export interface AgentSessionSummaryDto {
  id: string
  definitionName: string
  agentKind: AgentKind
  status: SessionStatus
  cwd: string
  createdAt: string
  startedAt: string
  lastSeenAt: string
  endedAt: string | null
  exitCode: number | null
  failureReason: string | null
}

export interface CreateBoardRequest {
  projectId: string
  name: string
  description?: string | null
  maxConcurrentSessions?: number
}

export interface CreateCardRequest {
  boardColumnId?: string | null
  title: string
  description?: string | null
  priority?: number
  labels?: string[]
}

export interface MoveCardRequest {
  boardColumnId: string
  concurrencyToken: string
}

export interface SpawnCardRequest {
  definitionName?: string | null
  cols?: number
  rows?: number
  prompt?: string | null
}

export interface SpawnCardResult {
  cardId: string
  sessionId: string
}

export interface BoardWorkflowDto {
  boardId: string
  definitionId: string | null
  version: number
  name: string
  content: string
  filePath: string | null
  updatedAt: string | null
}

export interface UpdateBoardWorkflowRequest {
  content: string
}

export const boardKeys = {
  all: ['boards'] as const,
  detail: (id: string) => ['boards', id] as const,
  workflow: (id: string) => ['boards', id, 'workflow'] as const,
}

export function useBoards() {
  return useQuery({
    queryKey: boardKeys.all,
    queryFn: () => apiGet<BoardSummaryDto[]>('/boards'),
  })
}

export function useBoard(id: string | undefined) {
  return useQuery({
    queryKey: id ? boardKeys.detail(id) : ['boards', 'missing'],
    queryFn: () => apiGet<BoardDetailDto>(`/boards/${id}`),
    enabled: !!id,
  })
}

export function useBoardWorkflow(id: string | undefined) {
  return useQuery({
    queryKey: id ? boardKeys.workflow(id) : ['boards', 'missing', 'workflow'],
    queryFn: () => apiGet<BoardWorkflowDto>(`/boards/${id}/workflow`),
    enabled: !!id,
  })
}

export function useCreateBoard() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateBoardRequest) => apiPost<BoardDetailDto>('/boards', request),
    onSuccess: (board) => {
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.setQueryData(boardKeys.detail(board.id), board)
    },
  })
}

export function useCreateCard(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateCardRequest) => apiPost<CardDto>(`/boards/${boardId}/cards`, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
    },
  })
}

export function useMoveCard(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: MoveCardRequest }) =>
      apiPatch<CardDto>(`/cards/${cardId}`, request),
    onMutate: async ({ cardId, request }) => {
      await queryClient.cancelQueries({ queryKey: boardKeys.detail(boardId) })
      const previous = queryClient.getQueryData<BoardDetailDto>(boardKeys.detail(boardId))
      if (previous) {
        queryClient.setQueryData(boardKeys.detail(boardId), moveCardOptimistically(previous, cardId, request.boardColumnId))
      }
      return { previous }
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(boardKeys.detail(boardId), context.previous)
      }
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
    },
  })
}

export function useSpawnCard(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: SpawnCardRequest }) =>
      apiPost<SpawnCardResult>(`/cards/${cardId}/spawn`, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
    },
  })
}

export function useUpdateBoardWorkflow(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateBoardWorkflowRequest) =>
      apiPut<BoardWorkflowDto>(`/boards/${boardId}/workflow`, request),
    onSuccess: (workflow) => {
      queryClient.setQueryData(boardKeys.workflow(boardId), workflow)
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
    },
  })
}

export function moveCardOptimistically(
  board: BoardDetailDto,
  cardId: string,
  targetColumnId: string,
): BoardDetailDto {
  let movingCard: CardDto | undefined
  const columnsWithoutCard = board.columns.map((column) => {
    const cards = column.cards.filter((card) => {
      if (card.id === cardId) {
        movingCard = card
        return false
      }
      return true
    })
    return { ...column, cards }
  })

  if (!movingCard) return board

  const columns = columnsWithoutCard.map((column) => {
    if (column.id !== targetColumnId || !movingCard) return column
    return {
      ...column,
      cards: [
        ...column.cards,
        {
          ...movingCard,
          boardColumnId: column.id,
          status: column.cardStatus,
        },
      ],
    }
  })

  return { ...board, columns }
}
