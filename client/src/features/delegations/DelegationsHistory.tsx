import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  SegmentedControl,
  Stack,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useMemo, useRef, useState } from 'react'
import { TbAlertCircle, TbRefresh } from 'react-icons/tb'
import { useSearchParams } from 'react-router'
import {
  useAgentTaskListSummary,
  useAgentTasks,
  type AgentTaskSummaryDto,
} from '../../api/agentTasks'
import { isUnreadDeliverable } from '../home/taskReview'
import { formatClockTime } from '../home/workLineFormat'
import { TaskDrawer } from './TaskDrawer'
import { TierBadge } from './TaskChip'
import {
  SETTLED_STATUSES,
  STATUS_COLOR,
  completionObserved,
  elapsedSeconds,
  formatCost,
  formatDuration,
  isLegacyCostEstimate,
  isSettled,
  shortId,
} from './taskVisuals'

const ROW_HEIGHT = 36
const COLUMNS = '76px 92px minmax(0, 1.6fr) 72px 72px 110px 64px 72px 16px'
const VIEWPORT_HEIGHT = 620

type OutcomeFilter = 'all' | 'Succeeded' | 'Failed' | 'Canceled'

/**
 * The record of settled delegations: newest first, one row per task, virtualised. The board
 * answers "what is in flight"; this answers "what already finished".
 */
export function DelegationsHistory() {
  const [showAll, setShowAll] = useState(false)
  const [outcome, setOutcome] = useState<OutcomeFilter>('all')
  const tasks = useAgentTasks(false, {
    since: showAll ? undefined : 'default',
    status: SETTLED_STATUSES,
  })
  const summary = useAgentTaskListSummary()
  const [searchParams] = useSearchParams()
  const [drawerId, setDrawerId] = useState<string | null>(searchParams.get('task'))

  const settled = useMemo(() => {
    const rows = (tasks.data ?? []).filter((task) => isSettled(task.status))
    rows.sort(
      (a, b) =>
        Date.parse(b.completedAt ?? b.createdAt) - Date.parse(a.completedAt ?? a.createdAt),
    )
    return rows
  }, [tasks.data])

  const byId = useMemo(() => new Map(settled.map((task) => [task.id, task])), [settled])

  const visible = useMemo(
    () => (outcome === 'all' ? settled : settled.filter((task) => task.status === outcome)),
    [settled, outcome],
  )

  const totals = summary.data
  const runs = totals?.runs ?? 0

  const open = (task: AgentTaskSummaryDto | string) => {
    setDrawerId(typeof task === 'string' ? task : task.id)
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
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading settled tasks">
        {tasks.error instanceof Error ? tasks.error.message : 'No tasks returned.'}
      </Alert>
    )
  }

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Group gap="xs">
          <Title order={4}>History</Title>
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
          <Badge variant="light" color="gray">
            {visible.length} settled
          </Badge>
        </Group>
        <Group gap="xs">
          <SegmentedControl
            size="xs"
            value={outcome}
            onChange={(value) => setOutcome(value as OutcomeFilter)}
            data={[
              { label: 'All', value: 'all' },
              { label: 'Succeeded', value: 'Succeeded' },
              { label: 'Failed', value: 'Failed' },
              { label: 'Canceled', value: 'Canceled' },
            ]}
          />
          <Button size="xs" variant="default" onClick={() => setShowAll((current) => !current)}>
            {showAll ? 'Last 7 days' : 'Show all'}
          </Button>
          <Tooltip label="Refresh">
            <ActionIcon variant="subtle" onClick={() => tasks.refetch()} loading={tasks.isFetching}>
              <TbRefresh />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {visible.length === 0 ? (
        <HistoryEmpty runs={runs} showAll={showAll} onShowAll={() => setShowAll(true)} />
      ) : (
        <HistoryTable rows={visible} byId={byId} selectedId={drawerId} onOpen={open} />
      )}

      <TaskDrawer taskId={drawerId} onClose={() => setDrawerId(null)} />
    </Stack>
  )
}

function HistoryEmpty({
  runs,
  showAll,
  onShowAll,
}: {
  runs: number
  showAll: boolean
  onShowAll: () => void
}) {
  if (runs === 0) {
    return (
      <Text size="sm" c="dimmed" p="sm">
        No delegated tasks yet. Start one with “New task”, or let an orchestrator delegate with the
        antiphon-delegate skill.
      </Text>
    )
  }
  if (!showAll) {
    return (
      <Text size="sm" c="dimmed" p="sm">
        Nothing settled in the last 7 days —{' '}
        <Anchor component="button" type="button" onClick={onShowAll}>
          Show all
        </Anchor>
      </Text>
    )
  }
  return (
    <Text size="sm" c="dimmed" p="sm">
      Nothing settled matches this filter.
    </Text>
  )
}

