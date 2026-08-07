import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  Paper,
  ScrollArea,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { useMemo, useState } from 'react'
import { TbAlertCircle, TbPlus, TbRefresh } from 'react-icons/tb'
import { useAgentTasks, type AgentTaskSummaryDto } from '../../api/agentTasks'
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
  const tasks = useAgentTasks()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [drawerId, setDrawerId] = useState<string | null>(null)
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

  const totals = useMemo(
    () => ({
      runs: forest.length,
      active: all.filter((t) => t.status === 'Dispatched' || t.status === 'Working').length,
      blocked: all.filter((t) => t.status === 'Blocked').length,
      spend: all.reduce((sum, t) => sum + t.costUsd, 0),
    }),
    [all, forest.length],
  )

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
            {totals.runs} run{totals.runs === 1 ? '' : 's'}
          </Badge>
          {totals.active > 0 && (
            <Badge variant="light" color="active">
              {totals.active} working
            </Badge>
          )}
          {totals.blocked > 0 && (
            <Badge variant="light" color="warning">
              {totals.blocked} blocked
            </Badge>
          )}
          <Badge variant="default" style={{ fontVariantNumeric: 'tabular-nums' }}>
            {formatCost(totals.spend)}
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
                  <Text size="xs" tt="uppercase" fw={700}>
                    {lane.label}
                  </Text>
                  <Badge size="xs" variant="default">
                    {laneTasks.length}
                  </Badge>
                </Group>
                <ScrollArea.Autosize mah={560}>
                  <Stack gap={6}>
                    {laneTasks.length === 0 ? (
                      <Text size="xs" c="dimmed">
                        {lane.hint}
                      </Text>
                    ) : (
                      laneTasks.map((task) => (
                        <TaskChip
                          key={task.id}
                          task={task}
                          selected={selectedId === task.id}
                          onOpen={open}
                        />
                      ))
                    )}
                  </Stack>
                </ScrollArea.Autosize>
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

function findRoot(forest: TaskNode[], taskId: string): TaskNode | null {
  for (const node of forest) {
    if (subtreeIds(node).has(taskId)) return node
  }
  return null
}
