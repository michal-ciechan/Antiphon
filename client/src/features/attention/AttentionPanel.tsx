import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Center,
  Code,
  Collapse,
  Group,
  Loader,
  Modal,
  Paper,
  Stack,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { TbAlertCircle, TbChecks, TbChevronDown, TbChevronRight, TbRefresh } from 'react-icons/tb'
import { useNavigate } from 'react-router'
import {
  useCancelAgentTask,
  useEscalateAgentTask,
  useRetryAgentTask,
} from '../../api/agentTasks'
import {
  attentionKeys,
  useAttention,
  type AttentionAction,
  type AttentionItemDto,
} from '../../api/attention'
import { getApiErrorMessage } from '../../api/client'
import { useClearModelAvailabilityHold } from '../../api/modelAvailability'
import { cancelQueuedMessage, sendQueuedMessageNow, stopSession } from '../../api/sessions'
import { formatCost, formatDuration } from '../delegations/taskVisuals'
import { BlockedReplyRow } from './BlockedReplyRow'
import {
  ATTENTION_GROUPS,
  ATTENTION_VISUALS,
  ageSeconds,
  groupOf,
  keyOf,
  targetOf,
  type AttentionGroupKey,
} from './attentionVisuals'

/**
 * "Across everything, what is stuck — and why." The diagnostic tab (CARD-0035 §D2).
 *
 * <p><b>Empty is the design target, not a failure.</b> On a healthy day this panel says "Nothing is
 * stuck." and that has to read as reassurance: a blank box or an error-shaped placeholder would
 * teach the operator that the view is broken, and a view nobody believes is worse than no view. The
 * rows it does show are all server-computed — nothing here decides that something is stuck, it only
 * decides how a decision already made reads.</p>
 *
 * <p>Every row's click goes to the thing that explains it — the task drawer on the sibling tab, or
 * the agent's incident drawer. That is the whole reason this lives on the Orchestrator page.</p>
 *
 * <p><b>The actions (slice 4) are what make it diagnostic rather than a list</b> — and they are also
 * the standing temptation to widen it. A page with verbs on it wants to feel useful, and the way to
 * make a page feel useful is to put more rows on it; that is exactly how a diagnostic view becomes
 * something a human learns to skim, at which point the one real row arrives among nine false ones and
 * the view is worse than nothing. Nothing here decides what is listed. The verbs are the ones the
 * server named per row, all of which already existed as endpoints.</p>
 */