function HistoryTable({
  rows,
  byId,
  selectedId,
  onOpen,
}: {
  rows: AgentTaskSummaryDto[]
  byId: Map<string, AgentTaskSummaryDto>
  selectedId: string | null
  onOpen: (task: AgentTaskSummaryDto | string) => void
}) {
  const scrollRef = useRef<HTMLDivElement>(null)
  // TanStack Virtual returns imperative scroll/measurement functions; this component deliberately
  // owns them and does not memoize or pass the virtualizer object across a memoization boundary.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
    initialRect: { width: 0, height: VIEWPORT_HEIGHT },
  })
  const virtualItems = virtualizer.getVirtualItems()
  const painted =
    virtualItems.length > 0
      ? virtualItems
      : rows.slice(0, 20).map((_, index) => ({ index, start: index * ROW_HEIGHT }))

  return (
    <Box ref={scrollRef} style={{ height: VIEWPORT_HEIGHT, overflowY: 'auto' }}>
      <Box
        style={{
          height: virtualizer.getTotalSize() || rows.length * ROW_HEIGHT,
          position: 'relative',
        }}
      >
        {painted.map((row) => {
          const task = rows[row.index]
          return (
            <Box
              key={task.id}
              data-index={row.index}
              ref={virtualizer.measureElement}
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                width: '100%',
                transform: `translateY(${row.start}px)`,
              }}
            >
              <HistoryRow
                task={task}
                rootTitle={rootTitleOf(task, byId)}
                selected={selectedId === task.id}
                onOpen={onOpen}
              />
            </Box>
          )
        })}
      </Box>
    </Box>
  )
}

function HistoryRow({
  task,
  rootTitle,
  selected,
  onOpen,
}: {
  task: AgentTaskSummaryDto
  rootTitle: string | null
  selected: boolean
  onOpen: (task: AgentTaskSummaryDto | string) => void
}) {
  const observed = completionObserved(task)
  const legacy = isLegacyCostEstimate(task)
  const unread = isUnreadDeliverable(task)
  const settledAt = task.completedAt ?? task.createdAt

  return (
    <UnstyledButton
      onClick={() => onOpen(task)}
      data-testid={`history-row-${shortId(task.id)}`}
      aria-pressed={selected}
      style={{
        display: 'grid',
        gridTemplateColumns: COLUMNS,
        alignItems: 'center',
        columnGap: 8,
        height: ROW_HEIGHT,
        width: '100%',
        padding: '0 8px',
        backgroundColor: selected ? 'var(--mantine-color-dark-6)' : undefined,
      }}
    >
      <Tooltip label={settledAt} withArrow>
        <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatSettledAt(settledAt)}
        </Text>
      </Tooltip>
      <Badge size="xs" variant="light" color={STATUS_COLOR[task.status]}>
        {task.status}
      </Badge>
      <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
        {unread && (
          <Box
            component="span"
            c="violet"
            aria-label="Unread"
            data-testid={`history-unread-${task.id}`}
            style={{ flexShrink: 0 }}
          >
            ●
          </Box>
        )}
        <Text size="sm" truncate style={{ minWidth: 0 }}>
          {task.title}
        </Text>
        {task.cardIdentifier && (
          <Badge size="xs" variant="default" style={{ flexShrink: 0 }}>
            {task.cardIdentifier}
          </Badge>
        )}
        {task.parentTaskId && (
          <Text size="xs" c="dimmed" truncate style={{ flexShrink: 1, minWidth: 0 }}>
            ↳{rootTitle ? ` ${rootTitle}` : ''}
          </Text>
        )}
      </Group>
      <Text size="xs" c="dimmed" truncate>
        {task.role}
      </Text>
      <Box>
        <TierBadge level={task.modelLevel} kind={task.agentKind} />
      </Box>
      <Text size="xs" c="dimmed" truncate>
        {task.agentName ?? shortId(task.id)}
      </Text>
      {observed ? (
        <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatDuration(elapsedSeconds(task))}
        </Text>
      ) : (
        <Tooltip
          label="recovered from an unbound session - completion was not observed; the delegate may have kept working"
          withArrow
        >
          <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
            ~{formatDuration(elapsedSeconds(task))}
          </Text>
        </Tooltip>
      )}
      {legacy ? (
        <Tooltip label="legacy estimate — priced before the cache-read fix" withArrow>
          <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
            {formatCost(task.costUsd)}~
          </Text>
        </Tooltip>
      ) : (
        <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatCost(task.costUsd)}
        </Text>
      )}
    </UnstyledButton>
  )
}

function rootTitleOf(task: AgentTaskSummaryDto, byId: Map<string, AgentTaskSummaryDto>): string | null {
  if (!task.parentTaskId) return null
  return byId.get(task.rootTaskId)?.title ?? null
}

function formatSettledAt(iso: string, now = new Date()): string {
  const date = new Date(iso)
  if (
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate()
  ) {
    return formatClockTime(iso)
  }
  return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
}
