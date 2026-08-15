import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiDelete, apiGet, apiPatch, apiPost, apiPut } from './client'

export type TrackerKind = 'Internal' | 'Linear' | 'GitHubIssues' | 'Jira'
export type CardStatus = 'Backlog' | 'InProgress' | 'Review' | 'Done' | 'Blocked' | 'Canceled'
export type AgentKind = 'Raw' | 'ClaudeCode' | 'Codex'
export type SessionStatus = 'Created' | 'Starting' | 'Running' | 'Stopping' | 'Stopped' | 'Failed'
export type CardWorkflowRunStatus = 'Queued' | 'Running' | 'WaitingForHumanReview' | 'Completed' | 'Failed' | 'Canceled'

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
  assignedAgentId: string | null
  assignedAgentName: string | null
  agentQueuePosition: number | null
  activeWorkflowRunId: string | null
  workflowRunStatus: CardWorkflowRunStatus | null
  currentWorkflowStageName: string | null
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
  /**
   * How many history entries this card has. Counts **moves** as well as content edits and
   * archives, so it is non-zero on almost every card and is NOT an "edited" marker — it is the
   * History tab's count and nothing else.
   */
  revisionCount: number
  archivedAt: string | null
  archivedReason: string | null
  archivedBy: string | null
}

/**
 * What a history entry records. Lockstep with `server/Domain/Enums/CardRevisionKind.cs`; the API
 * serializes enums as strings.
 */
export type CardRevisionKind = 'ContentEdit' | 'Move' | 'Archive' | 'Unarchive'

/**
 * One entry of a card's immutable history. `revisionNumber` is a single monotonic sequence across
 * ALL kinds, so the four kinds interleave into one timeline; the server serves it newest first.
 *
 * Which fields are populated depends on `kind`: a `ContentEdit` carries the values it SUPERSEDED
 * (entry n plus the current card is the whole history), a `Move` carries the transition and no
 * text, `Archive`/`Unarchive` carry only their reason.
 */
export interface CardRevisionDto {
  id: string
  cardId: string
  revisionNumber: number
  kind: CardRevisionKind
  title: string | null
  description: string | null
  priority: number | null
  labels: string[] | null
  fromColumnId: string | null
  toColumnId: string | null
  fromStatus: CardStatus | null
  toStatus: CardStatus | null
  reason: string | null
  editedBy: string | null
  createdAt: string
}

/**
 * LOCKSTEP PAIR with `CardService.MaxTitleLength` / `MaxDescriptionLength` / `MaxReasonLength`.
 * No endpoint serves these and adding one is not worth it for three integers — the counters here
 * are the UX and the server's 422 is the backstop, whose message the UI shows verbatim precisely
 * so that drift is visible rather than silent.
 */
export const CARD_LIMITS = {
  title: 300,
  description: 20_000,
  reason: 4_000,
} as const

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
  /**
   * Why the card is moving. It PERSISTS on every move, as the reason on the card's `Move`
   * revision; a move into a terminal column additionally stamps `TerminalReason`, the
   * cheap-to-read summary.
   */
  reason?: string | null
}

/**
 * A correction to a card's text — deliberately not an overload of the move PATCH.
 *
 * `null`/omitted means UNCHANGED for every content field, so send only what actually changed.
 * `reason` is required: a correction that does not say why is how a record silently rots.
 * `editedBy` is self-reported free text (the server has no principals) — the web UI sends
 * `"operator"`.
 */
export interface UpdateCardContentRequest {
  concurrencyToken: string
  reason: string
  title?: string | null
  description?: string | null
  priority?: number | null
  labels?: string[] | null
  editedBy?: string | null
}

/** Archive is what "delete" means for a card: the row stays, so no identifier ever dangles. */
export interface ArchiveCardRequest {
  concurrencyToken: string
  reason: string
  archivedBy?: string | null
}

