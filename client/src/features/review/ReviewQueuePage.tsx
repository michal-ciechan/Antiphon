import { Anchor, Badge, Box, Button, Chip, Group, Paper, Skeleton, Stack, Table, Tabs, Text, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbArrowsShuffle } from 'react-icons/tb'
import { Link, useNavigate } from 'react-router'
import type { GateKind, QueueItemDto, QueueScope, ReviewViewerDto } from '../../api/reviewEngine'
import { FakeApiBanner } from './components/FakeApiBanner'
import { GatePasses } from './components/GatePasses'
import { OwnerChips } from './components/OwnerChips'
import { PersonChip } from './components/PersonChip'
import { GateKindBadge, QueueStatusBadge } from './components/ReviewBadges'
import { QueryErrorAlert } from './components/ReviewProblem'
import { NotMeButton } from './queue/NotMeButton'
import { useReviewLiveUpdates } from './reviewApi'
import { GATE_KIND_META, SLA_META } from './reviewMeta'
import { fmtWorkingMinutes, personLabel, prRef, slaPressure } from './reviewFormat'
import { useNow } from './useNow'
import { useAssignGate, usePassGate, useReviewMe, useReviewQueue } from './useReview'
import { useReviewUiStore } from './useReviewUiStore'

const SCOPES: { value: QueueScope; label: string }[] = [
  { value: 'Mine', label: 'Mine' },
  { value: 'Teams', label: 'My teams' },
  { value: 'All', label: 'All' },
]

/** Age against the team's SLA, both in working time: the colour comes from `ageWorkingMinutes / slaWorkingMinutes`. */
function SlaCell({ item }: { item: QueueItemDto }) {
  const { ratio, state } = slaPressure(item.ageWorkingMinutes, item.slaWorkingMinutes, item.slaState)
  const m = SLA_META[state]
  const of = item.slaWorkingMinutes > 0 ? ` of ${fmtWorkingMinutes(item.slaWorkingMinutes)} SLA (${Math.round((ratio ?? 0) * 100)}%)` : ''
  return (
    <Tooltip label={`${fmtWorkingMinutes(item.ageWorkingMinutes)} in working time${of} · ${m.label}. Weekends and out-of-hours don't count.`}>
      <Box>
        <Group gap={6} wrap="nowrap">
          <Box w={8} h={8} style={{ borderRadius: 4, background: `var(--mantine-color-${m.color}-6)` }} />
          <Text size="xs" ff="monospace" c={state === 'Ok' ? 'dimmed' : `${m.color}.4`} style={{ whiteSpace: 'nowrap' }}>
            {fmtWorkingMinutes(item.ageWorkingMinutes)}
            {item.slaWorkingMinutes > 0 && ` / ${fmtWorkingMinutes(item.slaWorkingMinutes)}`}
            {state === 'Breached' ? ' · breached' : state === 'Warn' ? ' · near' : ''}
          </Text>
        </Group>
        {ratio !== null && (
          <Box mt={3} h={3} w={96} style={{ borderRadius: 2, background: 'var(--mantine-color-dark-4)', overflow: 'hidden' }}>
            <Box h="100%" w={`${Math.min(100, ratio * 100)}%`} style={{ background: `var(--mantine-color-${m.color}-6)` }} />
          </Box>
        )}
      </Box>
    </Tooltip>
  )
}

