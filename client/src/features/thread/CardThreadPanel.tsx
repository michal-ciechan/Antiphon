import {
  Alert,
  Badge,
  Box,
  Button,
  Divider,
  Group,
  Loader,
  Modal,
  Paper,
  Select,
  Stack,
  Text,
  Textarea,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { Fragment, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { TbAlertTriangle, TbChevronDown, TbChevronRight } from 'react-icons/tb'
import { useAttention, type AttentionItemDto } from '../../api/attention'
import { useMoveCard, type BoardColumnDto, type CardDto } from '../../api/boards'
import {
  useCancelAgentTask,
  useEscalateAgentTask,
  useRetryAgentTask,
} from '../../api/agentTasks'
import {
  useCardThread,
  type CardThreadCommitDto,
  type CardThreadPlanDto,
  type CardThreadTaskDto,
} from '../../api/cardThread'
import { getApiErrorMessage } from '../../api/client'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { BlockedReplyRow } from '../attention/BlockedReplyRow'
import { ATTENTION_VISUALS } from '../attention/attentionVisuals'
import { legalMoveTargets } from '../board/boardShapeModel'
import { DelegateModal, type DelegatePrefill } from '../delegations/DelegateModal'
import { STATUS_COLOR, TIER_VISUALS, formatCost } from '../delegations/taskVisuals'
import { formatClockTime } from '../home/workLineFormat'

/**
 * The thread (mobile-thread spec §D2/§4, slice M4): one card's work as ONE scroll — plans, then
 * tasks, then commits, then the terminal reason — assembled by the server's citation join and
 * annotated here with the one piece the server deliberately does not send: which `/api/attention`
 * rows belong to these tasks. That join is client-side by `taskId` (spec §D2 — the server does not
 * re-derive stuckness; the CARD-0035 non-widening rule).
 *
 * <p><b>Subject vs mentioned plans.</b> The thread carries two plan lists per the T2 contract: the
 * plans ABOUT this card (`subject: true`) and the plans that merely cite it. Subjects render as
 * full rows with the verbs (Approve, Hand back); mentions fold behind a counted toggle and render
 * dimmed, without verbs — collapsing the two would put every plan on every neighbour's thread,
 * and dropping the mentions would hide real context.</p>
 */
export function CardThreadPanel({
  identifier,
  boardId,
  columns = [],
  showCardHeader = false,
}: {
  /** Any form the card routes take: `CARD-0067`, `#67`, `67`, or the guid. */
  identifier: string
  boardId?: string
  /** The board's states, so a plan can be approved (a move) from here. Empty disables Approve. */
  columns?: BoardColumnDto[]
  /** The full-screen page shows the header; inside CardModal the modal header already carries it. */
  showCardHeader?: boolean
}) {
  const thread = useCardThread(identifier)
  const attention = useAttention()

  const attentionByTask = useMemo(() => {
    const map = new Map<string, AttentionItemDto[]>()
    for (const item of attention.data?.items ?? []) {
      if (!item.taskId) continue
      map.set(item.taskId, [...(map.get(item.taskId) ?? []), item])
    }
    return map
  }, [attention.data])

  if (thread.isPending) {
    return (
      <Group p="md">
        <Loader size="xs" />
        <Text size="sm" c="dimmed">Loading thread…</Text>
      </Group>
    )
  }
  if (thread.isError) {
    return (
      <Alert color="red" title="Thread failed to load" m="sm" data-testid="thread-error">
        {getApiErrorMessage(thread.error, 'Thread failed to load')}
      </Alert>
    )
  }

  const data = thread.data
  const subjectPlans = data.plans.filter((p) => p.subject)
  const mentionPlans = data.plans.filter((p) => !p.subject)

  return (
    <Stack gap="md" p="sm" maw={640} data-testid="card-thread">
      {showCardHeader && <ThreadCardHeader card={data.card} />}

      <ThreadSection title={`Plans · ${subjectPlans.length}`}>
        {subjectPlans.length === 0 && (
          <Text size="sm" c="dimmed" px={4} py={6}>
            No plan is about this card.
          </Text>
        )}
        {subjectPlans.map((plan, i) => (
          <Fragment key={plan.plan.relativePath}>
            {i > 0 && <Divider />}
            <SubjectPlanRow
              plan={plan}
              identifier={data.identifier}
              card={data.card}
              boardId={boardId}
              columns={columns}
            />
          </Fragment>
        ))}
        {mentionPlans.length > 0 && <MentionedPlans plans={mentionPlans} />}
      </ThreadSection>

      <ThreadSection title={`Tasks · ${data.tasks.length}`}>
        {data.tasks.length === 0 && (
          <Text size="sm" c="dimmed" px={4} py={6}>
            No task cites this card.
          </Text>
        )}
        {data.tasks.map((task, i) => (
          <Fragment key={task.id}>
            {i > 0 && <Divider />}
            <ThreadTaskRow task={task} attention={attentionByTask.get(task.id) ?? []} />
          </Fragment>
        ))}
      </ThreadSection>

      <ThreadSection title={data.reposConsulted ? `Commits · ${data.commits.length}` : 'Commits'}>
        {!data.reposConsulted ? (
          // False means nobody could ASK git — a deleted worktree, a project with no local
          // repository — which is a different answer from "nothing was committed".
          <Text size="sm" c="dimmed" px={4} py={6} data-testid="thread-repos-not-consulted">
            No repository could be consulted — commits here are unknown, not absent. The task list
            above is complete regardless.
          </Text>
        ) : data.commits.length === 0 ? (
          <Text size="sm" c="dimmed" px={4} py={6}>
            No commit cites this card.
          </Text>
        ) : (
          data.commits.map((commit, i) => (
            <Fragment key={commit.sha}>
              {i > 0 && <Divider />}
              <CommitRow commit={commit} />
            </Fragment>
          ))
        )}
      </ThreadSection>

      {data.card.terminalReason && (
        <Paper withBorder radius="md" p="sm" data-testid="thread-terminal-reason">
          <Text size="xs" c="dimmed" fw={700} tt="uppercase">Closed</Text>
          <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{data.card.terminalReason}</Text>
        </Paper>
      )}
    </Stack>
  )
}

function ThreadCardHeader({ card }: { card: CardDto }) {
  return (
    <Box data-testid="thread-card-header">
      <Group gap={6} wrap="nowrap">
        <Badge color="gray" variant="outline" title={card.identifier}>
          {displayIdentifier(card.identifier)}
        </Badge>
        <Text fw={700} size="lg" style={{ minWidth: 0 }} lineClamp={2}>
          {card.title}
        </Text>
      </Group>
      <Group gap={6} mt={4}>
        <Badge variant="light">{card.status}</Badge>
        <Badge color="gray" variant="outline">P{card.priority}</Badge>
      </Group>
    </Box>
  )
}

function ThreadSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box>
      <Text size="xs" c="dimmed" fw={700} tt="uppercase" mb={4} style={{ letterSpacing: 1 }}>
        {title}
      </Text>
      <Paper withBorder radius="md" px="xs">
        {children}
      </Paper>
    </Box>
  )
}

