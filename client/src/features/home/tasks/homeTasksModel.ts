import type { AgentSummaryDto } from '../../../api/agents'
import type {
  AgentTaskPipelineDto,
  AgentTaskPipelineHolderDto,
  AgentTaskPipelineInFlightDto,
  AgentTaskPipelineQueueReason,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
  AgentTaskStatus,
} from '../../../api/agentTasks'
import type { AttentionItemDto, AttentionKind } from '../../../api/attention'
import type { CardStatus } from '../../../api/boards'
import type { HomeTaskGroup, HomeTaskHumanReason, HomeTaskItemDto, HomeTaskSource } from '../../../api/homeTasks'
import { STATE_COLORS } from '../../board/boardVisuals'
import { formatDuration, STATUS_COLOR } from '../../delegations/taskVisuals'
import { routingPinChip } from '../../orchestrator/pipelineStageModel'
import { normalizeDir } from '../projectGrouping'

export const SOURCE_LABEL: Record<HomeTaskSource, string> = {
  Card: 'Card',
  Delegation: 'Task',
}

export const HUMAN_REASON_LABEL: Record<HomeTaskHumanReason, string> = {
  Decision: 'Needs decision',
  Question: 'Question',
  Gate: 'Gate',
  Review: 'Review',
}

export const STATE_COLOR: Record<CardStatus | AgentTaskStatus, string> = {
  ...STATE_COLORS,
  ...STATUS_COLOR,
}

export const GROUP_ORDER: HomeTaskGroup[] = ['NeedsHuman', 'Running', 'Review', 'Next', 'Done']

export const GROUP_LABEL: Record<HomeTaskGroup, string> = {
  NeedsHuman: 'Needs you',
  Running: 'Running',
  Review: 'To review',
  Next: 'Up next',
  Done: 'Done',
}

export const GROUP_CAP: Record<HomeTaskGroup, number | null> = {
  NeedsHuman: null,
  Running: null,
  Review: 8,
  Next: 8,
  Done: 12,
}

export interface GroupedHomeTasks {
  NeedsHuman: HomeTaskItemDto[]
  Running: HomeTaskItemDto[]
  Review: HomeTaskItemDto[]
  Next: HomeTaskItemDto[]
  Done: HomeTaskItemDto[]
  hidden: { Review: number; Next: number; Done: number }
}

/**
 * Keep items whose working directory, repo path or worktree folds into the selected project's
 * dirKeys. `normalizeDir` is the same fold ProjectTasksPanel uses (mixed `C:/` and `C:\`, casing).
 */
export function filterByProject(items: HomeTaskItemDto[], dirKeys: string[]): HomeTaskItemDto[] {
  const keys = new Set(dirKeys)
  return items.filter((item) => itemDirs(item).some((dir) => keys.has(normalizeDir(dir))))
}

function itemDirs(item: HomeTaskItemDto): string[] {
  return [item.workingDirectory, item.repoPath, item.worktreePath].filter((dir): dir is string => !!dir)
}

/**
 * Split the already-ordered server list into groups. Never re-sorts. Caps To review / Up next /
 * Done and reports how many were cut.
 */
export function groupItems(items: HomeTaskItemDto[]): GroupedHomeTasks {
  const buckets: Record<HomeTaskGroup, HomeTaskItemDto[]> = {
    NeedsHuman: [],
    Running: [],
    Review: [],
    Next: [],
    Done: [],
  }
  for (const item of items) buckets[item.group].push(item)

  const take = (group: 'Review' | 'Next' | 'Done') => {
    const cap = GROUP_CAP[group]!
    const all = buckets[group]
    return { visible: all.slice(0, cap), hidden: Math.max(0, all.length - cap) }
  }

  const review = take('Review')
  const next = take('Next')
  const done = take('Done')
  return {
    NeedsHuman: buckets.NeedsHuman,
    Running: buckets.Running,
    Review: review.visible,
    Next: next.visible,
    Done: done.visible,
    hidden: { Review: review.hidden, Next: next.hidden, Done: done.hidden },
  }
}

/**
 * First non-blank line of the matching attention evidence. Cards match `CardNeedsDecision` by
 * card id, then `BlockedQuestion` on the bound worker; unbound tasks match `BlockedQuestion` by
 * their own id. Null when the feed has no row — the reason badge still shows.
 */
