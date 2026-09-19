import type {
  AgentTaskListEnvelopeDto,
  AgentTaskListExcludedDto,
  AgentTaskScopeDto,
  AgentTaskSummaryDto,
} from '../api/agentTasks'

export function emptyExcluded(): AgentTaskListExcludedDto {
  return { total: 0, unscoped: 0, byProject: [] }
}

export function agentTaskEnvelope(
  items: AgentTaskSummaryDto[],
  extras: { scope?: AgentTaskScopeDto | null; excluded?: Partial<AgentTaskListExcludedDto> } = {},
): AgentTaskListEnvelopeDto {
  return {
    scope: extras.scope === undefined ? null : extras.scope,
    items,
    excluded: { ...emptyExcluded(), ...extras.excluded },
  }
}