function QueueRow({ item, viewer, now }: { item: QueueItemDto; viewer: ReviewViewerDto; now: number }) {
  const navigate = useNavigate()
  const assign = useAssignGate()
  const pass = usePassGate()
  const { gate, run } = item
  const isAuthor = run.subject.authorLogin === viewer.login
  const mine = gate.assigneeLogin === viewer.login
  const inTeam = gate.teamKey !== null && viewer.teams.includes(gate.teamKey)
  const notMeAllowed = mine || (gate.assigneeLogin === null && inTeam)

  return (
    <Table.Tr>
      <Table.Td style={{ minWidth: 130 }}>
        <GateKindBadge kind={gate.kind} />
      </Table.Td>
      <Table.Td maw={440}>
        <Anchor component={Link} to={`/reviews/${run.id}`} size="sm" fw={500} c="gray.1" truncate display="block">
          {run.subject.title}
        </Anchor>
        <Group gap={6} wrap="nowrap">
          <Text size="xs" ff="monospace" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            {prRef(run.subject)} · R{run.round}
          </Text>
          {gate.reasons.length > 0 && (
            <Tooltip multiline w={340} label={gate.reasons.join(' · ')}>
              <Text size="xs" c="pink.4" truncate>
                {gate.reasons[0]}
                {gate.reasons.length > 1 && ` +${gate.reasons.length - 1}`}
              </Text>
            </Tooltip>
          )}
        </Group>
        <GatePasses passes={gate.passes} now={now} note={false} />
      </Table.Td>
      <Table.Td>
        <Text size="xs" fw={600}>
          {gate.teamKey ?? '—'}
        </Text>
        <OwnerChips owners={run.subject.owners} max={3} />
      </Table.Td>
      <Table.Td>
        <PersonChip login={run.subject.authorLogin} displayName={run.subject.authorDisplayName} size="xs" />
        {(run.subject.authorResolvedBy || run.subject.openedByLogin !== run.subject.authorLogin) && (
          <Tooltip
            multiline
            w={300}
            label={`${run.subject.openedByLogin !== run.subject.authorLogin ? `Opened by @${run.subject.openedByLogin}. ` : ''}Author resolved via ${run.subject.authorResolvedBy ?? 'an unknown rule'}.`}
          >
            <Text size="10px" c="grape.4">
              {run.subject.openedByLogin !== run.subject.authorLogin ? `opened by @${run.subject.openedByLogin}` : 'resolved author'}
            </Text>
          </Tooltip>
        )}
      </Table.Td>
      <Table.Td>
        <PersonChip login={gate.assigneeLogin} displayName={gate.assigneeDisplayName} size="xs" />
        <QueueStatusBadge status={gate.status} />
      </Table.Td>
      <Table.Td>
        <SlaCell item={item} />
      </Table.Td>
      <Table.Td>
        <Group gap={4} wrap="nowrap">
          <Button size="compact-xs" variant="light" onClick={() => navigate(`/reviews/${run.id}`)}>
            Open
          </Button>
          {!mine && (
            <Tooltip label={isAuthor ? "You're the author: you can't take your own gate." : 'Assign it to yourself'}>
              <Box>
                <Button
                  size="compact-xs"
                  variant="default"
                  disabled={isAuthor}
                  loading={assign.isPending && assign.variables?.login === viewer.login}
                  onClick={() => assign.mutate({ gateId: gate.id, login: viewer.login })}
                >
                  Take
                </Button>
              </Box>
            </Tooltip>
          )}
          <NotMeButton
            teamKey={gate.teamKey}
            disabled={!notMeAllowed}
            disabledReason={gate.assigneeLogin ? `Assigned to ${personLabel(gate.assigneeLogin, gate.assigneeDisplayName)}` : 'Not in your team'}
            loading={pass.isPending}
            onConfirm={(reason) =>
              pass.mutate(
                { gateId: gate.id, reason },
                {
                  onSuccess: () => notifications.show({ color: 'warning', title: `Returned to ${gate.teamKey ?? 'the team'}`, message: `"${reason}". You're excluded from the next pick.` }),
                },
              )
            }
          />
          <Tooltip multiline w={300} label="Random pick from the owning team, excluding the author and anyone who passed.">
            <Button
              size="compact-xs"
              variant="default"
              leftSection={<TbArrowsShuffle size={12} />}
              loading={assign.isPending && !assign.variables?.login}
              onClick={() =>
                assign.mutate(
                  { gateId: gate.id },
                  {
                    onSuccess: (g) =>
                      notifications.show({
                        color: 'active',
                        title: `Assigned to ${personLabel(g.assigneeLogin, g.assigneeDisplayName)}`,
                        message: `Random from ${g.teamKey ?? 'the team'}; excluded the author${g.passes.length ? ` and ${[...new Set(g.passes.map((p) => `@${p.login}`))].join(', ')}` : ''}.`,
                      }),
                  },
                )
              }
            >
              Assign random
            </Button>
          </Tooltip>
        </Group>
      </Table.Td>
    </Table.Tr>
  )
}