export function questionFor(item: HomeTaskItemDto, attentionItems: AttentionItemDto[]): string | null {
  const firstLine = (evidence: string) =>
    evidence
      .split(/\r?\n/)
      .map((line) => line.trim())
      .find((line) => line.length > 0) ?? null

  if (item.source === 'Card') {
    const decision = attentionItems.find((row) => row.kind === 'CardNeedsDecision' && row.cardId === item.id)
    if (decision) return firstLine(decision.evidence)
    if (item.worker) {
      const blocked = attentionItems.find(
        (row) => row.kind === 'BlockedQuestion' && row.taskId === item.worker!.taskId,
      )
      if (blocked) return firstLine(blocked.evidence)
    }
    return null
  }

  const blocked = attentionItems.find((row) => row.kind === 'BlockedQuestion' && row.taskId === item.id)
  return blocked ? firstLine(blocked.evidence) : null
}

export function workerAgent(item: HomeTaskItemDto, agents: AgentSummaryDto[]): AgentSummaryDto | null {
  const agentId = item.worker?.agentId
  if (!agentId) return null
  return agents.find((agent) => agent.id === agentId) ?? null
}

const OPEN_WORKER: ReadonlySet<AgentTaskStatus> = new Set(['Queued', 'Dispatched', 'Working', 'Blocked'])

export function isSpawnable(item: HomeTaskItemDto): boolean {
  if (item.source !== 'Card' || !item.boardId) return false
  if (item.ownerAgentId != null) return false
  if (item.state === 'Done' || item.state === 'Canceled') return false
  return !(item.worker && OPEN_WORKER.has(item.worker.status))
}

export function isAnswerable(item: HomeTaskItemDto): boolean {
  if (item.source === 'Delegation') return item.state === 'Blocked'
  return item.worker?.status === 'Blocked'
}

/**
 * Task-progress conditions the rail may badge. Deliberately not total over AttentionKind: a new
 * kind is ignored here until someone adds it. BlockedQuestion / CardStalled are never liveness.
 */
export const LIVENESS_KINDS: ReadonlySet<AttentionKind> = new Set([
  'DeadSession',
  'NeverStarted',
  'BriefUndelivered',
  'ReportUnsettled',
  'UnmarkedWaiting',
  'PastExpectedIdle',
  'ProgressStalled',
  'Overdue',
  'ChecksSpent',
  'UncorrelatedReport',
])

/** Join key for attention and in-flight/queued pipeline rows: the task, never the card. */
function taskKey(item: HomeTaskItemDto): string | null {
  return item.source === 'Delegation' ? item.id : (item.worker?.taskId ?? null)
}

/**
 * First attention row whose taskId is this item's task and whose kind is a progress verdict.
 * Cards join on the bound worker; unbound delegations join on their own id. First match wins.
 */
export function livenessFor(
  item: HomeTaskItemDto,
  attentionItems: AttentionItemDto[],
): AttentionItemDto | null {
  const key = taskKey(item)
  if (!key) return null
  return attentionItems.find((row) => row.taskId === key && LIVENESS_KINDS.has(row.kind)) ?? null
}

/**
 * Instant the Running item started counting elapsed time: worker dispatch, else startedAt, else
 * createdAt. Null off the Running group — Up next / Done / Needs you never print duration.
 */
export function runningSince(item: HomeTaskItemDto): string | null {
  if (item.group !== 'Running') return null
  return item.worker?.dispatchedAt ?? item.startedAt ?? item.createdAt
}

export type HomeTaskPipelineRow =
  | AgentTaskPipelineInFlightDto
  | AgentTaskPipelineQueuedDto
  | AgentTaskPipelineReadyDto

/**
 * In-flight and queued rows match the item's task id (own id or worker.taskId). Ready rows match
 * a card by card.id. Null when the pipeline is missing or has no row for this item.
 */
export function pipelineRowFor(
  item: HomeTaskItemDto,
  pipeline: AgentTaskPipelineDto | null | undefined,
): HomeTaskPipelineRow | null {
  if (!pipeline) return null
  const key = taskKey(item)
  if (key) {
    for (const stage of pipeline.stages) {
      const inFlight = stage.inFlight.find((row) => row.taskId === key)
      if (inFlight) return inFlight
    }
    for (const stage of pipeline.stages) {
      const queued = stage.queued.find((row) => row.taskId === key)
      if (queued) return queued
    }
  }
  if (item.source === 'Card') {
    for (const stage of pipeline.stages) {
      const ready = stage.ready.find((row) => row.card.id === item.id)
      if (ready) return ready
    }
  }
  return null
}

export const QUEUE_REASON_LABEL: Record<AgentTaskPipelineQueueReason, string> = {
  sharedCheckoutLease: 'waiting: shared checkout held by',
  siblingLandInFlight: 'waiting: a sibling is landing',
  concurrencyCap: 'waiting: task slots in use',
  routingPinNotBefore: 'waiting: not before',
  awaitingDispatch: 'queued — next dispatch tick',
}

