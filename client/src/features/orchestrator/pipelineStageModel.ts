import type { AgentModelLevel } from '../../api/agents'
import type {
  AgentTaskPipelineBlockedDto,
  AgentTaskPipelineDto,
  AgentTaskPipelineHolderDto,
  AgentTaskPipelineInFlightDto,
  AgentTaskPipelineQueuedDto,
  AgentTaskPipelineReadyDto,
  AgentTaskPipelineStageDto,
  AgentTaskRole,
} from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { citationHead, formatClockTime } from '../home/workLineFormat'
import { STATUS_COLOR, tierAlias } from '../delegations/taskVisuals'

export type PipelineRowKind = 'inFlight' | 'blocked' | 'queued' | 'ready'

export interface PipelineRowView {
  key: string
  kind: PipelineRowKind
  identifier: string | null
  title: string
  right: string
  color: string
  target: { drawer: string } | { to: string }
  ariaLabel: string
}

export interface StageCounts {
  inFlight: number
  recommended: number | null
  queued: number
  blocked: number
  ready: number
}

export interface VisibleStages {
  shown: AgentTaskPipelineStageDto[]
  idleCount: number
}

export type RightCellSource =
  | { kind: 'inFlight'; row: AgentTaskPipelineInFlightDto }
  | { kind: 'blocked'; row: AgentTaskPipelineBlockedDto }
  | {
      kind: 'queued'
      row: AgentTaskPipelineQueuedDto
      stage: AgentTaskPipelineStageDto
      pipeline: AgentTaskPipelineDto
    }
  | { kind: 'ready'; row: AgentTaskPipelineReadyDto }

export type RowTargetSource =
  | { kind: Exclude<PipelineRowKind, 'ready'>; taskId: string }
  | { kind: 'ready'; row: AgentTaskPipelineReadyDto }

/** Operator labels: Execute is the UI alias for Code; Other is Custom. */
export const STAGE_LABEL: Record<AgentTaskRole, string> = {
  Custom: 'Other',
  Plan: 'Plan',
  Code: 'Execute',
  Review: 'Review',
  Debug: 'Debug',
  Coverage: 'Coverage',
  Docs: 'Docs',
  Commit: 'Commit',
  Test: 'Test',
  Deploy: 'Deploy',
  Merge: 'Merge',
  Check: 'Check',
  Distill: 'Distill',
  Diagnose: 'Diagnose',
  Investigate: 'Investigate',
  TestDesign: 'Test design',
}

const KIND_WORD: Record<PipelineRowKind, string> = {
  inFlight: 'running',
  blocked: 'blocked',
  queued: 'queued',
  ready: 'ready',
}

const KIND_COLOR: Record<PipelineRowKind, string> = {
  inFlight: STATUS_COLOR.Working,
  blocked: STATUS_COLOR.Blocked,
  queued: STATUS_COLOR.Queued,
  ready: STATUS_COLOR.Succeeded,
}

function stageHasRows(stage: AgentTaskPipelineStageDto): boolean {
  return (
    stage.inFlight.length > 0 ||
    stage.blocked.length > 0 ||
    stage.queued.length > 0 ||
    stage.ready.length > 0
  )
}

export function visibleStages(dto: AgentTaskPipelineDto): VisibleStages {
  const shown = dto.stages.filter(stageHasRows)
  return { shown, idleCount: dto.stages.length - shown.length }
}

export function stageCounts(stage: AgentTaskPipelineStageDto): StageCounts {
  return {
    inFlight: stage.inFlightCount,
    recommended: stage.recommendedInFlight,
    queued: stage.queued.length,
    blocked: stage.blocked.length,
    ready: stage.ready.length,
  }
}

