import { Anchor, Badge, Box, Group, Paper, Stack, Text } from '@mantine/core'
import { useMemo } from 'react'
import { Link, useNavigate } from 'react-router'
import { useAgentTasks, type AgentTaskSummaryDto } from '../../api/agentTasks'
import { STATUS_COLOR, formatCost, shortId } from '../delegations/taskVisuals'
import { isActiveTask } from './projectGrouping'
import { isUnreadDeliverable, taskIsInProject } from './taskReview'

const DONE_LIMIT = 12

/**
 * The project's delegations at a glance: everything in flight first (Blocked on top — it needs
 * you), then what recently finished. Each row deep-links into the full board's drawer.
 * `dirKeys` is every normalised directory the project spans (main, subdirectories, worktrees) —
 * a task belongs here when either the repo it came from or the checkout it runs in matches.
 */
export function ProjectTasksPanel({ dirKeys }: { dirKeys: string[] }) {
  const tasks = useAgentTasks()
  const navigate = useNavigate()

  const { active, done } = useMemo(() => {
    const mine = (tasks.data ?? []).filter((t) => taskIsInProject(t, dirKeys))
    const active = mine
      .filter(isActiveTask)
      .sort((a, b) => rank(a) - rank(b) || b.createdAt.localeCompare(a.createdAt))
    const done = mine
      .filter((t) => !isActiveTask(t))
      .sort(
        (a, b) =>
          Number(isUnreadDeliverable(b)) - Number(isUnreadDeliverable(a)) ||
          (b.completedAt ?? b.createdAt).localeCompare(a.completedAt ?? a.createdAt),
      )
      .slice(0, DONE_LIMIT)
    return { active, done }
  }, [tasks.data, dirKeys])

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
        showCount={false}
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
  showCount = true,
}: {
  label: string
  empty: string
  tasks: AgentTaskSummaryDto[]
  onOpen: (id: string) => void
  showCount?: boolean
}) {
  return (
    <Stack gap={6}>
      <Group gap="xs">
        <Text size="xs" tt="uppercase" fw={700} c="dimmed">
          {label}
        </Text>
        {showCount && <Badge size="xs" variant="default">{tasks.length}</Badge>}
      </Group>
      {tasks.length === 0 ? (
        <Text size="xs" c="dimmed">
          {empty}
        </Text>
      ) : (
        tasks.map((task) => {
          const unread = isUnreadDeliverable(task)
          const readTarget = task.deliverablePath
            ? `/plans?${new URLSearchParams({
                file: task.deliverablePath,
                ...(task.deliverableRef ? { ref: task.deliverableRef } : {}),
                task: task.id,
              }).toString()}`
            : null
          return (
            <Paper
              key={task.id}
              withBorder
              p={6}
              radius="sm"
              component="div"
              role="button"
              tabIndex={0}
              onClick={() => onOpen(task.id)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') onOpen(task.id)
              }}
              aria-label={`Open task ${shortId(task.id)}`}
              style={{ cursor: 'pointer' }}
            >
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
                  {unread && (
                    <Box component="span" c="violet" mr={6} aria-label="Unread" data-testid={`task-unread-${task.id}`}>
                      ●
                    </Box>
                  )}
                  {task.title}
                </Text>
                {unread && readTarget && (
                  <Anchor
                    component={Link}
                    to={readTarget}
                    size="xs"
                    onClick={(event) => event.stopPropagation()}
                    data-testid={`task-read-${task.id}`}
                  >
                    Read
                  </Anchor>
                )}
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
          )
        })
      )}
    </Stack>
  )
}
