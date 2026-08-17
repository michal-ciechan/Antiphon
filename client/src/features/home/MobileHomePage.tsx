import {
  Badge,
  Box,
  Button,
  Divider,
  Group,
  Loader,
  Paper,
  Stack,
  Text,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueries, useQueryClient } from '@tanstack/react-query'
import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import {
  agentTaskKeys,
  useAgentTasks,
  type AgentTaskDetailDto,
  type AgentTaskSummaryDto,
} from '../../api/agentTasks'
import { attentionKeys, useAttention, type AttentionItemDto } from '../../api/attention'
import { apiGet, getApiErrorMessage } from '../../api/client'
import { useAllBoardDetails, useBoards } from '../../api/boards'
import { cancelQueuedMessage, sendQueuedMessageNow } from '../../api/sessions'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { BlockedReplyRow } from '../attention/BlockedReplyRow'
import { ATTENTION_VISUALS, ageSeconds, keyOf, targetOf } from '../attention/attentionVisuals'
import { formatCost, formatDuration } from '../delegations/taskVisuals'
import {
  computeAwayDelta,
  firstSentence,
  readLastSeen,
  stampLastSeen,
  type AwayDelta,
} from './awayDelta'
import { isActiveTask } from './projectGrouping'
import { WorkLine } from './WorkLine'
import { citationHead, formatClockTime, workLineTarget } from './workLineFormat'

/**
 * The phone home: three bands in fixed order (spec `2026-08-17-mobile-thread-and-plan-surfacing.md`
 * §D3 — the CARD-0031 urgency order collapsed to a phone's attention budget).
 *
 * <p><b>Calm is a designed state, not an empty state.</b> Band 1 ("Needs you") is absent entirely
 * when nothing needs a human — no empty-state scaffolding for the band that should usually not
 * exist — and its place is taken by a card that says when the system will next have something to
 * say. That next-check time is what makes quiet feel supervised rather than dead: the operator
 * knows whether to wait or to chase.</p>
 *
 * <p>Nothing here decides that something is stuck — band 1 renders exactly the rows
 * `/api/attention` sent at Critical/Error (the CARD-0035 non-widening rule), with the answer
 * affordance inline: a blocked question opens straight onto `BlockedReplyRow`, the same component
 * and the same `POST …/reply` the desktop panel uses.</p>
 */