export interface UnarchiveCardRequest {
  concurrencyToken: string
  reason: string
  unarchivedBy?: string | null
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

export interface CardDiffFileDto {
  filename: string
  additions: number
  deletions: number
  patch: string
}

export interface CardDiffDto {
  baseBranch: string
  headBranch: string
  files: CardDiffFileDto[]
  prNumber?: number | null
  prUrl?: string | null
  prTitle?: string | null
  prState?: string | null
}

export interface CardCommentRequest {
  message: string
  filePath?: string | null
  line?: number | null
  side?: 'old' | 'new' | 'context' | null
  endLine?: number | null
}

export interface CardCommentResult {
  cardId: string
  sessionId: string
  formattedMessage: string
}

export interface CardPullRequestResult {
  cardId: string
  prNumber: number
  owner: string
  repo: string
  branch: string
  baseBranch: string
  prUrl: string | null
  prState: string | null
  created: boolean
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
  /**
   * The archived-inclusive board is a SIBLING of `detail`, not a variant of it: the two payloads
   * differ and must not share a cache entry. Nesting it under `['boards', id]` is what keeps every
   * existing `invalidateQueries({ queryKey: boardKeys.detail(id) })` covering it too — prefix
   * matching means no mutation's invalidation list has to learn about archived cards.
   */
  detailArchived: (id: string) => ['boards', id, 'archived'] as const,
  allDetails: ['boards', 'all-details'] as const,
  allDetailsFor: (ids: string[]) => [...boardKeys.allDetails, ids] as const,
  workflow: (id: string) => ['boards', id, 'workflow'] as const,
  cardDiff: (cardId: string) => ['cards', cardId, 'diff'] as const,
  cardRevisions: (cardId: string) => ['cards', cardId, 'revisions'] as const,
}

export function useBoards() {
  return useQuery({
    queryKey: boardKeys.all,
    queryFn: () => apiGet<BoardSummaryDto[]>('/boards'),
  })
}

export function useBoard(id: string | undefined, options: { includeArchived?: boolean } = {}) {
  const includeArchived = options.includeArchived ?? false
  return useQuery({
    queryKey: id
      ? (includeArchived ? boardKeys.detailArchived(id) : boardKeys.detail(id))
      : ['boards', 'missing'],
    queryFn: () =>
      apiGet<BoardDetailDto>(`/boards/${id}${includeArchived ? '?includeArchived=true' : ''}`),
    enabled: !!id,
  })
}

export function useAllBoardDetails(boardIds: string[], enabled = true) {
  return useQuery({
    queryKey: boardKeys.allDetailsFor(boardIds),
    queryFn: () => Promise.all(boardIds.map((boardId) => apiGet<BoardDetailDto>(`/boards/${boardId}`))),
    enabled: enabled && boardIds.length > 0,
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

export interface DeleteBoardResultDto {
  boardId: string
  projectId: string
  /** True when this was the project's last board and the empty project went with it. */
  projectDeleted: boolean
}

export function useDeleteBoard() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (boardId: string) => apiDelete<DeleteBoardResultDto>(`/boards/${boardId}`),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.removeQueries({ queryKey: boardKeys.detail(result.boardId) })
      // The board may have taken its project with it.
      queryClient.invalidateQueries({ queryKey: ['projects'] })
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
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
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
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

/**
 * Every card write that produces a revision invalidates the same set: the board detail (whose
 * prefix also covers the archived-inclusive sibling), the board list, the all-boards aggregate and
 * — the one `useMoveCard` never needed — that card's revision list.
 */
function invalidateAfterCardWrite(queryClient: ReturnType<typeof useQueryClient>, boardId: string, cardId: string) {
  queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
  queryClient.invalidateQueries({ queryKey: boardKeys.all })
  queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
  queryClient.invalidateQueries({ queryKey: boardKeys.cardRevisions(cardId) })
}

export function useUpdateCardContent(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: UpdateCardContentRequest }) =>
      apiPatch<CardDto>(`/cards/${cardId}/content`, request),
    onSuccess: (_card, { cardId }) => invalidateAfterCardWrite(queryClient, boardId, cardId),
  })
}

// POST, not DELETE: archive is not a delete — the row stays so references to the identifier never
// dangle, and the allocator never hands the number out again.
export function useArchiveCard(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: ArchiveCardRequest }) =>
      apiPost<CardDto>(`/cards/${cardId}/archive`, request),
    onSuccess: (_card, { cardId }) => invalidateAfterCardWrite(queryClient, boardId, cardId),
  })
}

export function useUnarchiveCard(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: UnarchiveCardRequest }) =>
      apiPost<CardDto>(`/cards/${cardId}/unarchive`, request),
    onSuccess: (_card, { cardId }) => invalidateAfterCardWrite(queryClient, boardId, cardId),
  })
}

/** A card's history, newest first. `enabled` is how the History tab pays for it only once opened. */
export function useCardRevisions(cardId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: cardId ? boardKeys.cardRevisions(cardId) : ['cards', 'missing', 'revisions'],
    queryFn: () => apiGet<CardRevisionDto[]>(`/cards/${cardId}/revisions`),
    enabled: !!cardId && enabled,
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
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useCardDiff(cardId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: cardId ? boardKeys.cardDiff(cardId) : ['cards', 'missing', 'diff'],
    queryFn: () => apiGet<CardDiffDto>(`/cards/${cardId}/diff`),
    enabled: !!cardId && enabled,
    retry: 1,
    staleTime: 30_000,
  })
}

export function usePostCardComment(boardId: string, cardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CardCommentRequest) =>
      apiPost<CardCommentResult>(`/cards/${cardId}/comments`, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function useOpenCardPullRequest(boardId: string, cardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<CardPullRequestResult>(`/cards/${cardId}/pr`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.cardDiff(cardId) })
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
