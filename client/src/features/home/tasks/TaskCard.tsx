import {
  ActionIcon,
  Anchor,
  Badge,
  Box,
  Group,
  Loader,
  Menu,
  Paper,
  Text,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import {
  TbDotsVertical,
  TbTerminal2,
} from 'react-icons/tb'
import { Link } from 'react-router'
import type { AgentSummaryDto } from '../../../api/agents'
import {
  useCancelAgentTask,
  useEscalateAgentTask,
  useRetryAgentTask,
  type AgentTaskPipelineDto,
  type AgentTaskStatus,
} from '../../../api/agentTasks'
import type { AttentionItemDto } from '../../../api/attention'
import { useSpawnCard, type CardStatus } from '../../../api/boards'
import { getApiErrorMessage } from '../../../api/client'
import type { HomeTaskItemDto } from '../../../api/homeTasks'
import { ATTENTION_VISUALS } from '../../attention/attentionVisuals'
import { importanceBadgeColor, stateLabel, urgencyBadgeColor } from '../../board/boardVisuals'
import { TierBadge } from '../../delegations/TaskChip'
import { formatCost, shortId, STATUS_COLOR } from '../../delegations/taskVisuals'
import { cardNumber } from '../../../shared/cardIdentifier'
import { isUnreadDeliverable } from '../taskReview'
import {
  HUMAN_REASON_LABEL,
  SOURCE_LABEL,
  STATE_COLOR,
  formatElapsed,
  formatRelativeAgo,
  isAnswerable,
  isSpawnable,
  queueReasonFor,
  readyLine,
  readinessFor,
  runningSince,
  workerAgent,
  type HomeTaskPipelineRow,
} from './homeTasksModel'

const OPEN_WORKER: ReadonlySet<AgentTaskStatus> = new Set(['Queued', 'Dispatched', 'Working', 'Blocked'])
const SETTLED: ReadonlySet<string> = new Set(['Succeeded', 'Failed', 'Canceled'])

export function TaskCard({
  item,
  question = null,
  agents = [],
  liveness = null,
  pipelineRow = null,
  pipeline = null,
  now: nowProp = null,
  onOpen,
  onOpenTask,
  onSelectAgent,
}: {
  item: HomeTaskItemDto
  question?: string | null
  agents?: AgentSummaryDto[]
  liveness?: AttentionItemDto | null
  pipelineRow?: HomeTaskPipelineRow | null
  pipeline?: AgentTaskPipelineDto | null
  now?: number | null
  onOpen: () => void
  onOpenTask?: (taskId: string) => void
  onSelectAgent?: (agentId: string) => void
}) {
  const spawn = useSpawnCard(item.boardId ?? '')
  const retry = useRetryAgentTask()
  const escalate = useEscalateAgentTask()
  const cancel = useCancelAgentTask()
  // Callers that care about time pass `now`; otherwise the card's clock is its mount time. A
  // `Date.now()` default parameter is an impure call during render (react-hooks/purity, CARD-0378).
  const [mountedAt] = useState(() => Date.now())
  const now = nowProp ?? mountedAt

  const reason = item.humanReason
  const borderColor =
    reason === 'Decision' || reason === 'Question'
      ? 'danger'
      : reason === 'Gate' || reason === 'Review'
        ? 'warning'
        : undefined
  const stateText =
    item.source === 'Card' ? stateLabel(item.state as CardStatus) : item.state
  const stateColor = STATE_COLOR[item.state as CardStatus | AgentTaskStatus] ?? 'gray'
  const unread = isHomeUnread(item)
  const readTarget = unread && item.deliverablePath
    ? `/plans?${new URLSearchParams({
        file: item.deliverablePath,
        ...(item.deliverableRef ? { ref: item.deliverableRef } : {}),
        task: item.id,
      }).toString()}`
    : null
  const threadNumber = item.source === 'Card' ? cardNumber(item.identifier) : null
  const spawnable = isSpawnable(item)
  const answerable = isAnswerable(item)
  const answerTaskId = item.source === 'Delegation' ? item.id : item.worker?.taskId
  const settled = SETTLED.has(item.state)
  const atTopTier = item.modelLevel === 'Frontier'
  const agent = workerAgent(item, agents)
  const running = item.group === 'Running'
  const headerLiveness = running && item.source === 'Delegation' ? liveness : null
  const workerLiveness = running && item.worker ? liveness : null
  const elapsed = runningElapsed(item, pipelineRow, now)
  const queue = item.group === 'Next' ? queueReasonFor(item, pipeline) : null
  const ready = item.group === 'Next' ? readinessFor(item, pipeline) : null
  const terminalLine =
    item.group === 'Done' && item.source === 'Card' ? firstNonBlankLine(item.terminalReason) : null

  const onError = (fallback: string) => (error: unknown) =>
    notifications.show({ color: 'red', message: getApiErrorMessage(error, fallback) })

  return (
    <Box pos="relative">
      <Paper
        withBorder
        p="xs"
        radius="sm"
        style={{
          borderLeft: borderColor
            ? `3px solid var(--mantine-color-${borderColor}-5)`
            : undefined,
        }}
      >
        <UnstyledButton
          onClick={onOpen}
          aria-label={`Open ${item.identifier}`}
          style={{ display: 'block', width: '100%', textAlign: 'left' }}
        >
          <Group gap={6} wrap="nowrap" pr={28} justify="space-between" align="flex-start">
            <Group gap={6} wrap="nowrap" style={{ minWidth: 0, flex: 1 }}>
              <Text size="xs" c="dimmed" ff="monospace" style={{ flexShrink: 0 }}>
                {item.identifier}
              </Text>
              <Badge
                size="xs"
                variant="outline"
                color={item.source === 'Card' ? 'active' : 'violet'}
              >
                {SOURCE_LABEL[item.source]}
              </Badge>
              {item.importance && item.importance !== 'Normal' && (
                <Badge size="xs" variant="light" color={importanceBadgeColor(item.importance)}>
                  {item.importance}
                </Badge>
              )}
              {item.effectiveUrgency && item.effectiveUrgency !== 'Normal' && (
                <Badge size="xs" variant="light" color={urgencyBadgeColor(item.effectiveUrgency)}>
                  {item.effectiveUrgency.toLowerCase()}
                </Badge>
              )}
            </Group>
            <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
              <Badge size="xs" variant="light" color={stateColor}>
                {stateText}
              </Badge>
              {reason && (
                <Badge
                  size="xs"
                  variant="light"
                  color={borderColor ?? 'gray'}
                >
                  {HUMAN_REASON_LABEL[reason]}
                </Badge>
              )}
              {headerLiveness && <LivenessBadge item={headerLiveness} />}
            </Group>
          </Group>

          {item.urgentSince && item.effectiveUrgency && item.effectiveUrgency !== 'Normal' && (
            <Text size="xs" c="dimmed" mt={4}>
              rated {item.effectiveUrgency} {formatRelativeAgo(item.urgentSince, now)}
            </Text>
          )}
          <Text size="sm" fw={600} lineClamp={2} mt={6}>
            {unread && (
              <Box
                component="span"
                c="violet"
                mr={6}
                aria-label="Unread"
                data-testid={`task-unread-${item.id}`}
              >
                ●
              </Box>
            )}
            {item.title}
          </Text>
        </UnstyledButton>

        {item.source === 'Card' && item.stage && (
          <Text size="xs" c="dimmed" mt={4}>
            stage: {item.stage.toLowerCase()}
          </Text>
        )}

        {item.source === 'Delegation' && (
          <Group gap={6} mt={4} wrap="wrap">
            {item.modelLevel && (
              <TierBadge level={item.modelLevel} kind={item.agentKind ?? 'ClaudeCode'} />
            )}
            {item.role && (
              <Badge size="xs" variant="default">
                {item.role.toLowerCase()}
              </Badge>
            )}
            {item.costUsd != null && (
              <Text size="xs" c="dimmed" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {formatCost(item.costUsd)}
              </Text>
            )}
          </Group>
        )}

        {question && (
          <Text size="xs" lineClamp={1} mt={4} fs="italic">
            {question}
          </Text>
        )}

        {unread && readTarget && (
          <Group mt={4}>
            <Anchor
              component={Link}
              to={readTarget}
              size="xs"
              onClick={(event) => event.stopPropagation()}
              data-testid={`task-read-${item.id}`}
            >
              Read
            </Anchor>
          </Group>
        )}

        {item.worker && (
          <WorkerLine
            item={item}
            agent={agent}
            liveness={workerLiveness}
            now={now}
            onOpenTask={onOpenTask}
            onSelectAgent={onSelectAgent}
          />
        )}

        {elapsed && (
          <Text size="xs" c="dimmed" mt={4} data-testid={`task-elapsed-${item.id}`}>
            {elapsed}
          </Text>
        )}

        {queue && (
          <Text size="xs" c="dimmed" mt={4} data-testid={`task-queue-${item.id}`}>
            {queue.line}
          </Text>
        )}

        {ready && (
          <Group mt={4} gap={6} wrap="nowrap">
            <Text size="xs" c="dimmed" data-testid={`task-ready-${item.id}`}>
              {readyLine(ready, now)}
            </Text>
            {ready.pinChip ? (
              <Text size="xs" c="dimmed" data-testid={`task-ready-pin-${item.id}`}>
                {ready.pinChip}
              </Text>
            ) : null}
            {ready.deliverablePath ? (
              <Anchor
                component={Link}
                to={`/plans?${new URLSearchParams({
                  file: ready.deliverablePath,
                  ...(ready.deliverableRef ? { ref: ready.deliverableRef } : {}),
                  task: ready.sourcePlanTaskId,
                }).toString()}`}
                size="xs"
                onClick={(event) => event.stopPropagation()}
                data-testid={`task-ready-read-${item.id}`}
              >
                Read
              </Anchor>
            ) : null}
          </Group>
        )}

        {terminalLine && (
          <Text size="xs" c="dimmed" lineClamp={1} mt={4} data-testid={`task-terminal-${item.id}`}>
            {terminalLine}
          </Text>
        )}
      </Paper>

      <Menu shadow="md" position="bottom-end" withinPortal>
        <Menu.Target>
          <ActionIcon
            variant="subtle"
            color="gray"
            aria-label={`Task menu ${item.identifier}`}
            pos="absolute"
            top={8}
            right={8}
          >
            <TbDotsVertical size={16} />
          </ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item onClick={onOpen}>Open</Menu.Item>
          {threadNumber != null && (
            <Menu.Item component={Link} to={`/thread/card-${threadNumber}`}>
              Open thread
            </Menu.Item>
          )}
          {item.source === 'Card' && item.boardId && (
            <Menu.Item component={Link} to={`/boards/${item.boardId}?card=${item.id}`}>
              Open board
            </Menu.Item>
          )}
          {item.source === 'Delegation' && (
            <Menu.Item
              component={Link}
              to={
                item.group === 'Done'
                  ? `/orchestrator?tab=history&task=${item.id}`
                  : `/orchestrator?tab=delegations&task=${item.id}`
              }
            >
              Open delegations
            </Menu.Item>
          )}
          {item.worker && (
            <Menu.Item onClick={() => onOpenTask?.(item.worker!.taskId)}>Open delegation</Menu.Item>
          )}
          {answerable && answerTaskId && (
            <Menu.Item onClick={() => onOpenTask?.(answerTaskId)}>Answer…</Menu.Item>
          )}
          {spawnable && (
            <Menu.Item
              onClick={() =>
                spawn.mutate(
                  { cardId: item.id, request: { cols: 120, rows: 30 } },
                  {
                    onSuccess: () =>
                      notifications.show({ color: 'green', message: `Spawned ${item.identifier}` }),
                    onError: onError('Spawn failed'),
                  },
                )
              }
            >
              Spawn session
            </Menu.Item>
          )}
          {item.source === 'Delegation' && (
            <>
              <Menu.Divider />
              <Menu.Item
                disabled={item.state === 'Queued'}
                onClick={() =>
                  retry.mutate(item.id, {
                    onSuccess: () =>
                      notifications.show({ color: 'green', message: `Retried — task ${shortId(item.id)}` }),
                    onError: onError('Retry failed'),
                  })
                }
              >
                Retry
              </Menu.Item>
              <Menu.Item
                disabled={atTopTier}
                onClick={() =>
                  escalate.mutate(
                    { id: item.id },
                    {
                      onSuccess: () =>
                        notifications.show({ color: 'green', message: `Escalated — task ${shortId(item.id)}` }),
                      onError: onError('Escalation failed'),
                    },
                  )
                }
              >
                Escalate
              </Menu.Item>
              <Menu.Item
                disabled={settled}
                color="danger"
                onClick={() =>
                  cancel.mutate(item.id, {
                    onSuccess: () =>
                      notifications.show({
                        color: 'green',
                        message: OPEN_WORKER.has(item.state as AgentTaskStatus)
                          ? 'Delegate stopped'
                          : `Task ${shortId(item.id)} canceled`,
                      }),
                    onError: onError('Cancel failed'),
                  })
                }
              >
                Cancel
              </Menu.Item>
            </>
          )}
        </Menu.Dropdown>
      </Menu>
    </Box>
  )
}

function WorkerLine({
  item,
  agent,
  liveness,
  now,
  onOpenTask,
  onSelectAgent,
}: {
  item: HomeTaskItemDto
  agent: AgentSummaryDto | null
  liveness: AttentionItemDto | null
  now: number
  onOpenTask?: (taskId: string) => void
  onSelectAgent?: (agentId: string) => void
}) {
  const worker = item.worker!
  const live = agent?.liveSession?.status === 'Running'
  const settled = SETTLED.has(worker.status)
  const name = worker.agentName ?? worker.shortId

  if (settled) {
    const word = worker.status === 'Succeeded' ? 'done' : worker.status.toLowerCase()
    const ago = worker.completedAt ? ` ${formatRelativeAgo(worker.completedAt, now)}` : ''
    return (
      <Text
        size="xs"
        c="dimmed"
        mt={6}
        onClick={(event) => {
          event.stopPropagation()
          onOpenTask?.(worker.taskId)
        }}
        style={{ cursor: 'pointer' }}
      >
        {worker.role.toLowerCase()} · {word}{ago}
      </Text>
    )
  }

  return (
    <Group
      gap={6}
      wrap="nowrap"
      mt={6}
      role="button"
      tabIndex={0}
      aria-label={`Open delegation ${worker.shortId}`}
      onClick={(event) => {
        event.stopPropagation()
        onOpenTask?.(worker.taskId)
      }}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onOpenTask?.(worker.taskId)
        }
      }}
      style={{ cursor: 'pointer' }}
    >
      <TbTerminal2
        size={14}
        color={live ? 'var(--mantine-color-success-5)' : 'var(--mantine-color-dimmed)'}
        style={{ flexShrink: 0 }}
      />
      {agent && onSelectAgent ? (
        <Text
          size="xs"
          component="span"
          onClick={(event) => {
            event.stopPropagation()
            onSelectAgent(agent.id)
          }}
          style={{ cursor: 'pointer' }}
        >
          {name}
        </Text>
      ) : (
        <Text size="xs" truncate>
          {name}
        </Text>
      )}
      <Badge size="xs" variant="default">
        {worker.role.toLowerCase()}
      </Badge>
      {agent?.working ? (
        <Badge
          size="xs"
          color="yellow"
          variant="light"
          leftSection={<Loader size={8} color="yellow" type="dots" />}
        >
          Working
        </Badge>
      ) : (
        <Badge size="xs" variant="light" color={STATUS_COLOR[worker.status]}>
          {worker.status}
        </Badge>
      )}
      {liveness && <LivenessBadge item={liveness} />}
    </Group>
  )
}

