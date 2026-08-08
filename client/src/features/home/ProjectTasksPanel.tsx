import { Anchor, Badge, Group, Paper, Stack, Text, UnstyledButton } from '@mantine/core'
import { useMemo } from 'react'
import { Link, useNavigate } from 'react-router'
import { useAgentTasks, type AgentTaskSummaryDto } from '../../api/agentTasks'
import { STATUS_COLOR, formatCost, shortId } from '../delegations/taskVisuals'
import { isActiveTask, normalizeDir, taskProjectDir } from './projectGrouping'

const DONE_LIMIT = 12

/**
 * The project's delegations at a glance: everything in flight first (Blocked on top — it needs
 * you), then what recently finished. Each row deep-links into the full board's drawer.
 */
export function ProjectTasksPanel({ projectKey }: { projectKey: string }) {
  const tasks = useAgentTasks()
  const navigate = useNavigate()

  const { active, done } = useMemo(() => {
    const mine = (tasks.data ?? []).filter(
      (t) => normalizeDir(taskProjectDir(t)) === projectKey,
    )
    const active = mine
      .filter(isActiveTask)
      .sort((a, b) => rank(a) - rank(b) || b.createdAt.localeCompare(a.createdAt))
    const done = mine
      .filter((t) => !isActiveTask(t))
      .sort((a, b) => (b.completedAt ?? b.createdAt).localeCompare(a.completedAt ?? a.createdAt))
      .slice(0, DONE_LIMIT)
    return { active, done }
  }, [tasks.data, projectKey])

  if (tasks.error) {
    return (
      <Text size="sm" c="dimmed" ta="center" py="xl">
        Delegated tasks are unavailable — the server did not answer for them.
      </Text>
    )
  }
  if (!tasks.data) return null

  return (
    <Stack gap="sm" style={{ minHeight: 0, flexGrow: 1, overflowY: 'auto' }}>
      <Section
        label="In flight"
        empty="Nothing running here — Delegate work to queue something for the pool."
        tasks={active}
        onOpen={(id) => navigate(`/orchestrator?tab=delegations&task=${id}`)}
      />
      <Section
        label="Done"
        empty="Nothing finished yet."
        tasks={done}
        onOpen={(id) => navigate(`/orchestrator?tab=delegations&task=${id}`)}
      />
      <Anchor component={Link} to="/orchestrator?tab=delegations" size="xs" c="dimmed">
        Open the full delegations board →
      </Anchor>
    </Stack>
  )
}

/** Blocked first — it is the only state waiting on a human. */
function rank(task: AgentTaskSummaryDto): number {
  return task.status === 'Blocked' ? 0 : 1
}

function Section({
  label,
  empty,
  tasks,
  onOpen,
}: {
  label: string
  empty: string
  tasks: AgentTaskSummaryDto[]
  onOpen: (id: string) => void
}) {
  return (
    <Stack gap={6}>
      <Group gap="xs">
        <Text size="xs" tt="uppercase" fw={700} c="dimmed">
          {label}
        </Text>
        <Badge size="xs" variant="default">
          {tasks.length}
        </Badge>
      </Group>
      {tasks.length === 0 ? (
        <Text size="xs" c="dimmed">
          {empty}
        </Text>
      ) : (
        tasks.map((task) => (
          <UnstyledButton key={task.id} onClick={() => onOpen(task.id)} aria-label={`Open task ${shortId(task.id)}`}>
            <Paper withBorder p={6} radius="sm">
              <Group gap={6} wrap="nowrap">
                <Badge
                  size="xs"
                  variant="light"
                  color={STATUS_COLOR[task.status]}
                  style={{ flexShrink: 0 }}
                >
                  {task.status}
                </Badge>
                <Text size="sm" truncate style={{ flexGrow: 1, minWidth: 0 }}>
                  {task.title}
                </Text>
                {task.agentName && (
                  <Text size="xs" c="dimmed" truncate style={{ maxWidth: 90, flexShrink: 0 }}>
                    {task.agentName}
                  </Text>
                )}
                <Text
                  size="xs"
                  c="dimmed"
                  style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}
                >
                  {formatCost(task.costUsd)}
                </Text>
              </Group>
            </Paper>
          </UnstyledButton>
        ))
      )}
    </Stack>
  )
}
