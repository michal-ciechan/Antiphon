import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query'
import type { CardImportance, CardQuadrant, CardUrgency } from '../features/board/cardRanking'
import { apiDelete, apiGet, apiPatch, apiPost, apiPut } from './client'

export type { CardImportance, CardQuadrant, CardUrgency }

export type TrackerKind = 'Internal' | 'Linear' | 'GitHubIssues' | 'Jira'
export type CardStatus = 'Backlog' | 'InProgress' | 'Review' | 'Done' | 'NeedsDecision' | 'Canceled'
export type AgentKind = 'Raw' | 'ClaudeCode' | 'Codex' | 'OpenCode' | 'Grok'
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
  importance: CardImportance
  /** Auto until an explicit create/edit sets importance; older payloads omit it. */
  importanceProvenance?: 'Auto' | 'Human'
  urgency: CardUrgency
  dueAt: string | null
  urgentSince: string | null
  effectiveUrgency: CardUrgency
  quadrant: CardQuadrant
  rank: number
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
  /**
   * Set when the card sits in an active column with spawn declined; auto-dispatch skips it.
   * Cleared by POST /spawn or by a move off the active column. Omitted/null on cards the tick
   * may pick up.
   */
  autoDispatchHeldAt?: string | null
  externalIssue?: ExternalIssueDto | null
  /** True only when the summary representation cut card text. Full-card responses omit it. */
  hasMore?: boolean
}

export interface CardListDto {
  cards: CardDto[]
  truncated: boolean
}

export interface CardListFilters {
  updatedSince?: string
  status?: CardStatus
  boardId?: string
}

export interface ExternalIssueDto {
  trackerKind: TrackerKind
  key: string
  url: string
  /** Tracker login of the issue author. Null when the adapter did not capture one. */
  author?: string | null
  /** Null = the board has no operator_logins, so the author was never judged. */
  authorIsOperator?: boolean | null
  /** Derived: import-origin, non-operator, unrated, still in Backlog. */
  needsHumanReview?: boolean
}

/**
 * What a history entry records. Lockstep with `server/Domain/Enums/CardRevisionKind.cs`; the API
 * serializes enums as strings.
 */
export type CardRevisionKind = 'ContentEdit' | 'Move' | 'Archive' | 'Unarchive' | 'Reopen'

/**
 * One entry of a card's immutable history. `revisionNumber` is a single monotonic sequence across
 * ALL kinds, so the five kinds interleave into one timeline; the server serves it newest first.
 *
 * Which fields are populated depends on `kind`: a `ContentEdit` carries the values it SUPERSEDED
 * (entry n plus the current card is the whole history), a `Move` carries the transition and no
 * text, a `Reopen` carries the transition AND the superseded `terminalReason`/`completedAt`
 * (those fields exist on every row and are null except on Reopen), `Archive`/`Unarchive` carry
 * only their reason.
 */
export interface CardRevisionDto {
  id: string
  cardId: string
  revisionNumber: number
  kind: CardRevisionKind
  title: string | null
  description: string | null
  importance: CardImportance | null
  urgency: CardUrgency | null
  dueAt: string | null
  labels: string[] | null
  fromColumnId: string | null
  toColumnId: string | null
  fromStatus: CardStatus | null
  toStatus: CardStatus | null
  reason: string | null
  editedBy: string | null
  createdAt: string
  /** Populated only on Reopen rows — the terminal reason the close had stamped. */
  terminalReason: string | null
  /** Populated only on Reopen rows — when the superseded close happened. */
  completedAt: string | null
}

