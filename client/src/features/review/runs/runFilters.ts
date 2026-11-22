import type { ReviewRunStatus, ReviewRunSummaryDto } from '../../../api/reviewEngine'
import type { RunsFilters } from '../useReviewUiStore'

export const IN_REVIEW_STATUSES: ReviewRunStatus[] = ['Queued', 'Running']

export const STATUS_ORDER: ReviewRunStatus[] = ['Queued', 'Running', 'AwaitingFixes', 'AtGate', 'Approved', 'AwaitingMerge', 'Merged', 'Closed', 'Failed']

/** The board's columns: statuses grouped by whose move it is. */
export const BOARD_COLUMNS: { key: string; title: string; statuses: ReviewRunStatus[]; color: string }[] = [
  { key: 'queued', title: 'Queued', statuses: ['Queued'], color: 'gray' },
  { key: 'running', title: 'Antiphon working', statuses: ['Running'], color: 'active' },
  { key: 'author', title: 'Awaiting fixes', statuses: ['AwaitingFixes'], color: 'warning' },
  { key: 'gate', title: 'At a human gate', statuses: ['AtGate'], color: 'pink' },
  { key: 'approved', title: 'Approved', statuses: ['Approved', 'AwaitingMerge'], color: 'success' },
  { key: 'done', title: 'Done', statuses: ['Merged', 'Closed', 'Failed'], color: 'violet' },
]

export function hasAllStatuses(filters: RunsFilters, statuses: ReviewRunStatus[]): boolean {
  return statuses.every((s) => filters.statuses.includes(s))
}

export function toggleStatuses(filters: RunsFilters, statuses: ReviewRunStatus[]): ReviewRunStatus[] {
  return hasAllStatuses(filters, statuses)
    ? filters.statuses.filter((s) => !statuses.includes(s))
    : [...new Set([...filters.statuses, ...statuses])]
}

/** The client-side filters (search and repo go to the server as `q` and `repo`). */
export function applyRunFilters(runs: ReviewRunSummaryDto[], filters: RunsFilters): ReviewRunSummaryDto[] {
  return runs
    .filter((r) => filters.statuses.length === 0 || filters.statuses.includes(r.status))
    .filter((r) => !filters.humans || r.waitingOn === 'Humans')
    .filter((r) => !filters.deepRounds || r.round >= 3)
    .filter((r) => filters.models.length === 0 || r.models.some((m) => filters.models.includes(m)))
}

export function anyClientFilter(filters: RunsFilters): boolean {
  return filters.statuses.length > 0 || filters.humans || filters.deepRounds || filters.models.length > 0
}