export function AttentionPanel() {
  const attention = useAttention()
  const navigate = useNavigate()
  const [collapsed, setCollapsed] = useState<Set<AttentionGroupKey>>(
    () => new Set(ATTENTION_GROUPS.filter((g) => g.collapsed).map((g) => g.key)),
  )

  const items = useMemo(() => attention.data?.items ?? [], [attention.data])

  const grouped = useMemo(() => {
    const byGroup = new Map<AttentionGroupKey, AttentionItemDto[]>()
    for (const group of ATTENTION_GROUPS) byGroup.set(group.key, [])
    // Order is the SERVER's — severity desc, then oldest-stuck first — and the groups are read in
    // the same order, so re-sorting here could only ever disagree with the rank it already assigned.
    for (const item of items) byGroup.get(groupOf(item))?.push(item)
    return byGroup
  }, [items])

  // Failures are context; they must not make a healthy fleet look busy, and the badge on the tab
  // counts the same set for the same reason.
  const openCount = items.filter((item) => item.kind !== 'RecentFailure').length

  if (attention.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (attention.error) {
    return (
      <Alert color="danger" icon={<TbAlertCircle />} title="Could not load the attention list">
        {attention.error instanceof Error ? attention.error.message : 'No response from the server.'}
      </Alert>
    )
  }

  const toggle = (key: AttentionGroupKey) =>
    setCollapsed((previous) => {
      const next = new Set(previous)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Group gap="xs">
          <Title order={4}>Needs attention</Title>
          <Badge variant="light" color={openCount === 0 ? 'success' : 'danger'}>
            {openCount} open
          </Badge>
          {/* False is not "nothing disagrees" — it is "nobody asked". Collapsing the two would let a
              runner that is down read as a clean bill of health, which is the one lie this view
              cannot afford to tell. */}
          {attention.data && !attention.data.runnerConsulted && (
            <Tooltip
              multiline
              w={280}
              label="The session runner did not answer, so leaked and unclaimed sessions are not in this list. It is not a claim that there are none."
            >
              <Badge variant="light" color="warning">
                Runner not consulted
              </Badge>
            </Tooltip>
          )}
        </Group>
        <Tooltip label="Refresh">
          <ActionIcon
            variant="subtle"
            aria-label="Refresh"
            onClick={() => attention.refetch()}
            loading={attention.isFetching}
          >
            <TbRefresh />
          </ActionIcon>
        </Tooltip>
      </Group>

      {openCount === 0 && <NothingIsStuck />}

      {ATTENTION_GROUPS.map((group) => {
        const rows = grouped.get(group.key) ?? []
        if (rows.length === 0) return null
        const isCollapsed = collapsed.has(group.key)

        return (
          <Stack key={group.key} gap={6}>
            <UnstyledButton onClick={() => toggle(group.key)} aria-expanded={!isCollapsed}>
              <Group gap="xs" wrap="nowrap">
                {isCollapsed ? <TbChevronRight size={14} /> : <TbChevronDown size={14} />}
                <Text size="xs" tt="uppercase" fw={700}>
                  {group.title}
                </Text>
                <Badge size="xs" variant="default">
                  {rows.length}
                </Badge>
                <Text size="xs" c="dimmed">
                  {group.hint}
                </Text>
              </Group>
            </UnstyledButton>
            <Collapse in={!isCollapsed}>
              <Stack gap={6}>
                {rows.map((item) => (
                  <AttentionRow
                    key={keyOf(item)}
                    item={item}
                    onOpen={(target) => navigate(target)}
                  />
                ))}
              </Stack>
            </Collapse>
          </Stack>
        )
      })}
    </Stack>
  )
}

/**
 * The common case. It says what is true rather than showing nothing — an empty panel is
 * indistinguishable from a broken one, and this view only works if a quiet day is legible AS a quiet
 * day. The second line names the exclusion, because "nothing is stuck" while an agent is visibly
 * grinding away would otherwise look like the view had missed it.
 */
function NothingIsStuck() {
  return (
    <Paper withBorder p="xl" data-testid="attention-empty">
      <Center>
        <Stack gap={4} align="center">
          <TbChecks size={28} color="var(--mantine-color-success-6)" />
          <Text fw={600}>Nothing is stuck.</Text>
          <Text size="sm" c="dimmed" ta="center" maw={420}>
            Work that is merely slow is deliberately not listed — a session that is mid-turn is
            working, not stuck.
          </Text>
        </Stack>
      </Center>
    </Paper>
  )
}

function AttentionRow({
  item,
  onOpen,
}: {
  item: AttentionItemDto
  onOpen: (target: string) => void
}) {
  const visual = ATTENTION_VISUALS[item.kind]
  const Icon = visual.icon
  const target = targetOf(item)
  const seconds = ageSeconds(item)
  const age = seconds === null ? null : formatDuration(seconds)

  const body = (
    <Stack gap={4}>
      <Group gap="xs" wrap="nowrap" justify="space-between">
        <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
          <Tooltip label={visual.hint} multiline w={280}>
            <Badge
              size="sm"
              variant="light"
              color={visual.color}
              leftSection={<Icon size={12} />}
              style={{ flexShrink: 0 }}
            >
              {visual.label}
            </Badge>
          </Tooltip>
          <Text size="sm" fw={600} truncate>
            {item.title}
          </Text>
        </Group>
        <Group gap={6} wrap="nowrap" style={{ flexShrink: 0 }}>
          {/* Shown even at $0, which is not noise here: a NeverStarted row that has spent nothing is
              confirming it never ran, and hiding the zero would hide that. */}
          {item.subtreeCostUsd != null && (
            <Tooltip label="Spend on this task and everything under it">
              <Badge size="xs" variant="default" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {formatCost(item.subtreeCostUsd)}
              </Badge>
            </Tooltip>
          )}
          {age && (
            <Tooltip label="How long this has been true">
              <Badge size="xs" variant="default" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {age}
              </Badge>
            </Tooltip>
          )}
        </Group>
      </Group>

      <Text size="sm">{item.headline}</Text>

      {item.evidence && (
        // Pre-wrapped, not reflowed: the evidence is the check interpreter's own reading when one is
        // stored (slice 5) and the tail of the deterministic digest otherwise, and in both cases its
        // line breaks are the structure a human reads it by.
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'pre-wrap' }} lineClamp={6}>
          {item.evidence}
        </Text>
      )}
    </Stack>
  )

  return (
    <Paper withBorder p="xs" data-testid={`attention-row-${item.kind}`}>
      <Stack gap={8}>
        {/* The click target is the body ONLY. Buttons inside a button is invalid markup and swallows
            the inner click, so the verbs sit beside the region that navigates, never inside it. */}
        {target ? (
          <UnstyledButton onClick={() => onOpen(target)} w="100%" aria-label={`Open ${item.title}`}>
            <Box>{body}</Box>
          </UnstyledButton>
        ) : (
          body
        )}
        {item.actions.length > 0 && <AttentionRowActions item={item} onOpen={onOpen} />}
      </Stack>
    </Paper>
  )
}