/**
 * A plan that is ABOUT this card: full row, tap-through to the reader, and the two verbs — Approve
 * (a card move with a reason, per §D5, never a new "approval" entity) and Hand back (the CARD-0034
 * react gesture: a delegate task pre-filled with the identifier and the plan path).
 */
function SubjectPlanRow({
  plan,
  identifier,
  card,
  boardId,
  columns,
}: {
  plan: CardThreadPlanDto
  identifier: string
  card: CardDto
  boardId?: string
  columns: BoardColumnDto[]
}) {
  const p = plan.plan
  return (
    <Box py={8} px={4} data-testid={`thread-plan-${p.fileName}`}>
      <UnstyledButton
        component={Link}
        to={`/plans?file=${encodeURIComponent(p.relativePath)}`}
        w="100%"
        aria-label={`Read plan ${p.title}`}
      >
        <Group wrap="nowrap" gap={8}>
          <Box style={{ minWidth: 0, flex: 1 }}>
            <Text size="sm" fw={600} truncate>{p.title}</Text>
            <Text size="xs" c="dimmed" truncate>
              {p.relativePath}
              {p.date ? ` · ${p.date}` : ''}
            </Text>
          </Box>
          {p.status && (
            <Badge size="xs" variant="light" color="blue" style={{ flexShrink: 0, maxWidth: 140 }}>
              {p.status}
            </Badge>
          )}
        </Group>
      </UnstyledButton>
      <Group justify="flex-end" gap="xs" mt={6}>
        <HandBackButton identifier={identifier} context={`plan ${p.relativePath}`} />
        {boardId && columns.length > 0 && (
          <ApprovePlanButton boardId={boardId} card={card} columns={columns} planPath={p.relativePath} />
        )}
      </Group>
    </Box>
  )
}

