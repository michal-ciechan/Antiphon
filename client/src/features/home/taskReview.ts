import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { normalizeDir, taskProjectDir, taskRunDir } from './projectGrouping'

const REVIEW_WINDOW_MS = 7 * 24 * 60 * 60 * 1000

/** The task scope shared by the home list and its pull-to-read badge. */
export function taskIsInProject(task: AgentTaskSummaryDto, dirKeys: string[]): boolean {
  const keys = new Set(dirKeys)
  return keys.has(normalizeDir(taskProjectDir(task))) || keys.has(normalizeDir(taskRunDir(task)))
}

/** A successful, recent, human-created deliverable which no operator has opened yet. */
export function isUnreadDeliverable(
  task: Pick<AgentTaskSummaryDto, 'status' | 'role' | 'completedAt'> & { readAt?: string | null },
  now = Date.now(),
): boolean {
  if (task.status !== 'Succeeded' || task.role === 'Check' || task.readAt !== null || !task.completedAt) {
    return false
  }
  const completedAt = Date.parse(task.completedAt)
  return Number.isFinite(completedAt) && completedAt >= now - REVIEW_WINDOW_MS && completedAt <= now
}