/**
 * LOCKSTEP PAIR with `CardService.MaxTitleLength` / `MaxDescriptionLength` / `MaxReasonLength`.
 * `GET /api/cards/limits` now serves the same constants (CARD-0051) for callers that cannot
 * hard-code them — scripts composing a correction from a file. The UI keeps its literals: a
 * character counter that has to wait for a request is worse than one that cannot drift, and the
 * server's 422 is still the backstop, whose message the UI shows verbatim precisely so that any
 * drift is visible rather than silent.
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
  /**
   * Newest-usage-row fullness (tokens / ceiling), 0.0–1.0+. Null/omitted = unknown: no usage
   * yet, or a CompactBoundary / /clear landed after the last usage-bearing row (CARD-0082).
   */
  contextFullness?: number | null
  /**
   * Why contextFullness is a number or null (CARD-0178). Additive; an older server omits it
   * and the badge falls back to "unknown".
   */
  contextFullnessState?: ContextFullnessState | null
  /**
   * CARD-0180 S4 / CARD-0190: runner transcript bind. `"awaiting-input"` is a neutral
   * first-prompt wait; omitted/null = unknown (older server / unreachable).
   */
  transcriptBinding?: 'bound' | 'unbound' | 'awaiting-input' | null
  herdrAgentStatus?: HerdrAgentStatus | null
  herdrAgentStatusSinceUtc?: string | null
  /** CARD-0213: `launched` | `attached`. Null for pty / older servers. */
  herdrOrigin?: 'launched' | 'attached' | null
  /**
   * CARD-0316: who ended the session. Enums serialise as strings. Omitted/null on older
   * servers; `Unknown` on a row closed after this card ships is a bug to file.
   */
  terminationSource?: SessionTerminationSource | null
}

export type SessionTerminationSource =
  | 'Unknown'
  | 'OperatorRequest'
  | 'SystemRequest'
  | 'ProcessExit'

export type HerdrAgentStatus = 'idle' | 'working' | 'blocked' | 'done' | 'unknown'

export type ContextFullnessState =
  | 'Known'
  | 'NoUsageYet'
  | 'Compacted'
  | 'Cleared'
  | 'Suppressed'

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
  /** Null/omitted → Normal with provenance Auto; an explicit value is Human. */
  importance?: CardImportance | null
  urgency?: CardUrgency
  dueAt?: string | null
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
  /**
   * Whether a move into an ACTIVE column may start an agent session. Omitted means **false** —
   * the server no longer spawns unasked, because a scripted move that only meant to file a card
   * would start work and say nothing. The UI sends `true`: its move dialog asks first and warns
   * that the target column spawns an agent, so the human has already opted in.
   */
  spawn?: boolean
}

/**
 * What a move DID. `spawnedSessionId` is the session it started (previously computed and thrown
 * away); `spawnSuppressed` is true when the target column was active and unowned and `spawn` was
 * not set — the card moved into a column where work happens and no work started.
 */
export type TrackerCardStatePushOutcome = 'Closed' | 'Reopened' | 'InSync' | 'Skipped' | 'Failed'

export interface TrackerCardStatePush {
  outcome: TrackerCardStatePushOutcome
  trackerKind: TrackerKind
  externalKey: string
  url: string
  reason?: string | null
}