export function MobileHomePage() {
  const attention = useAttention()
  const tasks = useAgentTasks()

  const needsYou = useMemo(
    () =>
      (attention.data?.items ?? []).filter(
        (item) =>
          // Critical/Error only — the phone gets the "needs a person" cut, not the diagnostic
          // list; Warning-band rows and settled-failure context stay on the desktop tab.
          (item.severity === 'Critical' || item.severity === 'Error') &&
          item.kind !== 'RecentFailure',
      ),
    [attention.data],
  )

  const inMotion = useMemo(
    () => (tasks.data ?? []).filter(isActiveTask).sort(byWatchOrder),
    [tasks.data],
  )

  // The away window (M6, CARD-0036's pull half): the previous visit and this visit's clock are
  // both read ONCE per mount, so the delta is stable for the whole stay — a task that settles
  // while the screen is open belongs to the live bands, not to "while you were away". The stamp
  // is a side effect, so it lives in an effect, not the render.
  const [lastSeen] = useState(() => readLastSeen(window.localStorage))
  const [nowMs] = useState(() => Date.now())
  useEffect(() => {
    stampLastSeen(window.localStorage, new Date(nowMs).toISOString())
  }, [nowMs])

  const boards = useBoards()
  const boardIds = useMemo(() => (boards.data ?? []).map((board) => board.id), [boards.data])
  const boardDetails = useAllBoardDetails(boardIds)
  const cards = useMemo(
    () => (boardDetails.data ?? []).flatMap((board) => board.columns.flatMap((column) => column.cards)),
    [boardDetails.data],
  )

  const delta = useMemo(
    () => computeAwayDelta(tasks.data ?? [], cards, lastSeen, nowMs),
    [tasks.data, cards, lastSeen, nowMs],
  )

  if (attention.isLoading || tasks.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  return (
    <Stack gap={0} maw={480} mx="auto" data-testid="mobile-home">
      {needsYou.length === 0 ? <CalmCard tasks={inMotion} /> : <NeedsYouBand items={needsYou} />}
      <InMotionBand tasks={inMotion} />
      <AwayBand delta={delta} />
    </Stack>
  )
}

/**
 * Soonest check first — the next thing that will produce news tops the band — then newest task.
 * Blocked tasks need no boost here: being blocked IS a band-1 row, and that duplication is the
 * signal (§D3 says the same of checks-spent).
 */
function byWatchOrder(a: AgentTaskSummaryDto, b: AgentTaskSummaryDto): number {
  const aCheck = a.nextCheckAt ? Date.parse(a.nextCheckAt) : Number.POSITIVE_INFINITY
  const bCheck = b.nextCheckAt ? Date.parse(b.nextCheckAt) : Number.POSITIVE_INFINITY
  if (aCheck !== bCheck) return aCheck - bCheck
  return Date.parse(b.createdAt) - Date.parse(a.createdAt)
}

function BandTitle({ children, color }: { children: React.ReactNode; color?: string }) {
  return (
    <Text
      size="xs"
      fw={700}
      c={color ?? 'dimmed'}
      tt="uppercase"
      mt="md"
      mb={4}
      style={{ letterSpacing: 1 }}
    >
      {children}
    </Text>
  )
}

/**
 * The common case, and the screen's first words on a healthy day. The second line always says
 * what happens next — a check time when one is scheduled, otherwise where the news will land —
 * because "nothing needs you" with no forecast is indistinguishable from "nothing is watching".
 */
function CalmCard({ tasks }: { tasks: AgentTaskSummaryDto[] }) {
  const nextCheck = tasks
    .map((task) => task.nextCheckAt)
    .filter((at): at is string => at !== null)
    .sort((a, b) => Date.parse(a) - Date.parse(b))[0]

  const forecast = nextCheck
    ? `Next check-in ${formatClockTime(nextCheck)} — you'll see its reading here.`
    : tasks.length > 0
      ? 'No more check-ins scheduled — the live lines below are the signal.'
      : 'Nothing is running. Whatever finishes next will appear below.'

  return (
    <Paper withBorder radius="md" p="md" mt="xs" data-testid="calm-state">
      <Text size="sm" c="dimmed">
        Nothing needs you.
      </Text>
      <Text size="xs" c="dimmed">
        {forecast}
      </Text>
    </Paper>
  )
}

function NeedsYouBand({ items }: { items: AttentionItemDto[] }) {
  return (
    <>
      <BandTitle color="var(--mantine-color-danger-5)">Needs you · {items.length}</BandTitle>
      <Stack gap="xs">
        {items.map((item) => (
          <NeedsYouRow key={keyOf(item)} item={item} />
        ))}
      </Stack>
    </>
  )
}

/**
 * One stuck row at phone altitude: the badge, the server's headline, and the fix inline. A
 * blocked question opens directly onto the reply box — the CARD-0033 ask; on a phone the tap that
 * would merely reveal the box is a tap the answer loses. Parked messages carry their two queue
 * verbs. Every other condition navigates to the surface that explains it (the task drawer or the
 * agent page) — the phone shows THAT it is stuck; diagnosis has more room elsewhere.
 */
function NeedsYouRow({ item }: { item: AttentionItemDto }) {
  const visual = ATTENTION_VISUALS[item.kind]
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const target = targetOf(item)
  const canAnswer = item.actions.includes('Reply') && item.taskId !== null
  const [answering, setAnswering] = useState(canAnswer)

  const settle = (success: string, failure: string) => ({
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: attentionKeys.all })
      notifications.show({ color: 'green', message: success })
    },
    onError: (error: unknown) =>
      notifications.show({ color: 'red', message: getApiErrorMessage(error, failure) }),
  })

  const sendNow = useMutation({
    mutationFn: () => sendQueuedMessageNow(item.sessionId!, item.messageId!),
  })
  const dropMessage = useMutation({
    mutationFn: () => cancelQueuedMessage(item.sessionId!, item.messageId!),
  })
  const hasQueueVerbs =
    item.actions.some((action) => action === 'SendNow' || action === 'CancelMessage') &&
    item.sessionId !== null &&
    item.messageId !== null

  const seconds = ageSeconds(item)
  const age = seconds === null ? null : formatDuration(seconds)

  const body = (
    <Stack gap={4}>
      <Group gap={6} wrap="nowrap">
        <Badge size="xs" color={visual.color} variant="filled" style={{ flexShrink: 0 }}>
          {visual.label}
        </Badge>
        <Text size="xs" c="dimmed" truncate>
          {item.title}
          {age ? ` · ${age}` : ''}
        </Text>
      </Group>
      <Text size="sm">{item.headline}</Text>
      {item.evidence && (
        <Text size="xs" c="dimmed" lineClamp={2} style={{ whiteSpace: 'pre-wrap' }}>
          {item.evidence}
        </Text>
      )}
    </Stack>
  )

  return (
    <Paper withBorder radius="md" p="sm" data-testid={`needs-you-${item.kind}`}>
      <Stack gap={8}>
        {/* Body-only click target — verbs beside it, never inside it (nested buttons swallow). */}
        {target ? (
          <UnstyledButton onClick={() => navigate(target)} w="100%" aria-label={`Open ${item.title}`}>
            {body}
          </UnstyledButton>
        ) : (
          body
        )}
        {answering && item.taskId && (
          <BlockedReplyRow taskId={item.taskId} onDone={() => setAnswering(false)} />
        )}
        {canAnswer && !answering && (
          <Group justify="flex-end">
            <Button size="compact-xs" onClick={() => setAnswering(true)}>
              Answer it
            </Button>
          </Group>
        )}
        {hasQueueVerbs && (
          <Group justify="flex-end" gap="xs">
            <Button
              size="compact-xs"
              variant="default"
              disabled={sendNow.isPending || dropMessage.isPending}
              onClick={() =>
                dropMessage.mutate(
                  undefined,
                  settle('Message dropped', 'Could not drop the message'),
                )
              }
            >
              Drop message
            </Button>
            <Button
              size="compact-xs"
              color="warning"
              disabled={sendNow.isPending || dropMessage.isPending}
              onClick={() =>
                sendNow.mutate(undefined, settle('Message sent', 'Could not send the message'))
              }
            >
              Send now
            </Button>
          </Group>
        )}
      </Stack>
    </Paper>
  )
}

