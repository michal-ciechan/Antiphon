import type { AgentTaskListExcludedDto, AgentTaskSummaryDto } from '../../api/agentTasks'

export function scopeChipLabel(
  task: Pick<AgentTaskSummaryDto, 'scopeSource' | 'projectName'>,
): string {
  return task.scopeSource === 'None' || !task.projectName ? 'unscoped' : task.projectName
}

export function hiddenByScopeLine(excluded: AgentTaskListExcludedDto | undefined): string | null {
  if (!excluded || excluded.total === 0) return null
  const parts = [
    ...excluded.byProject.map((row) => `${row.count} ${row.projectName ?? row.projectId}`),
    excluded.unscoped > 0 ? `${excluded.unscoped} unscoped` : null,
  ].filter((part): part is string => part !== null)
  return `hidden by scope: ${parts.join(', ')}`
}