export interface MoveCardResult {
  card: CardDto
  spawnedSessionId: string | null
  spawnSuppressed: boolean
  trackerPush?: TrackerCardStatePush | null
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
  importance?: CardImportance | null
  urgency?: CardUrgency | null
  dueAt?: string | null
  clearDueAt?: boolean
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

/**
 * Undo a terminal close. Dedicated verb, not a move: Done/Canceled stay unreachable via
 * PATCH /cards/{id}. Reopen never spawns — want an agent afterwards, POST /spawn.
 */
export interface ReopenCardRequest {
  concurrencyToken: string
  reason: string
  boardColumnId?: string | null
  reopenedBy?: string | null
}

export interface ReopenCardResult {
  card: CardDto
  trackerPush?: TrackerCardStatePush | null
}

export function reopenCard(cardId: string, body: ReopenCardRequest) {
  return apiPost<ReopenCardResult>(`/cards/${cardId}/reopen`, body)
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

/** CARD-0166: stored discussion thread (not session-inject /comments). */
export interface CardDiscussionCommentDto {
  id: string
  cardId: string
  body: string
  author: string | null
  origin: 'Antiphon' | 'External'
  externalCommentId: string | null
  externalUrl: string | null
  createdAt: string
  syncedAt: string | null
}

export interface CreateCardDiscussionRequest {
  body: string
  author?: string | null
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

/** CARD-0166 S7: summary returned by POST /boards/{id}/tracker/sync and /tracker-sync/run. */
export interface TrackerSyncBoardResultDto {
  boardId: string
  boardName: string
  issuesPulled: number
  commentsIn: number
  commentsOut: number
  labelsChanged: number
  stateChanges: number
  creates: number
  skips: string[]
  error?: string | null
}

export interface TrackerSyncRunResultDto {
  boards: TrackerSyncBoardResultDto[]
  concurrentRunSkipped?: boolean
}

export const boardKeys = {
  all: ['boards'] as const,
  detail: (id: string) => ['boards', id] as const,
  detailSummary: (id: string) => ['boards', id, 'summary'] as const,
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
  columns: (id: string) => ['boards', id, 'columns'] as const,
  card: (cardId: string) => ['cards', 'detail', cardId] as const,
  cards: ['cards', 'list'] as const,
  cardsFor: (filters: CardListFilters) => ['cards', 'list', filters] as const,
  cardDiff: (cardId: string) => ['cards', cardId, 'diff'] as const,
  cardRevisions: (cardId: string) => ['cards', cardId, 'revisions'] as const,
  cardDiscussion: (cardId: string) => ['cards', cardId, 'discussion'] as const,
}

export function useBoards() {
  return useQuery({
    queryKey: boardKeys.all,
    queryFn: () => apiGet<BoardSummaryDto[]>('/boards'),
  })
}

export function useBoard(
  id: string | undefined,
  options: { includeArchived?: boolean; view?: 'full' | 'summary' } = {},
) {
  const includeArchived = options.includeArchived ?? false
  const view = options.view ?? 'full'
  return useQuery({
    queryKey: id
      ? (includeArchived
        ? boardKeys.detailArchived(id)
        : view === 'summary' ? boardKeys.detailSummary(id) : boardKeys.detail(id))
      : ['boards', 'missing'],
    queryFn: () => {
      const query = new URLSearchParams()
      if (includeArchived) query.set('includeArchived', 'true')
      if (view === 'summary') query.set('view', 'summary')
      const suffix = query.size ? `?${query}` : ''
      return apiGet<BoardDetailDto>(`/boards/${id}${suffix}`)
    },
    enabled: !!id,
  })
}

export function useAllBoardDetails(boardIds: string[], enabled = true) {
  return useQuery({
    queryKey: boardKeys.allDetailsFor(boardIds),
    queryFn: () => Promise.all(boardIds.map((boardId) => apiGet<BoardDetailDto>(`/boards/${boardId}?view=summary`))),
    enabled: enabled && boardIds.length > 0,
  })
}

export function useBoardColumns(id: string | undefined) {
  return useQuery({
    queryKey: id ? boardKeys.columns(id) : ['boards', 'missing', 'columns'],
    queryFn: () => apiGet<BoardColumnDto[]>(`/boards/${id}/columns`),
    enabled: !!id,
  })
}

/** Decisions need move targets, not every card on each decision's board. */
export function useBoardColumnsFor(boardIds: string[], enabled = true) {
  return useQueries({
    queries: boardIds.map((boardId) => ({
      queryKey: boardKeys.columns(boardId),
      queryFn: () => apiGet<BoardColumnDto[]>(`/boards/${boardId}/columns`),
      enabled,
    })),
  })
}

export function useCards(filters: CardListFilters, enabled = true) {
  return useQuery({
    queryKey: boardKeys.cardsFor(filters),
    queryFn: () => {
      const query = new URLSearchParams()
      if (filters.updatedSince) query.set('updatedSince', filters.updatedSince)
      if (filters.status) query.set('status', filters.status)
      if (filters.boardId) query.set('boardId', filters.boardId)
      return apiGet<CardListDto>(`/cards?${query}`)
    },
    enabled,
  })
}

/** Full card cache; deliberately unrelated to any board-detail summary cache. */
export function useCard(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: id ? boardKeys.card(id) : ['cards', 'missing', 'detail'],
    queryFn: () => apiGet<CardDto>(`/cards/${id}`),
    enabled: enabled && !!id,
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
  // Keep the two board representations in sync, but do not prefix-match `['boards', id]` here:
  // that prefix also owns the columns-only query, whose array has no `columns` member to move.
  const detailKeys = [boardKeys.detail(boardId), boardKeys.detailSummary(boardId)] as const
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: MoveCardRequest }) =>
      apiPatch<MoveCardResult>(`/cards/${cardId}`, request),
    onMutate: async ({ cardId, request }) => {
      await Promise.all(detailKeys.map((queryKey) => queryClient.cancelQueries({ queryKey, exact: true })))
      const previous = detailKeys.map((queryKey) =>
        [queryKey, queryClient.getQueryData<BoardDetailDto>(queryKey)] as const)
      detailKeys.forEach((queryKey) => {
        queryClient.setQueryData<BoardDetailDto>(queryKey, (board) =>
          board ? moveCardOptimistically(board, cardId, request.boardColumnId) : board)
      })
      return { previous }
    },
    onError: (_error, _variables, context) => {
      context?.previous.forEach(([key, board]) => queryClient.setQueryData(key, board))
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
      queryClient.invalidateQueries({ queryKey: boardKeys.cards })
    },
    onSuccess: (_result, { cardId }) => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
      queryClient.invalidateQueries({ queryKey: boardKeys.cards })
      queryClient.invalidateQueries({ queryKey: boardKeys.card(cardId) })
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
  queryClient.invalidateQueries({ queryKey: boardKeys.cards })
  queryClient.invalidateQueries({ queryKey: boardKeys.card(cardId) })
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

export function useReopenCard(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ cardId, request }: { cardId: string; request: ReopenCardRequest }) =>
      reopenCard(cardId, request),
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
      queryClient.invalidateQueries({ queryKey: boardKeys.cards })
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

export function useCardDiscussion(cardId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: cardId ? boardKeys.cardDiscussion(cardId) : ['cards', 'missing', 'discussion'],
    queryFn: () => apiGet<CardDiscussionCommentDto[]>(`/cards/${cardId}/discussion`),
    enabled: !!cardId && enabled,
    staleTime: 15_000,
  })
}

export function useCreateCardDiscussion(cardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateCardDiscussionRequest) =>
      apiPost<CardDiscussionCommentDto>(`/cards/${cardId}/discussion`, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.cardDiscussion(cardId) })
    },
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

/** CARD-0166 S7: on-demand bidirectional tracker sync for one board. */
export function useSyncTracker(boardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiPost<TrackerSyncRunResultDto>(`/boards/${boardId}/tracker/sync`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
      queryClient.invalidateQueries({ queryKey: boardKeys.all })
      queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
    },
  })
}

export function formatTrackerSyncSummary(result: TrackerSyncRunResultDto): string {
  if (!result.boards.length) return 'Tracker sync: no external boards.'
  return result.boards
    .map((b) => {
      const parts = [
        `pulled ${b.issuesPulled}`,
        `comments in ${b.commentsIn}/out ${b.commentsOut}`,
        `labels ${b.labelsChanged}`,
        `state ${b.stateChanges}`,
        `creates ${b.creates}`,
      ]
      if (b.skips.length) parts.push(`skips: ${b.skips.join(', ')}`)
      if (b.error) parts.push(`error: ${b.error}`)
      return `${b.boardName}: ${parts.join(', ')}`
    })
    .join(' | ')
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
