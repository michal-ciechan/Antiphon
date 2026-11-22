import { Alert, Anchor, Badge, Box, Button, Chip, Group, Loader, Paper, Stack, Table, Tabs, Text, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbArrowsShuffle } from 'react-icons/tb'
import { Link, useNavigate } from 'react-router'
import { getApiErrorMessage } from '../../api/client'
import type { GateKind, QueueItemDto, QueueScope } from '../../api/reviewEngine'
import { FakeApiBanner } from './components/FakeApiBanner'
import { PersonChip } from './components/PersonChip'
import { GateKindBadge, QueueStatusBadge } from './components/ReviewBadges'
import { NotMeButton } from './queue/NotMeButton'
import { useReviewLiveUpdates } from './reviewApi'
import { GATE_KIND_META, SLA_META } from './reviewMeta'
import { fmtWorkingMinutes, prRef } from './reviewFormat'
import { useAssignGate, usePassGate, useReviewQueue } from './useReview'
import { useReviewUiStore } from './useReviewUiStore'
import { useReviewViewerStore, type ReviewViewer } from './useReviewViewerStore'

const SCOPES: { value: QueueScope; label: string }[] = [
  { value: 'Mine', label: 'Mine' },
  { value: 'Teams', label: 'My teams' },
  { value: 'All', label: 'All' },
]

function SlaCell({ item }: { item: QueueItemDto }) {
  const m = SLA_META[item.slaState]
  return (
    <Tooltip label={`${fmtWorkingMinutes(item.ageWorkingMinutes)} in working time · ${m.label}. Weekends and out-of-hours don't count.`}>
      <Group gap={6} wrap="nowrap">
        <Box w={8} h={8} style={{ borderRadius: 4, background: `var(--mantine-color-${m.color}-6)` }} />
        <Text size="xs" ff="monospace" c={item.slaState === 'Ok' ? 'dimmed' : `${m.color}.4`} style={{ whiteSpace: 'nowrap' }}>
          {fmtWorkingMinutes(item.ageWorkingMinutes)}
          {item.slaState === 'Breached' ? ' · breached' : item.slaState === 'Warn' ? ' · near' : ''}
        </Text>
      </Group>
    </Tooltip>
  )
}

function QueueRow({ item, viewer }: { item: QueueItemDto; viewer: ReviewViewer }) {
  const navigate = useNavigate()
  const assign = useAssignGate()
  const pass = usePassGate()
  const { gate, run } = item
  const isAuthor = run.subject.authorLogin === viewer.login
  const mine = gate.assigneeLogin === viewer.login
  const inTeam = gate.teamKey !== null && viewer.teams.includes(gate.teamKey)
  const notMeAllowed = mine || (gate.assigneeLogin === null && inTeam)
  const fail = (title: string) => (error: unknown) => notifications.show({ color: 'danger', title, message: getApiErrorMessage(error, 'The review API refused it.') })

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
        {gate.passedBy.length > 0 && (
          <Text size="xs" c="warning.4" truncate>
            Passed by {gate.passedBy.map((p) => `@${p}`).join(', ')}
          </Text>
        )}
      </Table.Td>
      <Table.Td>
        <Text size="xs" fw={600}>
          {gate.teamKey ?? '—'}
        </Text>
        <Text size="xs" c="dimmed" truncate>
          {run.subject.owners.join(', ')}
        </Text>
      </Table.Td>
      <Table.Td>
        <PersonChip login={run.subject.authorLogin} displayName={run.subject.authorDisplayName} size="xs" />
        {run.subject.authorResolvedBy && (
          <Tooltip multiline w={300} label={run.subject.authorResolvedBy}>
            <Text size="10px" c="grape.4">
              resolved author
            </Text>
          </Tooltip>
        )}
      </Table.Td>
      <Table.Td>
        <PersonChip login={gate.assigneeLogin} displayName={mine ? viewer.displayName : null} size="xs" />
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
                  onClick={() => assign.mutate({ gateId: gate.id, login: viewer.login }, { onError: fail('Could not take it') })}
                >
                  Take
                </Button>
              </Box>
            </Tooltip>
          )}
          <NotMeButton
            teamKey={gate.teamKey}
            disabled={!notMeAllowed}
            disabledReason={gate.assigneeLogin ? `Assigned to @${gate.assigneeLogin}` : 'Not in your team'}
            loading={pass.isPending}
            onConfirm={(reason) =>
              pass.mutate(
                { gateId: gate.id, reason },
                {
                  onSuccess: () => notifications.show({ color: 'warning', title: `Returned to ${gate.teamKey ?? 'the team'}`, message: `"${reason}". You're excluded from the next pick.` }),
                  onError: fail('Could not pass it back'),
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
                        title: `Assigned to @${g.assigneeLogin}`,
                        message: `Random from ${g.teamKey ?? 'the team'}; excluded the author${g.passedBy.length ? ` and ${g.passedBy.map((p) => `@${p}`).join(', ')}` : ''}.`,
                      }),
                    onError: fail('No one to assign'),
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

/** /review-queue — gates waiting on people: Mine / My teams / All, sorted by SLA pressure. */
export function ReviewQueuePage() {
  useReviewLiveUpdates()
  const viewer = useReviewViewerStore((s) => s.viewer)
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
    <Stack gap="sm" p="md">
      <FakeApiBanner />
      <Group justify="space-between" align="flex-end" wrap="wrap">
        <Box>
          <Text fw={700} size="lg">
            Review queue
          </Text>
          <Text size="xs" c="dimmed">
            {viewer.displayName} · {viewer.teams.join(', ')} · ages in working time · sorted by SLA pressure
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
      {current.error && (
        <Alert color="danger" variant="light" title="Could not load the queue">
          {getApiErrorMessage(current.error, 'The review API did not answer.')}
        </Alert>
      )}
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
              {current.isLoading && (
                <Table.Tr>
                  <Table.Td colSpan={7}>
                    <Group justify="center" py="md">
                      <Loader size="sm" />
                    </Group>
                  </Table.Td>
                </Table.Tr>
              )}
              {rows.map((item) => (
                <QueueRow key={item.gate.id} item={item} viewer={viewer} />
              ))}
              {!current.isLoading && rows.length === 0 && (
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
    </Stack>
  )
}
