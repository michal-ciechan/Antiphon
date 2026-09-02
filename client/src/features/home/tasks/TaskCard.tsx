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
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
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
  type AgentTaskStatus,
} from '../../../api/agentTasks'
import { useSpawnCard, type CardStatus } from '../../../api/boards'
import { getApiErrorMessage } from '../../../api/client'
import type { HomeTaskItemDto } from '../../../api/homeTasks'
import { stateLabel } from '../../board/boardVisuals'
import { TierBadge } from '../../delegations/TaskChip'
import { formatCost, shortId, STATUS_COLOR } from '../../delegations/taskVisuals'
import { cardNumber } from '../../../shared/cardIdentifier'
import { isUnreadDeliverable } from '../taskReview'
import {
  HUMAN_REASON_LABEL,
  SOURCE_LABEL,
  STATE_COLOR,
  isAnswerable,
  isSpawnable,
  workerAgent,
} from './homeTasksModel'

const OPEN_WORKER: ReadonlySet<AgentTaskStatus> = new Set(['Queued', 'Dispatched', 'Working', 'Blocked'])
const SETTLED: ReadonlySet<string> = new Set(['Succeeded', 'Failed', 'Canceled'])

export function TaskCard({
  item,
  question = null,
  agents = [],
  onOpen,
  onOpenTask,
  onSelectAgent,
}: {
  item: HomeTaskItemDto
  question?: string | null
  agents?: AgentSummaryDto[]
  onOpen: () => void
  onOpenTask?: (taskId: string) => void
  onSelectAgent?: (agentId: string) => void
}) {
  const spawn = useSpawnCard(item.boardId ?? '')
  const retry = useRetryAgentTask()
  const escalate = useEscalateAgentTask()
  const cancel = useCancelAgentTask()

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
            </Group>
          </Group>

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
            onOpenTask={onOpenTask}
            onSelectAgent={onSelectAgent}
          />
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
            <Menu.Item component={Link} to={`/orchestrator?tab=delegations&task=${item.id}`}>
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
  onOpenTask,
  onSelectAgent,
}: {
  item: HomeTaskItemDto
  agent: AgentSummaryDto | null
  onOpenTask?: (taskId: string) => void
  onSelectAgent?: (agentId: string) => void
}) {
  const worker = item.worker!
  const live = agent?.liveSession?.status === 'Running'
  const settled = SETTLED.has(worker.status)
  const name = worker.agentName ?? worker.shortId

  if (settled) {
    const word = worker.status === 'Succeeded' ? 'done' : worker.status.toLowerCase()
    const ago = worker.completedAt ? ` ${formatRelativeAgo(worker.completedAt)}` : ''
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
    </Group>
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

function formatRelativeAgo(iso: string, now = Date.now()): string {
  const mins = Math.max(0, Math.floor((now - Date.parse(iso)) / 60_000))
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}