/** Two or three words per verb — the row's prose already said what happened. */
const ACTION_LABEL: Record<AttentionAction, string> = {
  Reply: 'Answer it',
  Retry: 'Retry',
  Cancel: 'Cancel task',
  Escalate: 'Escalate',
  SendNow: 'Send now',
  CancelMessage: 'Drop message',
  OpenDrawer: 'Read it first',
  KillSession: 'Kill session',
  OpenAgent: 'Open agent',
  OpenCard: 'Open card',
  ClearHold: 'Clear hold',
}

/** Verbs that destroy work. Colour is the only warning a one-click button gets. */
const DESTRUCTIVE: ReadonlySet<AttentionAction> = new Set<AttentionAction>([
  'Cancel',
  'CancelMessage',
  'KillSession',
])

/**
 * The verbs for one row, in the order the SERVER named them — first is primary.
 *
 * <p>That order is a judgement the projection makes per condition and it is load-bearing in one
 * place especially: <c>PastExpectedIdle</c> and <c>ChecksSpent</c> lead with "read it first", because
 * for those two the correct answer is very often to leave the task alone, and a Retry sitting in the
 * primary slot would invite a second agent onto work that is merely finishing quietly.</p>
 *
 * <p>Every mutation invalidates the attention list. A row that survived its own fix would teach the
 * operator that the view lags reality, and a view nobody believes is worse than no view.</p>
 */