/** Rows shown before the band folds the rest into a counted "+ n more" line — never silently. */
const MAX_AWAY_TASK_ROWS = 6

/**
 * Band 3: what happened since the last visit (spec §D3; CARD-0036's pull half). Settled tasks
 * lead — on a calm day this is the band that answers "so what got done?" — each with the first
 * sentence of its report; then cards that changed state; then the spend on what settled. Check
 * readings deliberately do NOT stream here (§D4: no firehose) — they live on the thread and in
 * the attention rows.
 */
function AwayBand({ delta }: { delta: AwayDelta }) {
  const shownTasks = delta.settledTasks.slice(0, MAX_AWAY_TASK_ROWS)
  const hidden = delta.settledTasks.length - shownTasks.length

  // The report's first sentence lives on the detail DTO; one fetch per shown row, cached under
  // the drawer's own key so opening the drawer later costs nothing extra.
  const details = useQueries({
    queries: shownTasks.map((task) => ({
      queryKey: agentTaskKeys.detail(task.id),
      queryFn: () => apiGet<AgentTaskDetailDto>(`/agent-tasks/${task.id}`),
      staleTime: 60_000,
    })),
  })

  const heading = delta.firstVisit
    ? 'While you were away · last 24h'
    : `While you were away · since ${formatClockTime(delta.sinceUtc)}`
  const isEmpty = shownTasks.length === 0 && delta.cardChanges.length === 0

  return (
    <>
      <BandTitle>{heading}</BandTitle>
      {isEmpty ? (
        <Text size="sm" c="dimmed" px={4} data-testid="away-empty">
          Nothing finished while you were away.
        </Text>
      ) : (
        <Paper withBorder radius="md" px="xs" data-testid="away-band">
          {shownTasks.map((task, index) => (
            <Fragment key={task.id}>
              {index > 0 && <Divider />}
              <AwayRow
                to={workLineTarget(task)}
                label={task.title}
                line={settledTaskLine(task, firstSentence(details[index]?.data?.result ?? null))}
                sub={`${formatClockTime(task.completedAt!)} · ${formatCost(task.costUsd)}`}
              />
            </Fragment>
          ))}
          {hidden > 0 && (
            <>
              <Divider />
              <AwayRow
                to="/orchestrator?tab=delegations"
                label="all settled work"
                line={`+ ${hidden} more settled`}
                sub="open the delegations board"
              />
            </>
          )}
          {delta.cardChanges.map((change) => (
            <Fragment key={change.card.id}>
              <Divider />
              <AwayRow
                to={`/boards/${change.card.boardId}`}
                label={change.card.title}
                line={`${displayIdentifier(change.card.identifier)} ${change.card.title} — ${
                  change.change === 'done' ? 'done' : 'started'
                }`}
                sub={formatClockTime(change.atUtc)}
              />
            </Fragment>
          ))}
        </Paper>
      )}
      {delta.settledSpendUsd > 0 && (
        <Text size="xs" c="dimmed" px={4} mt={4}>
          Spent {formatCost(delta.settledSpendUsd)} on work that settled in this window.
        </Text>
      )}
    </>
  )
}

