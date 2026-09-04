import {
  Anchor,
  Box,
  Divider,
  Group,
  Paper,
  Stack,
  Text,
  UnstyledButton,
} from '@mantine/core'
import { useQueries } from '@tanstack/react-query'
import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import {
  agentTaskKeys,
  useAgentTasks,
  type AgentTaskDetailDto,
  type AgentTaskSummaryDto,
} from '../../api/agentTasks'
import { useAttention } from '../../api/attention'
import { apiGet } from '../../api/client'
import { useCards } from '../../api/boards'
import { usePlanCatalog } from '../../api/plans'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { CardSkeleton, InlineSkeleton } from '../../shared/SkeletonLayouts'
import { AttentionGlance } from '../attention/AttentionGlance'
import { homeBucketCounts } from '../attention/attentionVisuals'
import { formatCost } from '../delegations/taskVisuals'
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
 * <p><b>Calm is a designed state, not an empty state.</b> Band 1 is absent entirely when nothing
 * needs a human — no empty-state scaffolding for the band that should usually not exist — and its
 * place is taken by a card that says when the system will next have something to say. That
 * next-check time is what makes quiet feel supervised rather than dead: the operator knows
 * whether to wait or to chase.</p>
 *
 * <p>Band 1 is the three-badge glance (CARD-0300), not the expandable rows. Reply stays on
 * `/attention` — one extra tap on a phone, accepted so Home stays a glance. Counts come from
 * `groupOf` over the same feed; nothing here decides that something is stuck.</p>
 */
export function MobileHomePage() {
  const attention = useAttention()
  const tasks = useAgentTasks()

  const glanceCounts = homeBucketCounts(attention.data?.items ?? [])
  const hasGlance = glanceCounts.blocked + glanceCounts.broken + glanceCounts.review > 0

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

  const cardList = useCards({
    updatedSince: lastSeen ?? new Date(nowMs - 24 * 60 * 60 * 1000).toISOString(),
  })
  const cards = useMemo(() => cardList.data?.cards ?? [], [cardList.data])

  // "Plans that appeared" (spec §D3) — the delta computation stays pure; the catalog is just one
  // more list the caller already holds, and an unresolved root contributes nothing rather than
  // failing the band.
  const planCatalog = usePlanCatalog(null)
  const plans = useMemo(
    () => (planCatalog.data?.rootResolved ? planCatalog.data.plans : []),
    [planCatalog.data],
  )

  const delta = useMemo(
    () => computeAwayDelta(tasks.data ?? [], cards, lastSeen, nowMs, plans),
    [tasks.data, cards, lastSeen, nowMs, plans],
  )

  return (
    <Stack gap={0} maw={480} mx="auto" data-testid="mobile-home">
      {attention.isPending ? (
        <div data-testid="needs-you-skeleton">
          <CardSkeleton />
        </div>
      ) : attention.isError ? (
        <Text size="sm" c="dimmed" px={4} data-testid="needs-you-error">
          Couldn&apos;t load what needs you — retrying.
        </Text>
      ) : !hasGlance ? (
        <CalmCard tasks={inMotion} />
      ) : (
        <Box mt="xs">
          <AttentionGlance />
        </Box>
      )}
      {tasks.isPending ? (
        <BandLoading title="In motion" rows={2} testId="in-motion-skeleton" />
      ) : tasks.isError ? (
        <BandError title="In motion" message="Couldn't load running work." testId="in-motion-error" />
      ) : (
        <InMotionBand tasks={inMotion} />
      )}
      {tasks.isPending ? (
        <BandLoading title="While you were away" testId="away-skeleton" />
      ) : tasks.isError ? (
        <BandError
          title="While you were away"
          message="Couldn't load what happened while you were away."
          testId="away-error"
        />
      ) : (
        <AwayBand delta={delta} />
      )}
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

function BandTitle({
  children,
  color,
  right,
}: {
  children: React.ReactNode
  color?: string
  right?: React.ReactNode
}) {
  const title = (
    <Text
      size="xs"
      fw={700}
      c={color ?? 'dimmed'}
      tt="uppercase"
      mt={right ? undefined : 'md'}
      mb={right ? undefined : 4}
      style={{ letterSpacing: 1 }}
    >
      {children}
    </Text>
  )
  if (!right) return title
  return (
    <Group justify="space-between" align="baseline" wrap="nowrap" mt="md" mb={4}>
      {title}
      {right}
    </Group>
  )
}

function BandLoading({ title, rows = 1, testId }: { title: string; rows?: number; testId: string }) {
  return (
    <>
      <BandTitle>{title}</BandTitle>
      <Stack gap="xs" px={4} data-testid={testId}>
        {Array.from({ length: rows }, (_, index) => (
          <InlineSkeleton key={index} />
        ))}
      </Stack>
    </>
  )
}

function BandError({ title, message, testId }: { title: string; message: string; testId: string }) {
  return (
    <>
      <BandTitle>{title}</BandTitle>
      <Text size="sm" c="dimmed" px={4} data-testid={testId}>
        {message}
      </Text>
    </>
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
  const isEmpty =
    shownTasks.length === 0 && delta.cardChanges.length === 0 && delta.newPlans.length === 0

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
                to="/orchestrator?tab=history"
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
          {delta.newPlans.map((plan) => (
            <Fragment key={plan.relativePath}>
              <Divider />
              <AwayRow
                to={`/plans?file=${encodeURIComponent(plan.relativePath)}`}
                label={plan.title}
                line={`plan — ${
                  plan.cards.length > 0 ? `${plan.cards.map(displayIdentifier).join(' ')} ` : ''
                }${plan.title}`}
                sub={`${formatClockTime(plan.modifiedAt)}${plan.status ? ` · ${plan.status}` : ''}`}
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
      <BandTitle
        right={
          <Anchor
            component={Link}
            to="/orchestrator?tab=pipeline"
            size="xs"
            c="dimmed"
            style={{ flexShrink: 0 }}
          >
            by stage ›
          </Anchor>
        }
      >
        In motion{tasks.length > 0 ? ` · ${tasks.length}` : ''}
      </BandTitle>
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