function AttentionRowActions({
  item,
  onOpen,
}: {
  item: AttentionItemDto
  onOpen: (target: string) => void
}) {
  const [replying, setReplying] = useState(false)
  const [killAsked, setKillAsked] = useState(false)
  const queryClient = useQueryClient()

  const settle = (success: string, failure: string) => ({
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: attentionKeys.all })
      notifications.show({ color: 'green', message: success })
    },
    onError: (error: unknown) =>
      notifications.show({ color: 'red', message: getApiErrorMessage(error, failure) }),
  })

  const retry = useRetryAgentTask()
  const cancel = useCancelAgentTask()
  const escalate = useEscalateAgentTask()
  const sendNow = useMutation({
    mutationFn: () => sendQueuedMessageNow(item.sessionId!, item.messageId!),
  })
  const dropMessage = useMutation({
    mutationFn: () => cancelQueuedMessage(item.sessionId!, item.messageId!),
  })
  const kill = useMutation({ mutationFn: () => stopSession(item.sessionId!) })
  const clearHold = useClearModelAvailabilityHold()

  const pending =
    retry.isPending ||
    cancel.isPending ||
    escalate.isPending ||
    sendNow.isPending ||
    dropMessage.isPending ||
    kill.isPending ||
    clearHold.isPending

  const target = targetOf(item)
  const shortSession = item.sessionId ? item.sessionId.replace(/-/g, '').slice(0, 8) : 'unknown'

  const run = (action: AttentionAction) => {
    switch (action) {
      case 'Reply':
        setReplying((open) => !open)
        return
      case 'Retry':
        retry.mutate(item.taskId!, settle('Task retried', 'Could not retry the task'))
        return
      case 'Cancel':
        cancel.mutate(item.taskId!, settle('Task cancelled', 'Could not cancel the task'))
        return
      case 'Escalate':
        escalate.mutate(
          { id: item.taskId! },
          settle('Task escalated a tier', 'Could not escalate the task'),
        )
        return
      case 'SendNow':
        sendNow.mutate(undefined, settle('Message sent', 'Could not send the message'))
        return
      case 'CancelMessage':
        dropMessage.mutate(undefined, settle('Message dropped', 'Could not drop the message'))
        return
      case 'OpenDrawer':
      case 'OpenAgent':
      case 'OpenCard':
        if (target) onOpen(target)
        return
      case 'KillSession':
        // NEVER one click. The 2026-08-16 miss (CARD-0056) was a perfectly healthy session — the
        // operator's own working conversation — that the database had written off, and a kill there
        // would have ended somebody mid-sentence. The dialog names the session it would end.
        setKillAsked(true)
        return
      case 'ClearHold':
        clearHold.mutate(
          { kind: item.modelKind!, alias: item.modelAlias! },
          settle('Hold cleared', 'Could not clear the model hold'),
        )
        return
    }
  }

  // A verb whose subject the row does not carry cannot be offered — the server names the actions and
  // the ids separately, so this is the one place they are checked against each other.
  const usable = item.actions.filter((action) => {
    if (action === 'Reply' || action === 'Retry' || action === 'Cancel' || action === 'Escalate')
      return item.taskId !== null
    if (action === 'SendNow' || action === 'CancelMessage')
      return item.sessionId !== null && item.messageId !== null
    if (action === 'KillSession') return item.sessionId !== null
    if (action === 'ClearHold') return Boolean(item.modelKind && item.modelAlias)
    return target !== null
  })

  if (usable.length === 0) return null

  return (
    <Stack gap={6}>
      <Group gap="xs" wrap="wrap">
        {usable.map((action, index) => (
          <Button
            key={action}
            size="compact-xs"
            variant={index === 0 ? 'light' : 'subtle'}
            color={DESTRUCTIVE.has(action) ? 'danger' : index === 0 ? 'active' : 'gray'}
            disabled={pending}
            onClick={() => run(action)}
          >
            {ACTION_LABEL[action]}
          </Button>
        ))}
      </Group>

      {replying && item.taskId && (
        <BlockedReplyRow taskId={item.taskId} onDone={() => setReplying(false)} />
      )}

      <Modal
        opened={killAsked}
        onClose={() => setKillAsked(false)}
        title={`Kill session ${shortSession}?`}
        centered
      >
        <Stack gap="sm">
          <Text size="sm">
            <Code>{shortSession}</Code> — {item.title}
          </Text>
          {item.evidence && (
            <Text size="xs" c="dimmed" style={{ whiteSpace: 'pre-wrap' }}>
              {item.evidence}
            </Text>
          )}
          <Alert color="warning" variant="light">
            The runner is still running this. The live process may be the side that is right — the
            database row is the other side of the disagreement, and killing ends whatever the session
            is in the middle of.
          </Alert>
          <Group justify="flex-end" gap="xs">
            <Button size="xs" variant="subtle" onClick={() => setKillAsked(false)}>
              Leave it running
            </Button>
            <Button
              size="xs"
              color="danger"
              loading={kill.isPending}
              onClick={() =>
                kill.mutate(undefined, {
                  ...settle(`Killed session ${shortSession}`, 'Could not kill the session'),
                  onSettled: () => setKillAsked(false),
                })
              }
            >
              Kill session {shortSession}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