/**
 * The plans that merely CITE this card — neighbours, not subjects. Folded behind a counted toggle
 * and rendered dimmed with a "mentions" badge, no verbs: they are context you go looking for, and
 * promoting them would put five neighbouring plans on every card's thread.
 */
function MentionedPlans({ plans }: { plans: CardThreadPlanDto[] }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Divider />
      <UnstyledButton
        w="100%"
        py={8}
        px={4}
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
        data-testid="thread-mentions-toggle"
      >
        <Group gap={6}>
          {open ? (
            <TbChevronDown size={14} style={{ flexShrink: 0 }} />
          ) : (
            <TbChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5 }} />
          )}
          <Text size="xs" c="dimmed">
            Mentioned in {plans.length} other {plans.length === 1 ? 'plan' : 'plans'}
          </Text>
        </Group>
      </UnstyledButton>
      {open &&
        plans.map((plan) => (
          <UnstyledButton
            key={plan.plan.relativePath}
            component={Link}
            to={`/plans?file=${encodeURIComponent(plan.plan.relativePath)}`}
            w="100%"
            py={6}
            px={4}
            pl={24}
            aria-label={`Read plan ${plan.plan.title}`}
            data-testid={`thread-mention-${plan.plan.fileName}`}
          >
            <Group wrap="nowrap" gap={8}>
              <Badge size="xs" variant="outline" color="gray" style={{ flexShrink: 0 }}>
                mentions
              </Badge>
              <Text size="xs" c="dimmed" truncate style={{ flex: 1 }}>
                {plan.plan.title}
              </Text>
            </Group>
          </UnstyledButton>
        ))}
    </>
  )
}

/**
 * Approve = a card move with `reason: "plan approved: <file>"` (spec §D5) — the board column is
 * where work-state lives and the reason is durable on the move. The confirm reuses the MoveMenu
 * contract: the target picker names which columns spawn an agent, and an ACTIVE target gets the
 * same warning before Move.
 */