/** Header counts: running only when something is in flight; other kinds only when non-zero. */
export function stageCountLine(stage: AgentTaskPipelineStageDto): string {
  const counts = stageCounts(stage)
  const parts: string[] = []
  if (counts.inFlight > 0) {
    parts.push(
      counts.recommended == null
        ? `${counts.inFlight} running`
        : `${counts.inFlight}/${counts.recommended} running`,
    )
  }
  if (counts.queued > 0) parts.push(`${counts.queued} queued`)
  if (counts.blocked > 0) parts.push(`${counts.blocked} blocked`)
  if (counts.ready > 0) parts.push(`${counts.ready} ready`)
  return parts.join(' · ')
}

/**
 * Right-aligned pin on the stage header. The full server alias (not compactAlias) — the phone
 * shortening is only the in-flight right cell.
 */
export function stagePinLabel(stage: AgentTaskPipelineStageDto): string | null {
  const pin = stage.routingPin
  if (!pin) return null
  const alias = pin.modelLevel
    ? tierAlias(pin.modelLevel, pin.agentKind ?? 'ClaudeCode')
    : pin.agentKind
  return alias ? `pin ${alias}` : null
}

/** Drops only the `gpt-5.6-` prefix so a Codex cell stays inside ~14 characters. */
export function compactAlias(level: AgentModelLevel, kind: AgentKind): string {
  const alias = tierAlias(level, kind)
  return alias.startsWith('gpt-5.6-') ? alias.slice('gpt-5.6-'.length) : alias
}

/**
 * Phone-width elapsed / ago: largest unit only (`4m`, `3h`, `1d`). formatDuration's `4m00` /
 * `3h00m` would blow the right-cell budget once an alias sits next to it.
 */
export function compactElapsed(fromIso: string | null | undefined, now: number): string {
  if (!fromIso) return '0s'
  const parsed = Date.parse(fromIso)
  if (Number.isNaN(parsed)) return '0s'
  const seconds = Math.max(0, Math.floor((now - parsed) / 1000))
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)
  if (days > 0) return `${days}d`
  if (hours > 0) return `${hours}h`
  if (minutes > 0) return `${minutes}m`
  return `${seconds}s`
}

function holderCitation(holder: AgentTaskPipelineHolderDto): string {
  const cited = citationHead(holder.title)
  if (cited !== holder.title) {
    const match = /^#\d+/.exec(cited)
    if (match) return match[0]
  }
  return `~${holder.shortId}`
}

export function compactQueueReason(
  row: AgentTaskPipelineQueuedDto,
  stage: AgentTaskPipelineStageDto,
  pipeline: AgentTaskPipelineDto,
  formatTime: (iso: string) => string = formatClockTime,
): string {
  switch (row.queueReason) {
    case 'sharedCheckoutLease': {
      const first = row.heldBy[0]
      if (!first) return 'behind'
      const extra = row.heldBy.length > 1 ? ` +${row.heldBy.length - 1}` : ''
      return `behind ${holderCitation(first)}${extra}`
    }
    case 'siblingLandInFlight': {
      const first = row.heldBy[0]
      return first ? `landing ${holderCitation(first)}` : 'landing'
    }
    case 'routingPinNotBefore': {
      const notBefore = stage.routingPin?.notBefore
      return notBefore ? `after ${formatTime(notBefore)}` : 'after'
    }
    case 'concurrencyCap':
      return `slots ${pipeline.inFlightAgainstCap}/${pipeline.maxConcurrentTasks}`
    case 'awaitingDispatch':
      return 'queued'
  }
}

export function rightCell(
  source: RightCellSource,
  now: number,
  formatTime: (iso: string) => string = formatClockTime,
): string {
  switch (source.kind) {
    case 'inFlight': {
      const elapsed = compactElapsed(source.row.dispatchedAt, now)
      // Pre-S1 servers omit agentKind; render the elapsed alone rather than guessing an alias.
      if (!source.row.agentKind || !source.row.modelLevel) return elapsed
      return `${compactAlias(source.row.modelLevel, source.row.agentKind)} ${elapsed}`
    }
    case 'blocked':
      return source.row.routingExhausted ? 'no route' : 'blocked'
    case 'queued':
      return compactQueueReason(source.row, source.stage, source.pipeline, formatTime)
    case 'ready':
      return `ready ${compactElapsed(source.row.readySince, now)}`
  }
}

