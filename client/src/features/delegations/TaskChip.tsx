import { Badge, Group, Paper, Text, Tooltip } from '@mantine/core'
import { TbArrowBigUpLine, TbGitBranch, TbLock, TbSitemap } from 'react-icons/tb'
import type { AgentModelLevel } from '../../api/agents'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
import {
  STATUS_COLOR,
  TIER_VISUALS,
  WORKSPACE_LABEL,
  elapsedSeconds,
  formatCost,
  formatDuration,
  shortId,
  tierAlias,
  tierTooltip,
} from './taskVisuals'

/**
 * The tier, on its own axis (see TIER_VISUALS) — solid fable down to grey haiku. The NAME follows
 * the agent program the task runs on (CARD-0084 S4); the intensity deliberately does not, because
 * Frontier is the same rung on the ladder whoever is on it.
 */
export function TierBadge({
  level,
  kind = 'ClaudeCode',
  size = 'xs',
}: {
  level: AgentModelLevel
  kind?: AgentKind
  size?: string
}) {
  const tier = TIER_VISUALS[level]
  return (
    <Tooltip label={tierTooltip(level, kind)} withArrow>
      <Badge size={size} variant={tier.variant} color={tier.color} data-testid={`tier-${level}`}>
        {tierAlias(level, kind)}
      </Badge>
    </Tooltip>
  )
}

/**
 * One task on the board: what it is, who ran it, at what tier, for how long and how much. The
 * escalation marker is deliberately loud — a task that got bumped is the one worth looking at.
 */
export function TaskChip({
  task,
  selected = false,
  onOpen,
}: {
  task: AgentTaskSummaryDto
  selected?: boolean
  onOpen: (id: string) => void
}) {
  const workspaceIcon =
    task.workspace === 'Worktree' ? <TbGitBranch size={12} /> : task.workspace === 'ReadOnly' ? <TbLock size={12} /> : null

  return (
    <Paper
      withBorder
      p="xs"
      component="button"
      type="button"
      onClick={() => onOpen(task.id)}
      data-testid={`task-chip-${shortId(task.id)}`}
      aria-pressed={selected}
      style={{
        display: 'block',
        width: '100%',
        textAlign: 'left',
        cursor: 'pointer',
        borderColor: selected ? 'var(--mantine-color-active-5)' : undefined,
        borderLeft: `3px solid var(--mantine-color-${STATUS_COLOR[task.status]}-6)`,
      }}
    >
      <Group gap={6} wrap="nowrap" justify="space-between" align="flex-start">
        <Text size="sm" fw={600} lineClamp={2} style={{ flex: 1 }}>
          {task.title}
        </Text>
        {task.kind === 'Orchestrator' && (
          <Tooltip label="Sub-orchestrator — runs its own delegates" withArrow>
            <Badge size="xs" variant="light" color="active" leftSection={<TbSitemap size={11} />}>
              orch
            </Badge>
          </Tooltip>
        )}
      </Group>

      <Group gap={6} mt={6} wrap="wrap">
        {/* Reads left to right as the ladder it is: "opus → fable". */}
        {task.escalatedFrom && (
          <Tooltip
            label={`Escalated from ${tierAlias(task.escalatedFrom, task.agentKind)} — attempt ${task.attempt}`}
            withArrow
          >
            <Badge size="xs" variant="light" color="warning" leftSection={<TbArrowBigUpLine size={11} />}>
              {tierAlias(task.escalatedFrom, task.agentKind)} →
            </Badge>
          </Tooltip>
        )}
        <TierBadge level={task.modelLevel} kind={task.agentKind} />
        <Badge size="xs" variant="default">
          {task.role.toLowerCase()}
        </Badge>
        {workspaceIcon && (
          <Tooltip label={WORKSPACE_LABEL[task.workspace]} withArrow>
            <Badge size="xs" variant="default" leftSection={workspaceIcon}>
              {WORKSPACE_LABEL[task.workspace]}
            </Badge>
          </Tooltip>
        )}
      </Group>

      <Group gap={8} mt={6} wrap="nowrap">
        <Text size="xs" c="dimmed" truncate style={{ flex: 1 }}>
          {task.agentName ?? shortId(task.id)}
        </Text>
        <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatDuration(elapsedSeconds(task))}
        </Text>
        <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatCost(task.costUsd)}
        </Text>
      </Group>
    </Paper>
  )
}
