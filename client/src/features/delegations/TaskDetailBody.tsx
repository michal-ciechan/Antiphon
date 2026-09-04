import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Code,
  Group,
  Loader,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Text,
  Timeline,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  TbAlertTriangle,
  TbArrowBigUpLine,
  TbFiles,
  TbPlayerStop,
  TbRefresh,
  TbTerminal2,
} from 'react-icons/tb'
import { getApiErrorMessage } from '../../api/client'
import type { AgentModelLevel } from '../../api/agents'
import {
  useAgentTask,
  useCancelAgentTask,
  useEscalateAgentTask,
  useRetryAgentTask,
  useMarkAgentTaskRead,
  useRerouteAgentTask,
  type AgentTaskDetailDto,
  type PipelineHandoffKind,
} from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
import { useModelAvailability } from '../../api/modelAvailability'
import { RenderedMarkdown } from '../../shared/RenderedMarkdown'
import { SelectionComposer, SelectionDelegate } from '../agents/SelectionDelegate'
import { TierBadge } from './TaskChip'
import { BlockedQuestionCard } from './BlockedQuestionCard'
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
 * The task body shared by the drawer and the home-rail modal. Fetch, loading, Retry / Escalate /
 * Cancel / Answer and the read-stamp effect live here so the two shells cannot drift.
 */
export function TaskDetailBody({ taskId, onClose }: { taskId: string | null; onClose: () => void }) {
  const detail = useAgentTask(taskId)

  return (
    <>
      {detail.isLoading && <Loader size="sm" />}
      {detail.data && <TaskDetail detail={detail.data} onClose={onClose} />}
    </>
  )
}

export function TaskDetailTitle({ detail }: { detail: AgentTaskDetailDto }) {
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
  const reroute = useRerouteAgentTask()
  const availability = useModelAvailability()
  const markRead = useMarkAgentTaskRead()
  const [selection, setSelection] = useState<string | null>(null)
  const [rerouteKind, setRerouteKind] = useState<string | null>('Grok')
  const [rerouteLevel, setRerouteLevel] = useState<string | null>('Frontier')
  const [expandedEvent, setExpandedEvent] = useState<number | null>(null)
  const stampedTask = useRef<string | null>(null)
  const wasBlocked = useRef(Boolean(detail.blocked))
  if (detail.blocked) wasBlocked.current = true
  const answeredElsewhere =
    wasBlocked.current && !detail.blocked && (summary.status === 'Working' || summary.status === 'Dispatched')

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
        {summary.complexity && (
          <Badge size="sm" variant="light" color="violet">
            {summary.complexity}
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

      {detail.blocked && (
        <BlockedQuestionCard detail={detail} variant="full" autoFocus />
      )}
      {answeredElsewhere && (
        <Text size="sm" c="dimmed" data-testid="blocked-answered-elsewhere">
          {answeredViaLine(detail.events)}
        </Text>
      )}

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
          {summary.scope ? ` · scope ${summary.scope}` : ''}
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

      {!detail.blocked && (
        <Section title="Goal">
          <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
            {detail.goal}
          </Text>
        </Section>
      )}

      {summary.status === 'Failed' && detail.failureReason && (
        <Alert color="danger" icon={<TbAlertTriangle />} title="Failed">
          {detail.failureReason}
        </Alert>
      )}

      {detail.result && summary.status !== 'Blocked' && (
        <Section title="Report">
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
                scope={null}
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

      {(detail.deliverablePath || detail.nextStage || detail.nextHandoff) && (
        <Section title="Handoff">
          {detail.deliverablePath && (
            <Text size="sm" data-testid="task-deliverable">
              deliverable: <Code>{detail.deliverablePath}</Code>
              {detail.deliverableRef ? ` @ ${detail.deliverableRef}` : ''}
            </Text>
          )}
          {detail.nextStage && (
            <Text size="sm" data-testid="task-next-stage">
              next: {nextStageToken(detail.nextStage)}
            </Text>
          )}
          {detail.nextHandoff && (
            <Text size="sm" data-testid="task-next-handoff">
              handoff: {detail.nextHandoff}
            </Text>
          )}
        </Section>
      )}

      <Section title="Timeline">
        <Timeline active={detail.events.length - 1} bulletSize={12} lineWidth={1}>
          {detail.events.map((event, index) => (
            <Timeline.Item key={`${event.at}-${index}`} title={<Text size="sm">{event.type}</Text>}>
              <Text
                size="xs"
                c="dimmed"
                lineClamp={expandedEvent === index ? undefined : 3}
                style={{ whiteSpace: 'pre-wrap' }}
              >
                {event.detail}
              </Text>
              {event.detail.length > 180 && (
                <Button
                  size="compact-xs"
                  variant="subtle"
                  px={0}
                  onClick={() => setExpandedEvent((open) => (open === index ? null : index))}
                >
                  {expandedEvent === index ? 'show less' : 'show all'}
                </Button>
              )}
              <Text size="xs" c="dimmed">
                {new Date(event.at).toLocaleString()}
              </Text>
            </Timeline.Item>
          ))}
        </Timeline>
      </Section>

      {summary.status === 'Blocked' && summary.complexity && detail.failureReason?.startsWith('routing exhausted:') && (
        <Paper withBorder p="sm" data-testid="task-reroute">
          <Stack gap="xs">
            <Text size="sm" fw={600}>
              Reroute
            </Text>
            <Text size="xs" c="dimmed">
              Explicit kind/level. Ends chain governance. Held aliases 409.
            </Text>
            <Group>
              <Select
                size="xs"
                label="Kind"
                data={['ClaudeCode', 'Grok', 'Codex']}
                value={rerouteKind}
                onChange={setRerouteKind}
              />
              <Select
                size="xs"
                label="Level"
                data={['Frontier', 'High', 'Medium', 'Low']}
                value={rerouteLevel}
                onChange={setRerouteLevel}
              />
              <Button
                size="xs"
                mt="lg"
                loading={reroute.isPending}
                disabled={!rerouteKind || !rerouteLevel}
                onClick={() =>
                  reroute.mutate(
                    {
                      id: summary.id,
                      agentKind: rerouteKind as AgentKind,
                      modelLevel: rerouteLevel as AgentModelLevel,
                    },
                    { onSuccess: () => notifications.show({ color: 'green', message: 'Rerouted' }), onError: onError('Reroute failed') },
                  )
                }
              >
                Reroute
              </Button>
            </Group>
            {availability.data && (
              <Text size="xs" c="dimmed">
                available: {availability.data.available.join(', ') || '(none)'}
              </Text>
            )}
          </Stack>
        </Paper>
      )}

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

const NEXT_STAGE_TOKEN: Record<PipelineHandoffKind, string> = {
  Investigate: 'investigate',
  Plan: 'plan',
  TestDesign: 'test-design',
  Code: 'code',
  Review: 'review',
  Land: 'land',
  Decide: 'decide',
  None: 'none',
}

function nextStageToken(kind: PipelineHandoffKind): string {
  return NEXT_STAGE_TOKEN[kind] ?? kind.toLowerCase()
}

function Metric({ label, value }: { label: string; value: ReactNode }) {
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

function answeredViaLine(events: AgentTaskDetailDto['events']): string {
  const replied = [...events].reverse().find((event) => event.type === 'Replied')
  if (!replied) return 'Answered — the delegate is working'
  const at = new Date(replied.at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const via = /Answered via (\w+)/.exec(replied.detail)?.[1]
  return via
    ? `Answered via ${via} at ${at} — the delegate is working`
    : `Answered at ${at} — the delegate is working`
}

function Section({ title, children }: { title: string; children: ReactNode }) {
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