/** `#67 finished — <the report's own first line>`, or the bare outcome when no report exists. */
function settledTaskLine(task: AgentTaskSummaryDto, sentence: string | null): string {
  const verb =
    task.status === 'Succeeded' ? 'finished' : task.status === 'Failed' ? 'failed' : 'cancelled'
  const head = `${citationHead(task.title)} — ${verb}`
  return sentence ? `${head} — ${sentence}` : head
}

function AwayRow({ to, label, line, sub }: { to: string; label: string; line: string; sub: string }) {
  return (
    <UnstyledButton component={Link} to={to} w="100%" py={8} px={4} aria-label={`Open ${label}`}>
      <Group justify="space-between" wrap="nowrap" gap={4}>
        <Box style={{ minWidth: 0 }}>
          <Text size="sm" lineClamp={2}>
            {line}
          </Text>
          <Text size="xs" c="dimmed" truncate>
            {sub}
          </Text>
        </Box>
      </Group>
    </UnstyledButton>
  )
}

/** Band 2: the live lines, or the honest three words when there are none. */
function InMotionBand({ tasks }: { tasks: AgentTaskSummaryDto[] }) {
  return (
    <>
      <BandTitle>In motion{tasks.length > 0 ? ` · ${tasks.length}` : ''}</BandTitle>
      {tasks.length === 0 ? (
        <Text size="sm" c="dimmed" px={4}>
          Nothing running.
        </Text>
      ) : (
        <Paper withBorder radius="md" px="xs">
          {tasks.map((task, index) => (
            <Fragment key={task.id}>
              {index > 0 && <Divider />}
              <WorkLine task={task} />
            </Fragment>
          ))}
        </Paper>
      )}
    </>
  )
}