export function rowTarget(source: RowTargetSource): PipelineRowView['target'] {
  if (source.kind === 'ready') {
    if (!source.row.deliverablePath) {
      return { drawer: source.row.sourcePlanTaskId }
    }
    const params = new URLSearchParams({
      file: source.row.deliverablePath,
      ...(source.row.deliverableRef ? { ref: source.row.deliverableRef } : {}),
      task: source.row.sourcePlanTaskId,
    })
    return { to: `/plans?${params.toString()}` }
  }
  return { drawer: source.taskId }
}

export function rowLabel(row: Pick<PipelineRowView, 'identifier' | 'title' | 'kind'>): string {
  return `Open ${row.identifier ?? row.title} — ${KIND_WORD[row.kind]}`
}

function identity(
  card: { identifier: string; title: string } | null,
  title: string,
): { identifier: string | null; title: string } {
  if (card) return { identifier: displayIdentifier(card.identifier), title: card.title }
  return { identifier: null, title: citationHead(title) }
}

function viewFor(
  kind: PipelineRowKind,
  key: string,
  card: { identifier: string; title: string } | null,
  title: string,
  right: string,
  target: PipelineRowView['target'],
): PipelineRowView {
  const { identifier, title: lineTitle } = identity(card, title)
  const row = { key, kind, identifier, title: lineTitle, right, color: KIND_COLOR[kind], target, ariaLabel: '' }
  row.ariaLabel = rowLabel(row)
  return row
}

export function stageRows(
  stage: AgentTaskPipelineStageDto,
  now: number,
  pipeline: AgentTaskPipelineDto,
  formatTime: (iso: string) => string = formatClockTime,
): PipelineRowView[] {
  const inFlight = stage.inFlight.map((row) =>
    viewFor(
      'inFlight',
      row.taskId,
      row.card,
      row.title,
      rightCell({ kind: 'inFlight', row }, now, formatTime),
      rowTarget({ kind: 'inFlight', taskId: row.taskId }),
    ),
  )
  const blocked = stage.blocked.map((row) =>
    viewFor(
      'blocked',
      row.taskId,
      row.card,
      row.title,
      rightCell({ kind: 'blocked', row }, now, formatTime),
      rowTarget({ kind: 'blocked', taskId: row.taskId }),
    ),
  )
  const queued = stage.queued.map((row) =>
    viewFor(
      'queued',
      row.taskId,
      row.card,
      row.title,
      rightCell({ kind: 'queued', row, stage, pipeline }, now, formatTime),
      rowTarget({ kind: 'queued', taskId: row.taskId }),
    ),
  )
  const ready = stage.ready.map((row) =>
    viewFor(
      'ready',
      `ready:${row.card.id}`,
      row.card,
      row.card.title,
      rightCell({ kind: 'ready', row }, now, formatTime),
      rowTarget({ kind: 'ready', row }),
    ),
  )
  return [...inFlight, ...blocked, ...queued, ...ready]
}

export function fleetStrip(
  dto: AgentTaskPipelineDto,
  formatTime: (iso: string) => string = formatClockTime,
): string {
  const slots = `${dto.inFlightAgainstCap} of ${dto.maxConcurrentTasks} slots`
  if (dto.inFlightAgainstCap === 0) return slots
  return `${slots} · as of ${formatTime(dto.asOf)}`
}

export function isPipelineEmpty(dto: AgentTaskPipelineDto): boolean {
  return visibleStages(dto).shown.length === 0
}

export function idleLine(idleCount: number): string {
  return idleCount === 1 ? '1 idle stage' : `${idleCount} idle stages`
}
