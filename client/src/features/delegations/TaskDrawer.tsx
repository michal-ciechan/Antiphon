import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Code,
  Drawer,
  Group,
  Loader,
  Paper,
  ScrollArea,
  Stack,
  Text,
  Textarea,
  Timeline,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useRef, useState } from 'react'
import {
  TbAlertTriangle,
  TbArrowBigUpLine,
  TbFiles,
  TbPlayerStop,
  TbRefresh,
  TbSend,
  TbTerminal2,
} from 'react-icons/tb'
import { getApiErrorMessage } from '../../api/client'
import {
  useAgentTask,
  useCancelAgentTask,
  useEscalateAgentTask,
  useRetryAgentTask,
  useReplyToAgentTask,
  useMarkAgentTaskRead,
  type AgentTaskDetailDto,
} from '../../api/agentTasks'
import { RenderedMarkdown } from '../../shared/RenderedMarkdown'
import { SelectionComposer, SelectionDelegate } from '../agents/SelectionDelegate'
import { TierBadge } from './TaskChip'
import {
  STATUS_COLOR,
  WORKSPACE_LABEL,
  completionObserved,
  elapsedSeconds,
  formatCost,
  formatDuration,
  isLegacyCostEstimate,
  shortId,
  tierAlias,
  totalTokens,
} from './taskVisuals'

/**
 * Everything about one task, and the four things you can do to it. The drawer exists because the
 * chip is deliberately small: the brief, the delegate's untouched report and the event timeline are
 * what you need when a task went wrong, and none of them fit on a board.
 */
export function TaskDrawer({ taskId, onClose }: { taskId: string | null; onClose: () => void }) {
  const detail = useAgentTask(taskId)

  return (
    <Drawer
      opened={!!taskId}
      onClose={onClose}
      position="right"
      size="xl"
      title={detail.data ? <DrawerTitle detail={detail.data} /> : 'Task'}
    >
      {detail.isLoading && <Loader size="sm" />}
      {detail.data && <TaskDetail detail={detail.data} onClose={onClose} />}
    </Drawer>
  )
}

function DrawerTitle({ detail }: { detail: AgentTaskDetailDto }) {
  const { summary } = detail
  return (
    <Group gap="xs" wrap="nowrap">
      <Code>{shortId(summary.id)}</Code>
      <Text fw={600} lineClamp={1}>
        {summary.title}
      </Text>
      <Badge size="sm" variant="light" color={STATUS_COLOR[summary.status]}>
        {summary.status}
      </Badge>
    </Group>
  )
}

