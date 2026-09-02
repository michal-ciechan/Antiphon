import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  Paper,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useMemo, useRef, useState } from 'react'
import { TbAlertCircle, TbPlus, TbRefresh } from 'react-icons/tb'
import { Link, useSearchParams } from 'react-router'
import {
  useAgentTaskListSummary,
  useAgentTasks,
  type AgentTaskSummaryDto,
} from '../../api/agentTasks'
import { DelegateModal } from './DelegateModal'
import { TaskChip } from './TaskChip'
import { TaskDrawer } from './TaskDrawer'
import { TaskTree } from './TaskTree'
import { LANES, buildTaskForest, formatCost, laneOf, subtreeIds, type TaskNode } from './taskVisuals'

/**
 * The delegations board: the fan-out on the left, what is happening right now on the right.
 *
 * Two views of the same rows on purpose — the tree answers "who asked for what" (the question that
 * matters once sub-orchestrators are normal) and the lanes answer "what needs me" without making
 * you walk a tree to find it.
 */
export function DelegationsBoard() {
  const tasks = useAgentTasks(false, { since: 'active' })
  const summary = useAgentTaskListSummary()
  // ?task=<id> opens the drawer on arrival — how the home page's task rows land here.
  const [searchParams] = useSearchParams()
  const [selectedId, setSelectedId] = useState<string | null>(searchParams.get('task'))
  const [drawerId, setDrawerId] = useState<string | null>(searchParams.get('task'))
  const [onlyThisRun, setOnlyThisRun] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  // null = nothing toggled yet, so the default applies. Once the user has opened or closed
  // anything their set wins outright — a refetch must never re-collapse what they opened.
  const [expanded, setExpanded] = useState<Set<string> | null>(null)

  const all = useMemo(() => tasks.data ?? [], [tasks.data])
  const forest = useMemo(() => buildTaskForest(all), [all])

  // Roots open, deeper sub-orchestrators closed: the run's top-level fan-out is visible at once,
  // and each subtree stays one line until you go looking.
  const defaultExpanded = useMemo(() => new Set(forest.map((node) => node.task.id)), [forest])
  const effectiveExpanded = expanded ?? defaultExpanded

  const selectedRun = useMemo(() => {
    if (!onlyThisRun || !selectedId) return null
    const root = findRoot(forest, selectedId)
    return root ? subtreeIds(root) : null
  }, [onlyThisRun, selectedId, forest])

  const visible = useMemo(
    () => (selectedRun ? all.filter((task) => selectedRun.has(task.id)) : all),
    [all, selectedRun],
  )

  const totals = summary.data

  const toggle = (id: string) => {
    setExpanded((previous) => {
      const next = new Set(previous ?? defaultExpanded)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const open = (task: AgentTaskSummaryDto | string) => {
    const id = typeof task === 'string' ? task : task.id
    setSelectedId(id)
    setDrawerId(id)
  }

  if (tasks.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (tasks.error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading delegated tasks">
        {tasks.error instanceof Error ? tasks.error.message : 'No tasks returned.'}
      </Alert>
    )
  }

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Group gap="xs">
          <Title order={4}>Delegations</Title>
          <Badge variant="light" color="gray">
            {totals?.runs ?? 0} run{totals?.runs === 1 ? '' : 's'}
          </Badge>
          {(totals?.active ?? 0) > 0 && (
            <Badge variant="light" color="active">
              {totals?.active} working
            </Badge>
          )}
          {(totals?.blocked ?? 0) > 0 && (
            <Badge variant="light" color="warning">
              {totals?.blocked} blocked
            </Badge>
          )}
          <Badge variant="default" style={{ fontVariantNumeric: 'tabular-nums' }}>
            {formatCost(totals?.totalCostUsd ?? 0)}
          </Badge>
        </Group>
        <Group gap="xs">
          {selectedId && (
            <Switch
              size="xs"
              label="Only this run"
              checked={onlyThisRun}
              onChange={(event) => setOnlyThisRun(event.currentTarget.checked)}
            />
          )}
          <Tooltip label="Refresh">
            <ActionIcon variant="subtle" onClick={() => tasks.refetch()} loading={tasks.isFetching}>
              <TbRefresh />
            </ActionIcon>
          </Tooltip>
          <Button size="xs" leftSection={<TbPlus size={14} />} onClick={() => setCreateOpen(true)}>
            New task
          </Button>
        </Group>
      </Group>

      <Group align="stretch" gap="md" wrap="nowrap" style={{ alignItems: 'flex-start' }}>
        <Paper withBorder p="xs" w={380} style={{ flexShrink: 0 }}>
          <Text size="xs" c="dimmed" tt="uppercase" fw={700} mb={6}>
            Fan-out
          </Text>
          {/* A plain scroll box, not ScrollArea: Mantine's viewport content wrapper is
              display:table, so it sizes to its content and the rows never shrink — which pushes
              the tier badge and the subtree total off the right edge instead of truncating. */}
          <Box style={{ maxHeight: 620, overflowY: 'auto', overflowX: 'hidden' }}>
            <TaskTree
              forest={forest}
              expanded={effectiveExpanded}
              selectedId={selectedId}
              runs={totals?.runs ?? 0}
              onToggle={toggle}
              onSelect={open}
            />
          </Box>
        </Paper>

        <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="sm" style={{ flex: 1 }}>
          {LANES.map((lane) => {
            const laneTasks = visible.filter((task) => laneOf(task.status) === lane.key)
            return (
              <Paper key={lane.key} withBorder p="xs" data-testid={`lane-${lane.key}`}>
                <Group justify="space-between" mb={6} wrap="nowrap">
                  <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
                    <Text size="xs" tt="uppercase" fw={700}>
                      {lane.label}
                    </Text>
                    {lane.key === 'done' && (
                      <Anchor
                        component={Link}
                        to="/orchestrator?tab=history"
                        size="xs"
                        c="dimmed"
                        style={{ flexShrink: 0 }}
                      >
                        older in History →
                      </Anchor>
                    )}
                  </Group>
                  <Badge size="xs" variant="default">
                    {laneTasks.length}
                  </Badge>
                </Group>
                <VirtualTaskLane
                  tasks={laneTasks}
                  hint={lane.hint}
                  selectedId={selectedId}
                  onOpen={open}
                />
              </Paper>
            )
          })}
        </SimpleGrid>
      </Group>

      <TaskDrawer taskId={drawerId} onClose={() => setDrawerId(null)} />
      <DelegateModal opened={createOpen} onClose={() => setCreateOpen(false)} />
    </Stack>
  )
}

function VirtualTaskLane({
  tasks,
  hint,
  selectedId,
  onOpen,
}: {
  tasks: AgentTaskSummaryDto[]
  hint: string
  selectedId: string | null
  onOpen: (task: AgentTaskSummaryDto | string) => void
}) {
  const scrollRef = useRef<HTMLDivElement>(null)
  // TanStack Virtual returns imperative scroll/measurement functions; this component deliberately
  // owns them and does not memoize or pass the virtualizer object across a memoization boundary.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: tasks.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => 118,
    overscan: 3,
    // Render the first viewport immediately. ResizeObserver replaces this with the actual size
    // after mount; without it a cold render can briefly show an empty lane.
    initialRect: { width: 0, height: 560 },
  })
  // A real browser populates virtual items synchronously once the scroll element mounts. Keep a
  // first viewport available during that hand-off (and in DOM test environments with no layout).
  const virtualItems = virtualizer.getVirtualItems()
  const rows =
    virtualItems.length > 0
      ? virtualItems
      : tasks.slice(0, 8).map((_, index) => ({ index, start: index * 118 }))

  if (tasks.length === 0) {
    return (
      <Text size="xs" c="dimmed">
        {hint}
      </Text>
    )
  }

  return (
    <Box ref={scrollRef} style={{ height: 560, overflowY: 'auto' }}>
      <Box style={{ height: virtualizer.getTotalSize() || tasks.length * 118, position: 'relative' }}>
        {rows.map((row) => {
          const task = tasks[row.index]
          return (
            <Box
              key={task.id}
              data-index={row.index}
              ref={virtualizer.measureElement}
              style={{ position: 'absolute', top: 0, left: 0, width: '100%', transform: `translateY(${row.start}px)` }}
              pb={6}
            >
              <TaskChip task={task} selected={selectedId === task.id} onOpen={onOpen} />
            </Box>
          )
        })}
      </Box>
    </Box>
  )
}

function findRoot(forest: TaskNode[], taskId: string): TaskNode | null {
  for (const node of forest) {
    if (subtreeIds(node).has(taskId)) return node
  }
  return null
}