function LivenessBadge({ item }: { item: AttentionItemDto }) {
  const visual = ATTENTION_VISUALS[item.kind]
  const Icon = visual.icon
  return (
    <Tooltip label={visual.hint} multiline w={280}>
      <Badge
        size="xs"
        variant="light"
        color={visual.color}
        leftSection={<Icon size={12} />}
        data-testid={`task-liveness-${item.kind}`}
      >
        {visual.label}
      </Badge>
    </Tooltip>
  )
}

function runningElapsed(
  item: HomeTaskItemDto,
  pipelineRow: HomeTaskPipelineRow | null,
  now: number,
): string | null {
  const openWorker = item.worker != null && OPEN_WORKER.has(item.worker.status)
  const runningDelegation = item.source === 'Delegation' && item.group === 'Running'
  if (!openWorker && !runningDelegation) return null
  const since = runningSince(item)
  if (!since) return null
  const elapsed = formatElapsed(since, now)
  const lastActivityAt =
    pipelineRow && 'lastActivityAt' in pipelineRow ? pipelineRow.lastActivityAt : null
  if (lastActivityAt) return `${elapsed} · active ${formatRelativeAgo(lastActivityAt, now)}`
  return elapsed
}

function firstNonBlankLine(text: string | null | undefined): string | null {
  if (!text) return null
  return (
    text
      .split(/\r?\n/)
      .map((line) => line.trim())
      .find((line) => line.length > 0) ?? null
  )
}

function isHomeUnread(item: HomeTaskItemDto): boolean {
  if (item.source !== 'Delegation') return false
  return isUnreadDeliverable({
    status: item.state as AgentTaskStatus,
    role: item.role ?? 'Custom',
    readAt: item.readAt,
    completedAt: item.completedAt,
  })
}