function TaskDetail({ detail, onClose }: { detail: AgentTaskDetailDto; onClose: () => void }) {
  const { summary } = detail
  const retry = useRetryAgentTask()
  const escalate = useEscalateAgentTask()
  const cancel = useCancelAgentTask()
  const reply = useReplyToAgentTask()
  const markRead = useMarkAgentTaskRead()
  const [answer, setAnswer] = useState('')
  const [selection, setSelection] = useState<string | null>(null)
  const stampedTask = useRef<string | null>(null)

  const running = summary.status === 'Dispatched' || summary.status === 'Working'
  const settled = summary.status === 'Succeeded' || summary.status === 'Failed' || summary.status === 'Canceled'
  const atTopTier = summary.modelLevel === 'Frontier'

  useEffect(() => {
    if (!settled || !detail.result || stampedTask.current === summary.id) return
    stampedTask.current = summary.id
    markRead.mutate(summary.id)
  }, [detail.result, markRead, settled, summary.id])

  const act = (
    label: string,
    run: (onDone: () => void) => void,
  ) =>
    run(() =>
      notifications.show({ color: 'green', message: `${label} — task ${shortId(summary.id)}` }),
    )

  const onError = (fallback: string) => (error: unknown) =>
    notifications.show({ color: 'red', message: getApiErrorMessage(error, fallback) })

  return (
    <Stack gap="md">
      <Group gap="xs">
        <TierBadge level={summary.modelLevel} kind={summary.agentKind} size="sm" />
        {summary.escalatedFrom && (
          <Badge size="sm" variant="light" color="warning" leftSection={<TbArrowBigUpLine size={12} />}>
            escalated from {tierAlias(summary.escalatedFrom, summary.agentKind)}
          </Badge>
        )}
        <Badge size="sm" variant="default">
          {summary.kind === 'Orchestrator' ? 'sub-orchestrator' : 'worker'}
        </Badge>
        <Badge size="sm" variant="default">
          {summary.role.toLowerCase()}
        </Badge>
        <Badge size="sm" variant="default">
          {WORKSPACE_LABEL[summary.workspace]}
        </Badge>
        {summary.attempt > 1 && (
          <Badge size="sm" variant="default">
            attempt {summary.attempt}
          </Badge>
        )}
      </Group>

      <Paper withBorder p="sm">
        <Group gap="xl" wrap="wrap">
          <Metric label="Agent" value={summary.agentName ?? '—'} />
          <Metric
            label="Elapsed"
            value={
              completionObserved(summary) ? (
                formatDuration(elapsedSeconds(summary))
              ) : (
                <Tooltip
                  label="recovered from an unbound session - completion was not observed; the delegate may have kept working"
                  withArrow
                >
                  <span>~{formatDuration(elapsedSeconds(summary))}</span>
                </Tooltip>
              )
            }
          />
          <Metric
            label={isLegacyCostEstimate(summary) ? 'Cost (legacy estimate)' : 'Cost'}
            value={formatCost(summary.costUsd)}
          />
          {summary.childCount > 0 && (
            <Metric label="Subtree" value={`${summary.childCount} children · ${formatCost(summary.subtreeCostUsd)}`} />
          )}
          <Metric label="Tokens" value={totalTokens(summary).toLocaleString()} />
        </Group>
        {isLegacyCostEstimate(summary) && (
          <Text size="xs" c="dimmed" mt="xs">
            Priced before the cache-read fix — cache reads were billed as fresh input against a stale
            rate table, so this figure is roughly 10x high. The run's cost ceiling still counts it.
          </Text>
        )}
        <Text size="xs" c="dimmed" mt="xs" style={{ wordBreak: 'break-all' }}>
          {summary.workingDirectory}
          {summary.scopeGlob ? ` · scope ${summary.scopeGlob}` : ''}
        </Text>
        {detail.mergeTargetRef && (
          <Text size="xs" c="dimmed">
            merges into {detail.mergeTargetRef}
          </Text>
        )}
      </Paper>

      {summary.agentId && (
        <Group gap="md">
          <Anchor href={`/agents?agent=${summary.agentId}`} size="sm">
            <Group gap={4} wrap="nowrap">
              <TbTerminal2 size={14} /> Transcript
            </Group>
          </Anchor>
          {!settled && (
            <Anchor href={`/agents/${summary.agentId}/files`} target="_blank" size="sm">
              <Group gap={4} wrap="nowrap">
                <TbFiles size={14} /> Files
              </Group>
            </Anchor>
          )}
        </Group>
      )}

      <Section title="Goal">
        <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
          {detail.goal}
        </Text>
      </Section>

      {detail.failureReason && (
        <Alert color="danger" icon={<TbAlertTriangle />} title="Failed">
          {detail.failureReason}
        </Alert>
      )}

      {detail.result && (
        <Section title={summary.status === 'Blocked' ? 'The delegate asked' : 'Report'}>
          <SelectionDelegate onCompose={setSelection}>
            <ScrollArea.Autosize mah={320}>
              <RenderedMarkdown>{detail.result}</RenderedMarkdown>
            </ScrollArea.Autosize>
          </SelectionDelegate>
          {selection && (
            <Box mt="sm">
              <SelectionComposer
                filePath={`task ${shortId(summary.id)} report`}
                workingDirectory={summary.workingDirectory}
                selection={selection}
                defaultRole="Docs"
                goalContext={`Re ${summary.title} (task ${shortId(summary.id)}):`}
                scopeGlob={null}
                onClose={() => setSelection(null)}
              />
            </Box>
          )}
          {detail.resultFilePath && (
            <Text size="xs" c="dimmed" mt="xs">
              Full detail: <Code>{detail.resultFilePath}</Code>
            </Text>
          )}
        </Section>
      )}

      {summary.status === 'Blocked' && (
        <Section title="Answer it">
          <Text size="xs" c="dimmed" mb="xs">
            Answer the question rather than taking the work back — the delegate keeps its context and
            carries on from where it stopped.
          </Text>
          <Textarea
            autosize
            minRows={2}
            placeholder="e.g. yes, accept negatives"
            value={answer}
            onChange={(event) => setAnswer(event.currentTarget.value)}
          />
          <Group justify="flex-end" mt="xs">
            <Button
              size="xs"
              leftSection={<TbSend size={14} />}
              loading={reply.isPending}
              disabled={!answer.trim()}
              onClick={() =>
                reply.mutate(
                  { id: summary.id, message: answer.trim() },
                  {
                    onSuccess: () => {
                      setAnswer('')
                      notifications.show({ color: 'green', message: 'Answer sent to the delegate' })
                    },
                    onError: onError('Could not deliver the answer'),
                  },
                )
              }
            >
              Send
            </Button>
          </Group>
        </Section>
      )}

      <Section title="Timeline">
        <Timeline active={detail.events.length - 1} bulletSize={12} lineWidth={1}>
          {detail.events.map((event, index) => (
            <Timeline.Item key={`${event.at}-${index}`} title={<Text size="sm">{event.type}</Text>}>
              <Text size="xs" c="dimmed">
                {event.detail}
              </Text>
              <Text size="xs" c="dimmed">
                {new Date(event.at).toLocaleString()}
              </Text>
            </Timeline.Item>
          ))}
        </Timeline>
      </Section>

      <Group justify="flex-end">
        <Tooltip label={settled ? 'Run it again at the same tier' : 'Stop the delegate and run it again'} withArrow>
          <Button
            variant="light"
            size="xs"
            leftSection={<TbRefresh size={14} />}
            loading={retry.isPending}
            disabled={summary.status === 'Queued'}
            onClick={() =>
              act('Retried', (done) =>
                retry.mutate(summary.id, { onSuccess: done, onError: onError('Retry failed') }),
              )
            }
          >
            Retry
          </Button>
        </Tooltip>
        <Tooltip
          label={atTopTier ? 'Already at the top of the ladder' : 'Run it again one tier up, carrying what this attempt found'}
          withArrow
        >
          <Button
            variant="light"
            color="violet"
            size="xs"
            leftSection={<TbArrowBigUpLine size={14} />}
            loading={escalate.isPending}
            disabled={atTopTier}
            onClick={() =>
              act('Escalated', (done) =>
                escalate.mutate(
                  { id: summary.id },
                  { onSuccess: done, onError: onError('Escalation failed') },
                ),
              )
            }
          >
            Escalate
          </Button>
        </Tooltip>
        <Button
          variant="light"
          color="danger"
          size="xs"
          leftSection={<TbPlayerStop size={14} />}
          loading={cancel.isPending}
          disabled={settled}
          onClick={() =>
            cancel.mutate(summary.id, {
              onSuccess: () => {
                notifications.show({
                  color: 'green',
                  message: running ? 'Delegate stopped' : `Task ${shortId(summary.id)} canceled`,
                })
                onClose()
              },
              onError: onError('Cancel failed'),
            })
          }
        >
          Cancel
        </Button>
      </Group>
    </Stack>
  )
}

function Metric({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <Box>
      <Text size="xs" c="dimmed" tt="uppercase" fw={700}>
        {label}
      </Text>
      <Text size="sm" style={{ fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </Text>
    </Box>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box>
      <Text size="xs" c="dimmed" tt="uppercase" fw={700} mb={4}>
        {title}
      </Text>
      <Paper withBorder p="sm">
        {children}
      </Paper>
    </Box>
  )
}