function QueueBody({ viewer }: { viewer: ReviewViewerDto }) {
  const now = useNow()
  const scope = useReviewUiStore((s) => s.queueScope)
  const setScope = useReviewUiStore((s) => s.setQueueScope)
  const [kinds, setKinds] = useState<string[]>([])
  const mine = useReviewQueue('Mine', viewer.login)
  const teams = useReviewQueue('Teams', viewer.login)
  const all = useReviewQueue('All', viewer.login)
  const current = scope === 'Mine' ? mine : scope === 'Teams' ? teams : all
  const counts: Record<QueueScope, number | undefined> = { Mine: mine.data?.total, Teams: teams.data?.total, All: all.data?.total }
  const rows = useMemo(() => (current.data?.items ?? []).filter((i) => kinds.length === 0 || kinds.includes(i.gate.kind)), [current.data, kinds])

  return (
    <>
      <Group justify="space-between" align="flex-end" wrap="wrap">
        <Box>
          <Text fw={700} size="lg">
            Review queue
          </Text>
          <Text size="xs" c="dimmed">
            {personLabel(viewer.login, viewer.displayName)} · {viewer.teams.length ? viewer.teams.join(', ') : 'no teams'} · ages in working time · sorted by SLA pressure
          </Text>
        </Box>
        <Chip.Group multiple value={kinds} onChange={setKinds}>
          <Group gap={4}>
            {(Object.keys(GATE_KIND_META) as GateKind[]).map((k) => (
              <Chip key={k} value={k} size="xs" color={GATE_KIND_META[k].color} variant="light">
                {GATE_KIND_META[k].label}
              </Chip>
            ))}
          </Group>
        </Chip.Group>
      </Group>
      <Tabs value={scope} onChange={(v) => v && setScope(v as QueueScope)}>
        <Tabs.List>
          {SCOPES.map((s) => (
            <Tabs.Tab key={s.value} value={s.value} rightSection={<Badge size="xs" variant={s.value === 'Mine' ? 'filled' : 'light'} color={s.value === 'Mine' ? 'active' : 'gray'}>{counts[s.value] ?? '·'}</Badge>}>
              {s.label}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>
      {current.error && <QueryErrorAlert title="Could not load the queue" error={current.error} onRetry={() => void current.refetch()} retrying={current.isFetching} />}
      <Paper withBorder radius="md">
        <Table.ScrollContainer minWidth={1180} type="native">
          <Table verticalSpacing={6} fz="sm" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={140}>Kind</Table.Th>
                <Table.Th>Item</Table.Th>
                <Table.Th w={170}>Team · owners</Table.Th>
                <Table.Th w={150}>Author</Table.Th>
                <Table.Th w={150}>Assignee</Table.Th>
                <Table.Th w={140}>Age / SLA</Table.Th>
                <Table.Th w={330}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {current.isLoading &&
                [0, 1, 2].map((i) => (
                  <Table.Tr key={i}>
                    <Table.Td colSpan={7}>
                      <Skeleton h={28} radius="sm" />
                    </Table.Td>
                  </Table.Tr>
                ))}
              {rows.map((item) => (
                <QueueRow key={item.gate.id} item={item} viewer={viewer} now={now} />
              ))}
              {!current.isLoading && !current.error && rows.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={7}>
                    <Text size="sm" c="dimmed" ta="center" py="md">
                      {scope === 'Mine' ? 'Nothing waiting on you.' : 'Nothing in this queue.'}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Paper>
      <Text size="xs" c="dimmed">
        "Mine" is what is assigned to you plus unassigned items from your teams that you haven't passed. "Take" is disabled on your own PRs. Passes and
        reassignments count as passes in stats, not as decisions.
      </Text>
    </>
  )
}

/** /review-queue — gates waiting on people: Mine / My teams / All, sorted by SLA pressure, for the `GET /api/review-me` viewer. */
export function ReviewQueuePage() {
  useReviewLiveUpdates()
  const me = useReviewMe()
  return (
    <Stack gap="sm" p="md">
      <FakeApiBanner />
      {me.isLoading ? (
        <Stack gap="sm">
          <Skeleton h={40} w={320} radius="sm" />
          <Skeleton h={32} radius="sm" />
          <Skeleton h={160} radius="md" />
        </Stack>
      ) : me.data ? (
        <QueueBody viewer={me.data} />
      ) : (
        <QueryErrorAlert
          title="Could not tell who you are"
          error={me.error}
          fallback="GET /api/review-me did not answer, so the queue cannot be scoped to you."
          onRetry={() => void me.refetch()}
          retrying={me.isFetching}
        />
      )}
    </Stack>
  )
}