function ApprovePlanButton({
  boardId,
  card,
  columns,
  planPath,
}: {
  boardId: string
  card: CardDto
  columns: BoardColumnDto[]
  planPath: string
}) {
  const [opened, setOpened] = useState(false)
  const targets = legalMoveTargets(card, columns)
  const defaultTarget = targets.find((column) => column.isActive) ?? targets[0] ?? null
  const [targetId, setTargetId] = useState<string | null>(null)
  const [reason, setReason] = useState<string | null>(null)
  const moveCard = useMoveCard(boardId)

  if (targets.length === 0) return null
  const target = targets.find((column) => column.id === (targetId ?? defaultTarget?.id)) ?? null
  const effectiveReason = reason ?? `plan approved: ${planPath}`

  const close = () => {
    setOpened(false)
    setTargetId(null)
    setReason(null)
  }

  const submit = () => {
    if (!target) return
    moveCard.mutate(
      {
        cardId: card.id,
        request: {
          boardColumnId: target.id,
          concurrencyToken: card.concurrencyToken,
          reason: effectiveReason.trim() || null,
          // The human has asked, twice: the picker names the spawn and the dialog warns again
          // before Move — the same opt-in the MoveMenu sends.
          spawn: true,
        },
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: `${card.identifier} moved to ${target.name}` })
        },
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Move failed') }),
      },
    )
    close()
  }

  return (
    <>
      <Button size="compact-xs" onClick={() => setOpened(true)} data-testid="thread-approve-plan">
        Approve
      </Button>
      <Modal
        opened={opened}
        onClose={close}
        title={`Approve plan — move ${displayIdentifier(card.identifier)}`}
        zIndex={400}
      >
        <Stack>
          <Select
            label="Move to"
            data={targets.map((column) => ({
              value: column.id,
              label: `${column.name}${column.isActive ? ' — spawns an agent' : ''}`,
            }))}
            value={target?.id ?? null}
            onChange={setTargetId}
            allowDeselect={false}
          />
          {target?.isActive && (
            <Alert
              color="warning"
              variant="light"
              icon={<TbAlertTriangle size={18} />}
              title="This starts work"
            >
              Moving a card into {target.name} spawns an agent session on it.
            </Alert>
          )}
          <Textarea
            label="Reason"
            autosize
            minRows={2}
            value={effectiveReason}
            onChange={(event) => setReason(event.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={close}>Cancel</Button>
            <Button onClick={submit} loading={moveCard.isPending}>Move</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}

/**
 * Hand back — "change this" at phone altitude (spec §D5): opens the DelegateModal pre-filled with
 * the card identifier and what is being reacted to, so the reaction becomes a delegated task
 * rather than the operator taking the work back.
 */
function HandBackButton({
  identifier,
  context,
  directory,
}: {
  identifier: string
  context: string
  directory?: string
}) {
  const [opened, setOpened] = useState(false)
  const prefill: DelegatePrefill = {
    goal: `${identifier} — change requested on ${context}: `,
    workingDirectory: directory,
  }
  return (
    <>
      <Button
        size="compact-xs"
        variant="default"
        onClick={() => setOpened(true)}
        data-testid="thread-hand-back"
      >
        Hand back
      </Button>
      <DelegateModal
        opened={opened}
        onClose={() => setOpened(false)}
        prefill={prefill}
        title={`Hand back — ${identifier}`}
      />
    </>
  )
}

/** Open statuses — the ones Cancel still means something for. */
const OPEN_STATUSES = new Set<CardThreadTaskDto['status']>([
  'Queued',
  'Dispatched',
  'Working',
  'Blocked',
])

function ThreadTaskRow({
  task,
  attention,
}: {
  task: CardThreadTaskDto
  attention: AttentionItemDto[]
}) {
  const [answering, setAnswering] = useState(false)
  const [showFullResult, setShowFullResult] = useState(false)
  const retry = useRetryAgentTask()
  const cancel = useCancelAgentTask()
  const escalate = useEscalateAgentTask()

  const settle = (success: string, failure: string) => ({
    onSuccess: () => notifications.show({ color: 'green', message: success }),
    onError: (error: unknown) =>
      notifications.show({ color: 'red', message: getApiErrorMessage(error, failure) }),
  })

  const open = OPEN_STATUSES.has(task.status)
  const firstParagraph = task.result?.trim().split(/\n\s*\n/, 1)[0] ?? null
  const hasMoreResult = !!task.result && firstParagraph !== task.result.trim()

  return (
    <Box py={8} px={4} data-testid={`thread-task-${task.id}`}>
      <Group wrap="nowrap" gap={8} align="flex-start">
        <Box style={{ minWidth: 0, flex: 1 }}>
          <Text size="sm" fw={600} lineClamp={2}>{task.title}</Text>
          <Group gap={6} mt={2}>
            <Badge size="xs" color={STATUS_COLOR[task.status]} variant="light">
              {task.status}
            </Badge>
            <Badge size="xs" color="violet" variant="outline">
              {TIER_VISUALS[task.modelLevel].alias}
            </Badge>
            <Text size="xs" c="dimmed">{formatCost(task.subtreeCostUsd)}</Text>
            {task.nextCheckAt && (
              <Text size="xs" c="dimmed">check {formatClockTime(task.nextCheckAt)}</Text>
            )}
            {task.matchedOn === 'goal' && (
              // A goal often names OTHER cards as context, so this correlation is the weaker
              // claim — the row says so instead of presenting the join as certain.
              <Text size="xs" c="dimmed" fs="italic">cited in goal only</Text>
            )}
          </Group>
        </Box>
      </Group>

      {attention.map((item) => (
        <Group
          key={`${item.kind}|${item.sinceUtc}`}
          gap={6}
          mt={6}
          wrap="nowrap"
          data-testid={`thread-task-attention-${task.id}`}
        >
          <Badge size="xs" color={ATTENTION_VISUALS[item.kind].color} variant="filled" style={{ flexShrink: 0 }}>
            {ATTENTION_VISUALS[item.kind].label}
          </Badge>
          <Text size="xs" c="dimmed" lineClamp={2}>{item.headline}</Text>
        </Group>
      ))}

      {task.latestCheck && (
        <Box
          mt={6}
          pl={8}
          style={{ borderLeft: '2px solid var(--mantine-color-default-border)' }}
          data-testid={`thread-task-check-${task.id}`}
        >
          <Text size="xs" c="dimmed">
            {/* A reading and a digest tail are not the same kind of claim — say which. */}
            {task.latestCheck.fromInterpreter ? 'check reading' : 'check digest tail'}
            {' · '}
            {formatClockTime(task.latestCheck.at)}
          </Text>
          <Text size="xs" style={{ whiteSpace: 'pre-wrap' }} lineClamp={8}>
            {task.latestCheck.text}
          </Text>
        </Box>
      )}

      {firstParagraph && (
        <Box mt={6} data-testid={`thread-task-result-${task.id}`}>
          <Text size="xs" c="dimmed" fw={700} tt="uppercase">Report</Text>
          <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
            {showFullResult ? task.result : firstParagraph}
          </Text>
          {hasMoreResult && (
            <Button
              size="compact-xs"
              variant="subtle"
              onClick={() => setShowFullResult((prev) => !prev)}
            >
              {showFullResult ? 'show less' : 'read all'}
            </Button>
          )}
        </Box>
      )}

      {task.failureReason && (
        <Text size="xs" c="danger.5" mt={4} style={{ whiteSpace: 'pre-wrap' }}>
          {task.failureReason}
        </Text>
      )}

      {answering && (
        <Box mt={6}>
          <BlockedReplyRow taskId={task.id} onDone={() => setAnswering(false)} />
        </Box>
      )}

      <Group justify="flex-end" gap="xs" mt={6}>
        {task.status === 'Blocked' && !answering && (
          <Button size="compact-xs" onClick={() => setAnswering(true)}>
            Answer it
          </Button>
        )}
        {(task.status === 'Failed' || task.status === 'Canceled') && (
          <Button
            size="compact-xs"
            variant="default"
            loading={retry.isPending}
            onClick={() => retry.mutate(task.id, settle('Task retried', 'Retry failed'))}
          >
            Retry
          </Button>
        )}
        {open && (
          <Button
            size="compact-xs"
            variant="default"
            loading={escalate.isPending}
            onClick={() =>
              escalate.mutate({ id: task.id }, settle('Task escalated a tier', 'Escalate failed'))
            }
          >
            Escalate
          </Button>
        )}
        {open && (
          <Button
            size="compact-xs"
            variant="default"
            color="red"
            loading={cancel.isPending}
            onClick={() => cancel.mutate(task.id, settle('Task cancelled', 'Cancel failed'))}
          >
            Cancel
          </Button>
        )}
        {task.status !== 'Blocked' && task.result && <HandBackButton identifier={citationOf(task.title)} context="its report" />}
      </Group>
    </Box>
  )
}

/** The task's own title is the best citation context we hold for a hand-back on a report. */
function citationOf(title: string): string {
  const match = /card-\d{1,4}/i.exec(title)
  return match ? match[0].toUpperCase() : title.slice(0, 60)
}

function CommitRow({ commit }: { commit: CardThreadCommitDto }) {
  return (
    <Group wrap="nowrap" gap={8} py={6} px={4} data-testid={`thread-commit-${commit.shortSha}`}>
      <Text size="xs" c="dimmed" ff="monospace" style={{ flexShrink: 0 }}>
        {commit.shortSha}
      </Text>
      <Text size="sm" truncate style={{ flex: 1 }}>{commit.subject}</Text>
      <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
        {commit.date.slice(0, 10)}
      </Text>
    </Group>
  )
}