export interface QueueReasonView {
  reason: AgentTaskPipelineQueueReason
  line: string
  holders: AgentTaskPipelineHolderDto[]
}

/**
 * Why an Up-next item has not dispatched, read from the pipeline queued row. Null when the item
 * has no task key or that task is not queued (a card whose worker is not queued).
 */
export function queueReasonFor(
  item: HomeTaskItemDto,
  pipeline: AgentTaskPipelineDto | null | undefined,
): QueueReasonView | null {
  if (!pipeline) return null
  const key = taskKey(item)
  if (!key) return null
  for (const stage of pipeline.stages) {
    const queued = stage.queued.find((row) => row.taskId === key)
    if (!queued) continue
    return {
      reason: queued.queueReason,
      line: queueReasonLine(queued, pipeline, stage),
      holders: queued.heldBy,
    }
  }
  return null
}

function queueReasonLine(
  queued: AgentTaskPipelineQueuedDto,
  pipeline: AgentTaskPipelineDto,
  stage: AgentTaskPipelineStageDto,
): string {
  switch (queued.queueReason) {
    case 'sharedCheckoutLease': {
      const first = queued.heldBy[0]
      const who = first ? `task-${first.shortId} — ${first.title}` : 'another task'
      const extra = queued.heldBy.length > 1 ? ` +${queued.heldBy.length - 1}` : ''
      return `waiting: shared checkout held by ${who}${extra}`
    }
    case 'siblingLandInFlight': {
      const first = queued.heldBy[0]
      const who = first ? `task-${first.shortId} — ${first.title}` : 'another task'
      return `waiting: a sibling is landing (${who})`
    }
    case 'concurrencyCap':
      return `waiting: ${pipeline.inFlightAgainstCap} of ${pipeline.maxConcurrentTasks} task slots in use`
    case 'routingPinNotBefore': {
      const notBefore = stage.routingPin?.notBefore
      const clock = notBefore ? formatClockUtc(notBefore) : null
      return clock
        ? `waiting: not before ${clock} (routing pin)`
        : 'waiting: not before (routing pin)'
    }
    case 'awaitingDispatch':
      return QUEUE_REASON_LABEL.awaitingDispatch
  }
}

function formatClockUtc(iso: string): string {
  const date = new Date(iso)
  return `${String(date.getUTCHours()).padStart(2, '0')}:${String(date.getUTCMinutes()).padStart(2, '0')}`
}

export interface ReadinessView {
  since: string
  deliverablePath: string
  deliverableRef: string | null
  sourcePlanShortId: string
  sourcePlanTaskId: string
  /** The stage the ready row sits on — the next dispatch. */
  targetRole: AgentTaskPipelineStageDto['role']
  sourceRole?: AgentTaskPipelineReadyDto['sourceRole']
  handoff?: string | null
  /** CARD-0322. `pin: fable` / `pin: fable +2` when the ready row carries a pin. */
  pinChip?: string | null
}

/**
 * Up-next card the pipeline lists as ready for a next stage. Done and NeedsDecision cards are
 * never asked — a close verdict or a parked decision is not a "ready" line.
 */
export function readinessFor(
  item: HomeTaskItemDto,
  pipeline: AgentTaskPipelineDto | null | undefined,
): ReadinessView | null {
  if (!pipeline) return null
  if (item.source !== 'Card') return null
  if (item.group === 'Done' || item.state === 'Done' || item.state === 'NeedsDecision') return null
  for (const stage of pipeline.stages) {
    const ready = stage.ready.find((row) => row.card.id === item.id)
    if (!ready) continue
    return {
      since: ready.readySince,
      deliverablePath: ready.deliverablePath,
      deliverableRef: ready.deliverableRef,
      sourcePlanShortId: ready.sourcePlanShortId,
      sourcePlanTaskId: ready.sourcePlanTaskId,
      targetRole: stage.role,
      sourceRole: ready.sourceRole,
      handoff: ready.handoff,
      pinChip: routingPinChip(ready.routingPin),
    }
  }
  return null
}

export function readyLine(ready: ReadinessView, now: number): string {
  const landed = !ready.sourceRole || ready.sourceRole === 'Plan'
    ? 'plan landed'
    : `${ready.sourceRole} landed`
  const forWord = ready.targetRole === 'TestDesign' ? 'Test design' : ready.targetRole
  return `${landed} ${formatRelativeAgo(ready.since, now)} — ready for ${forWord}`
}

export function formatRelativeAgo(iso: string, now = Date.now()): string {
  const mins = Math.max(0, Math.floor((now - Date.parse(iso)) / 60_000))
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}

export function formatElapsed(fromIso: string, now = Date.now()): string {
  return formatDuration(Math.max(0, (now - Date.parse(fromIso)) / 1000))
}
