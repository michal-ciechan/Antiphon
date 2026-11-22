import type { Meta, StoryObj } from '@storybook/react'
import { useMemo, useState } from 'react'
import {
  Avatar,
  Badge,
  Box,
  Button,
  Chip,
  Divider,
  Group,
  Indicator,
  Paper,
  Popover,
  ScrollArea,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  Textarea,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  TbArrowsShuffle,
  TbCheck,
  TbCircleCheck,
  TbHandStop,
  TbInbox,
  TbSparkles,
  TbUserOff,
  TbUserQuestion,
  TbX,
} from 'react-icons/tb'
import {
  PEOPLE,
  QUEUE,
  QUEUE_KIND_META,
  TEAMS,
  VIEWER,
  blastTrips,
  personName,
  thresholdFor,
  trustOf,
  type QueueItem,
  type QueueKind,
  type QueueState,
  type TeamKey,
} from './mockData'
import { BlastPanel, OwnersLine, PersonChip, SectionLabel, SlaBar } from './shared'

/**
 * PROPOSAL MOCK — not production code. Human queues for
 * `docs/features/012-review-engine/design.md` §11.3 queues, §11.4 routing, §11.5 author
 * resolution: Mine / My teams / Overall, "Not me", random assignment that excludes the author,
 * SLA ageing in working hours. Viewer: Alice Smith (payments-core). Owners come from CODEOWNERS and can
 * be several teams and users; reviewer names carry their trust on the repo, and random assignment
 * skips anyone below the trust threshold for the item (random-assign 30, clear-blocker 60).
 *   A — tabbed table with inline actions
 *   B — inbox list + preview pane, with author resolution shown (AI-resolved and unknown)
 */

const STATE_META: Record<QueueState, { label: string; color: string }> = {
  unassigned: { label: 'unassigned', color: 'gray' },
  assigned: { label: 'assigned', color: 'active' },
  'in-progress': { label: 'in progress', color: 'cyan' },
  'passed-back': { label: 'passed back', color: 'warning' },
  done: { label: 'done', color: 'success' },
}

const NOT_ME_REASONS = ['Not familiar with this area', 'Conflict of interest', 'Out / at capacity today', 'I wrote part of it', 'Other']

/** The trust an assignee needs for this item: clear-blocker for a second-reviewer item, random-assign otherwise. */
function trustNeeded(item: QueueItem): { value: number; label: string } {
  return item.kind === 'blocker-second'
    ? { value: thresholdFor('clear-blocker', item.team).value, label: 'clear-blocker' }
    : { value: thresholdFor('random-assign', item.team).value, label: 'random-assign' }
}

function pickRandom(item: QueueItem): { login: string; excluded: string[]; pool: string[] } | null {
  const author = item.authorResolution?.to ?? item.author
  const need = trustNeeded(item).value
  const lowTrust = Object.values(PEOPLE).filter((p) => p.team === item.team && p.optedIn && (trustOf(p.login, item.repo) ?? 0) < need).map((p) => p.login)
  const excluded = [author, ...(item.passedBy ?? []).map((p) => p.login), ...lowTrust]
  const pool = Object.values(PEOPLE).filter((p) => p.team === item.team && p.optedIn && !excluded.includes(p.login))
  if (pool.length === 0) return null
  // weight by inverse open load
  const weights = pool.map((p) => 1 / (1 + (p.openLoad ?? 0)))
  let r = Math.random() * weights.reduce((a, b) => a + b, 0)
  for (let i = 0; i < pool.length; i++) {
    r -= weights[i]
    if (r <= 0) return { login: pool[i].login, excluded, pool: pool.map((p) => p.login) }
  }
  return { login: pool[pool.length - 1].login, excluded, pool: pool.map((p) => p.login) }
}

function useQueue() {
  const [items, setItems] = useState<QueueItem[]>(() => QUEUE.map((q) => ({ ...q })))
  const update = (id: string, patch: Partial<QueueItem>) => setItems((xs) => xs.map((x) => (x.id === id ? { ...x, ...patch } : x)))
  const assignRandom = (item: QueueItem) => {
    const pick = pickRandom(item)
    if (!pick) {
      notifications.show({ color: 'warning', title: 'No eligible reviewer', message: `Everyone opted in to ${TEAMS[item.team].slug} is excluded.` })
      return
    }
    update(item.id, { assignee: pick.login, state: 'assigned' })
    notifications.show({
      color: 'active',
      title: `${item.id} assigned to ${personName(pick.login)}`,
      message: `Random from ${pick.pool.length} opted-in ${TEAMS[item.team].slug} members with trust ≥ ${trustNeeded(item).value}, weighted by open load. Excluded: ${pick.excluded.map(personName).join(', ')}.`,
    })
  }
  const notMe = (item: QueueItem, reason: string) => {
    update(item.id, { assignee: undefined, state: 'passed-back', passedBy: [...(item.passedBy ?? []), { login: VIEWER.login, reason }] })
    notifications.show({ color: 'warning', title: `${item.id} returned to ${TEAMS[item.team].slug}`, message: `"${reason}". You're excluded from the next pick for this item.` })
  }
  const take = (item: QueueItem) => update(item.id, { assignee: VIEWER.login, state: 'in-progress' })
  return { items, assignRandom, notMe, take, update }
}

function KindBadge({ kind }: { kind: QueueKind }) {
  return (
    <Badge size="xs" variant="light" color={QUEUE_KIND_META[kind].color} radius="sm">
      {QUEUE_KIND_META[kind].label}
    </Badge>
  )
}

function AuthorCell({ item }: { item: QueueItem }) {
  const ar = item.authorResolution
  if (!ar) return <PersonChip login={item.author} size="xs" />
  if (ar.status === 'unknown')
    return (
      <Tooltip label={`${item.author} is a service account. ${ar.via}.`}>
        <Badge size="sm" color="warning" variant="light" leftSection={<TbUserQuestion size={12} />}>
          author unknown
        </Badge>
      </Tooltip>
    )
  return (
    <Tooltip label={`${ar.via} (p=${ar.confidence?.toFixed(2)}). PR opened by ${item.author}.`}>
      <Group gap={4} wrap="nowrap">
        <PersonChip login={ar.to} size="xs" />
        <TbSparkles size={12} color="var(--mantine-color-grape-4)" />
      </Group>
    </Tooltip>
  )
}

function NotMeButton({ item, onConfirm, size = 'compact-xs' }: { item: QueueItem; onConfirm: (reason: string) => void; size?: 'compact-xs' | 'xs' }) {
  const [opened, setOpened] = useState(false)
  const [reason, setReason] = useState<string | null>(NOT_ME_REASONS[0])
  return (
    <Popover opened={opened} onChange={setOpened} width={280} position="bottom-end" withArrow shadow="md">
      <Popover.Target>
        <Button size={size} variant="default" leftSection={<TbHandStop size={12} />} onClick={() => setOpened((o) => !o)} disabled={item.assignee !== VIEWER.login}>
          Not me
        </Button>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap={6}>
          <Text size="xs">
            Return <b>{item.id}</b> to the {TEAMS[item.team].slug} queue. You'll be excluded from its next pick.
          </Text>
          <Select size="xs" data={NOT_ME_REASONS} value={reason} onChange={setReason} />
          <Textarea size="xs" placeholder="Optional note for the team" autosize minRows={1} />
          <Group justify="flex-end" gap={6}>
            <Button size="compact-xs" variant="subtle" color="gray" onClick={() => setOpened(false)}>
              Cancel
            </Button>
            <Button
              size="compact-xs"
              color="warning"
              onClick={() => {
                onConfirm(reason ?? 'Other')
                setOpened(false)
              }}
            >
              Pass back
            </Button>
          </Group>
        </Stack>
      </Popover.Dropdown>
    </Popover>
  )
}

// ---------------------------------------------------------------- (A) tabbed table

function QueueTable() {
  const { items, assignRandom, notMe, take } = useQueue()
  const [tab, setTab] = useState<string | null>('mine')
  const [myTeams, setMyTeams] = useState<string[]>(['payments'])
  const [kinds, setKinds] = useState<string[]>([])

  const sets = useMemo(() => {
    const mine = items.filter((q) => q.assignee === VIEWER.login || (!q.assignee && myTeams.includes(q.team) && !q.passedBy?.some((p) => p.login === VIEWER.login)))
    const teams = items.filter((q) => myTeams.includes(q.team))
    return { mine, teams, overall: items }
  }, [items, myTeams])

  const rows = (sets[(tab ?? 'mine') as keyof typeof sets] ?? []).filter((q) => kinds.length === 0 || kinds.includes(q.kind)).sort((a, b) => b.ageWh / b.slaWh - a.ageWh / a.slaWh)

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Text fw={700} size="lg">
            Queues
          </Text>
          <Text size="xs" c="dimmed">
            Alice Smith · payments-core · ages in working hours (team calendar; clocks paused for the weekend) · sorted by SLA pressure
          </Text>
        </Box>
        <Group gap={6}>
          <Text size="xs" c="dimmed">
            "Mine" includes unassigned from
          </Text>
          <Chip.Group multiple value={myTeams} onChange={setMyTeams}>
            <Group gap={4}>
              {(Object.keys(TEAMS) as TeamKey[]).map((t) => (
                <Chip key={t} value={t} size="xs" variant="outline">
                  {TEAMS[t].slug}
                </Chip>
              ))}
            </Group>
          </Chip.Group>
        </Group>
      </Group>
      <Tabs value={tab} onChange={setTab}>
        <Tabs.List>
          <Tabs.Tab value="mine" rightSection={<Badge size="xs" variant="filled">{sets.mine.length}</Badge>}>
            Mine
          </Tabs.Tab>
          <Tabs.Tab value="teams" rightSection={<Badge size="xs" variant="light" color="gray">{sets.teams.length}</Badge>}>
            My teams
          </Tabs.Tab>
          <Tabs.Tab value="overall" rightSection={<Badge size="xs" variant="light" color="gray">{sets.overall.length}</Badge>}>
            Overall
          </Tabs.Tab>
          <Box ml="auto" pt={4}>
            <Chip.Group multiple value={kinds} onChange={setKinds}>
              <Group gap={4}>
                {(Object.keys(QUEUE_KIND_META) as QueueKind[]).map((k) => (
                  <Chip key={k} value={k} size="xs" color={QUEUE_KIND_META[k].color} variant="light">
                    {QUEUE_KIND_META[k].label}
                  </Chip>
                ))}
              </Group>
            </Chip.Group>
          </Box>
        </Tabs.List>
      </Tabs>
      <Paper withBorder radius="md">
        <Table.ScrollContainer minWidth={1350} type="native">
          <Table verticalSpacing={6} fz="sm" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={130}>Kind</Table.Th>
                <Table.Th>Item</Table.Th>
                <Table.Th w={210}>Team · owners</Table.Th>
                <Table.Th w={150}>Author</Table.Th>
                <Table.Th w={150}>Assignee</Table.Th>
                <Table.Th w={150}>Age / SLA</Table.Th>
                <Table.Th w={320}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((q) => (
                <Table.Tr key={q.id}>
                  <Table.Td>
                    <KindBadge kind={q.kind} />
                    <Text size="10px" c="dimmed" ff="monospace" mt={2}>
                      {q.id}
                    </Text>
                  </Table.Td>
                  <Table.Td maw={420}>
                    <Text size="sm" fw={500} truncate>
                      {q.title}
                    </Text>
                    <Group gap={6} wrap="nowrap">
                      <Text size="xs" ff="monospace" c="dimmed" truncate>
                        {q.subject}
                      </Text>
                      <Tooltip multiline w={320} label={q.why.join(' · ')}>
                        <Text size="xs" c="pink.4" truncate>
                          {q.why[0]}
                          {q.why.length > 1 && ` +${q.why.length - 1}`}
                        </Text>
                      </Tooltip>
                    </Group>
                    {q.passedBy && q.passedBy.length > 0 && (
                      <Text size="xs" c="warning.4" truncate>
                        Passed by {q.passedBy.map((p) => `${personName(p.login)} ("${p.reason}")`).join(', ')}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" fw={600}>
                      {TEAMS[q.team].slug}
                    </Text>
                    <OwnersLine owners={q.owners} />
                  </Table.Td>
                  <Table.Td>
                    <AuthorCell item={q} />
                  </Table.Td>
                  <Table.Td>
                    <PersonChip login={q.assignee} size="xs" trustRepo={q.repo} />
                    <Badge size="xs" variant="dot" color={STATE_META[q.state].color} mt={2}>
                      {STATE_META[q.state].label}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <SlaBar ageWh={q.ageWh} slaWh={q.slaWh} width={60} />
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <Button size="compact-xs" variant="light">
                        Open
                      </Button>
                      {q.assignee !== VIEWER.login && (
                        <Button size="compact-xs" variant="default" onClick={() => take(q)} disabled={q.author === VIEWER.login || q.authorResolution?.to === VIEWER.login}>
                          Take
                        </Button>
                      )}
                      <NotMeButton item={q} onConfirm={(r) => notMe(q, r)} />
                      <Tooltip label="Random pick among opted-in team members with enough trust on the repo, weighted by open load. Excludes the author (or resolved author) and anyone who passed.">
                        <Button size="compact-xs" variant="default" leftSection={<TbArrowsShuffle size={12} />} onClick={() => assignRandom(q)}>
                          Assign random
                        </Button>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
              {rows.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={7}>
                    <Text size="sm" c="dimmed" ta="center" py="md">
                      Nothing waiting on you.
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Paper>
      <Text size="xs" c="dimmed">
        "Take" is disabled on your own PRs. "Assign random" shows who was excluded and why. Passes and reassignments count in stats as passes, not as decisions.
      </Text>
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) inbox + preview

function AuthorResolutionCard({ item, onSet }: { item: QueueItem; onSet: (login: string) => void }) {
  const ar = item.authorResolution
  const [choice, setChoice] = useState<string | null>(null)
  if (!ar)
    return (
      <Group gap={6}>
        <PersonChip login={item.author} size="xs" />
        <Text size="xs" c="dimmed">
          PR author (human account)
        </Text>
      </Group>
    )
  const teamPeople = Object.values(PEOPLE).filter((p) => p.team === item.team && !p.service)
  return (
    <Paper
      p="xs"
      radius="sm"
      withBorder
      style={{ borderColor: ar.status === 'unknown' ? 'var(--mantine-color-warning-7)' : 'var(--mantine-color-grape-8)' }}
    >
      <Group gap={6} mb={6} wrap="nowrap">
        {ar.status === 'unknown' ? <TbUserQuestion size={16} color="var(--mantine-color-warning-4)" /> : <TbSparkles size={16} color="var(--mantine-color-grape-4)" />}
        <Box>
          <Text size="sm" fw={600}>
            {ar.status === 'unknown' ? 'Author unknown' : <>Author: {personName(ar.to)}</>}
          </Text>
          <Text size="xs" c="dimmed">
            {ar.status === 'unknown'
              ? `Opened by service account ${item.author}. Nothing resolved a human, so findings went to the team queue.`
              : `${ar.via} · p=${ar.confidence?.toFixed(2)} · PR opened by ${item.author}`}
          </Text>
        </Box>
      </Group>
      <Table fz="xs" verticalSpacing={2} horizontalSpacing={4}>
        <Table.Tbody>
          {ar.signals.map((s) => (
            <Table.Tr key={s.name}>
              <Table.Td w={20}>
                {s.ok === true ? (
                  <TbCheck size={12} color="var(--mantine-color-success-5)" />
                ) : s.ok === false ? (
                  <TbX size={12} color="var(--mantine-color-dark-2)" />
                ) : (
                  <Text size="xs" c="warning.4">
                    ?
                  </Text>
                )}
              </Table.Td>
              <Table.Td>
                <Text size="xs" fw={s.ok ? 600 : 400}>
                  {s.name}
                </Text>
              </Table.Td>
              <Table.Td>
                <Text size="xs" c="dimmed">
                  {s.detail}
                </Text>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      {ar.status === 'unknown' ? (
        <Group gap={6} mt={8} wrap="nowrap">
          <Select size="xs" placeholder="Set author…" data={teamPeople.map((p) => ({ value: p.login, label: p.name }))} value={choice} onChange={setChoice} style={{ flex: 1 }} />
          <Button size="xs" variant="light" disabled={!choice} onClick={() => choice && onSet(choice)}>
            Set author
          </Button>
          <Button size="xs" variant="default" onClick={() => onSet(VIEWER.login)}>
            It's me
          </Button>
        </Group>
      ) : (
        <Group gap={6} mt={6}>
          <Text size="xs" c="dimmed">
            The deciding signal is recorded on the run.
          </Text>
          <Button size="compact-xs" variant="subtle" color="gray">
            Wrong person?
          </Button>
        </Group>
      )}
    </Paper>
  )
}

function slaGroup(q: QueueItem): string {
  const p = q.ageWh / q.slaWh
  if (p >= 1) return 'Over SLA'
  if (p >= 0.6) return 'Due soon'
  return 'Later'
}

function Inbox({ initial }: { initial: string }) {
  const { items, assignRandom, notMe, take, update } = useQueue()
  const [sel, setSel] = useState(initial)
  const [read, setRead] = useState<string[]>([initial])
  const [scope, setScope] = useState<'mine' | 'teams' | 'overall'>('teams')
  const list = items
    .filter((q) => (scope === 'overall' ? true : scope === 'teams' ? ['payments', 'platform'].includes(q.team) : q.assignee === VIEWER.login))
    .sort((a, b) => b.ageWh / b.slaWh - a.ageWh / a.slaWh)
  const groups = ['Over SLA', 'Due soon', 'Later'].map((g) => ({ g, items: list.filter((q) => slaGroup(q) === g) })).filter((x) => x.items.length)
  const q = items.find((x) => x.id === sel) ?? list[0]

  return (
    <Box style={{ display: 'grid', gridTemplateColumns: '400px minmax(0, 1fr)', gap: 12, height: 'calc(100vh - 80px)', minHeight: 620 }}>
      <Paper withBorder radius="md" style={{ display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        <Group justify="space-between" p="xs" style={{ borderBottom: '1px solid var(--mantine-color-dark-4)' }}>
          <Group gap={6}>
            <TbInbox size={18} />
            <Text fw={700}>Review inbox</Text>
          </Group>
          <Chip.Group value={scope} onChange={(v) => setScope(v as typeof scope)}>
            <Group gap={4}>
              <Chip value="mine" size="xs">
                Mine
              </Chip>
              <Chip value="teams" size="xs">
                My teams
              </Chip>
              <Chip value="overall" size="xs">
                Overall
              </Chip>
            </Group>
          </Chip.Group>
        </Group>
        <ScrollArea style={{ flex: 1 }}>
          {groups.map(({ g, items: gi }) => (
            <Box key={g}>
              <Text size="xs" fw={700} c={g === 'Over SLA' ? 'danger.4' : g === 'Due soon' ? 'warning.4' : 'dimmed'} tt="uppercase" px="xs" pt={8} pb={2} style={{ letterSpacing: 0.8 }}>
                {g} · {gi.length}
              </Text>
              {gi.map((x) => {
                const unread = !read.includes(x.id)
                return (
                  <UnstyledButton
                    key={x.id}
                    w="100%"
                    px="xs"
                    py={6}
                    onClick={() => {
                      setSel(x.id)
                      setRead((r) => [...r, x.id])
                    }}
                    style={{
                      background: sel === x.id ? 'var(--mantine-color-dark-5)' : undefined,
                      borderLeft: `3px solid ${sel === x.id ? 'var(--mantine-color-active-5)' : 'transparent'}`,
                    }}
                  >
                    <Group gap={8} wrap="nowrap" align="flex-start">
                      <Indicator disabled={!unread} size={7} offset={2} color="active">
                        <Avatar size={26} radius="xl" color={x.assignee ? 'active' : 'gray'}>
                          {x.assignee ? (
                            <Text size="9px" fw={700}>
                              {personName(x.assignee).split(' ').map((s) => s[0]).join('')}
                            </Text>
                          ) : (
                            <TbUserOff size={13} />
                          )}
                        </Avatar>
                      </Indicator>
                      <Box style={{ flex: 1, minWidth: 0 }}>
                        <Group gap={6} justify="space-between" wrap="nowrap">
                          <KindBadge kind={x.kind} />
                          <Text size="xs" ff="monospace" c={x.ageWh >= x.slaWh ? 'danger.4' : 'dimmed'}>
                            {x.ageWh.toFixed(1)} wh
                          </Text>
                        </Group>
                        <Text size="sm" fw={unread ? 700 : 400} truncate mt={2}>
                          {x.title}
                        </Text>
                        <Group gap={6} wrap="nowrap">
                          <Text size="xs" c="dimmed" ff="monospace" truncate>
                            {x.subject}
                          </Text>
                          {x.authorResolution?.status === 'unknown' && (
                            <Badge size="xs" color="warning" variant="light">
                              author unknown
                            </Badge>
                          )}
                          {x.authorResolution?.status === 'resolved' && (
                            <Tooltip label={x.authorResolution.via}>
                              <Badge size="xs" color="grape" variant="light" leftSection={<TbSparkles size={9} />}>
                                AI-resolved
                              </Badge>
                            </Tooltip>
                          )}
                        </Group>
                      </Box>
                    </Group>
                  </UnstyledButton>
                )
              })}
            </Box>
          ))}
        </ScrollArea>
      </Paper>

      {q && (
        <Paper withBorder radius="md" p="md" style={{ overflow: 'auto' }}>
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Box style={{ minWidth: 0 }}>
              <Group gap={6}>
                <KindBadge kind={q.kind} />
                <Badge size="xs" variant="dot" color={STATE_META[q.state].color}>
                  {STATE_META[q.state].label}
                </Badge>
                <Text size="xs" ff="monospace" c="dimmed">
                  {q.id} · {TEAMS[q.team].slug}
                </Text>
              </Group>
              <Text fw={700} size="lg" mt={4}>
                {q.title}
              </Text>
              <Text size="xs" ff="monospace" c="dimmed">
                {q.subject}
              </Text>
            </Box>
            <Stack gap={4} align="flex-end">
              <SlaBar ageWh={q.ageWh} slaWh={q.slaWh} width={120} />
              <PersonChip login={q.assignee} size="xs" trustRepo={q.repo} />
            </Stack>
          </Group>
          <Box mt={6}>
            <OwnersLine owners={q.owners} label="Owners (CODEOWNERS)" />
          </Box>

          <Group gap={6} mt="sm">
            <Button size="xs">Open review</Button>
            {q.assignee !== VIEWER.login && (
              <Button size="xs" variant="light" leftSection={<TbCircleCheck size={14} />} onClick={() => take(q)} disabled={q.author === VIEWER.login}>
                Take it
              </Button>
            )}
            <NotMeButton item={q} onConfirm={(r) => notMe(q, r)} size="xs" />
            <Button size="xs" variant="default" leftSection={<TbArrowsShuffle size={14} />} onClick={() => assignRandom(q)}>
              Assign random (excludes author)
            </Button>
          </Group>

          <Divider my="sm" />
          <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 280px', gap: 16 }}>
            <Stack gap="sm">
              <Box>
                <SectionLabel>Why this needs a human</SectionLabel>
                <Stack gap={2}>
                  {q.why.map((w) => (
                    <Text key={w} size="sm">
                      • {w}
                    </Text>
                  ))}
                </Stack>
              </Box>
              <Box>
                <SectionLabel>Author</SectionLabel>
                <AuthorResolutionCard
                  item={q}
                  onSet={(login) => {
                    update(q.id, { authorResolution: { status: 'resolved', to: login, via: `Set by ${VIEWER.name}`, confidence: 1, signals: q.authorResolution?.signals ?? [] } })
                    notifications.show({ color: 'success', title: 'Author set', message: `${personName(login)} now gets this PR's findings in their queue. Recorded as a manual override.` })
                  }}
                />
              </Box>
              <Box>
                <SectionLabel>Routing</SectionLabel>
                <Text size="sm">{q.routing}</Text>
                <Text size="xs" c="dimmed" mt={2}>
                  Assignable here: trust ≥ {trustNeeded(q).value} on {q.repo} ({trustNeeded(q).label} threshold, Trust settings).
                </Text>
                {q.passedBy && q.passedBy.length > 0 && (
                  <Stack gap={2} mt={4}>
                    {q.passedBy.map((p) => (
                      <Text key={p.login + p.reason} size="xs" c="warning.4">
                        ↩ {personName(p.login)} passed: "{p.reason}" (excluded from the next pick)
                      </Text>
                    ))}
                  </Stack>
                )}
              </Box>
              {q.openFindings !== undefined && (
                <Box>
                  <SectionLabel>Findings</SectionLabel>
                  <Text size="sm">
                    {q.openFindings} open. {q.authorResolution?.status === 'unknown' ? 'They are waiting in the team queue until someone owns the PR.' : `They are also in ${personName(q.authorResolution?.to ?? q.author)}'s queue.`}
                  </Text>
                </Box>
              )}
            </Stack>
            <Stack gap="sm">
              {q.blast && (
                <Paper withBorder p="xs" radius="md">
                  <SectionLabel>Blast radius</SectionLabel>
                  <BlastPanel blast={q.blast} trips={blastTrips(q.blast)} compact />
                </Paper>
              )}
              <Paper withBorder p="xs" radius="md">
                <SectionLabel>{TEAMS[q.team].slug} · opted in</SectionLabel>
                <Stack gap={4}>
                  {Object.values(PEOPLE)
                    .filter((p) => p.team === q.team)
                    .map((p) => {
                      const isAuthor = p.login === (q.authorResolution?.to ?? q.author)
                      const passed = q.passedBy?.some((x) => x.login === p.login)
                      const t = trustOf(p.login, q.repo)
                      const low = t !== undefined && t < trustNeeded(q).value
                      return (
                        <Group key={p.login} justify="space-between" wrap="nowrap">
                          <PersonChip login={p.login} size="xs" trustRepo={q.repo} />
                          <Text size="xs" c={isAuthor || passed || !p.optedIn || low ? 'dimmed' : undefined} ta="right">
                            {isAuthor ? 'author · excluded' : passed ? 'passed · excluded' : !p.optedIn ? 'not opted in' : low ? `trust < ${trustNeeded(q).value} · excluded` : `${p.openLoad} open`}
                          </Text>
                        </Group>
                      )
                    })}
                </Stack>
              </Paper>
            </Stack>
          </Box>
        </Paper>
      )}
    </Box>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Queues',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Tabs Mine / My teams / Overall; Take, Not me (with reason), Assign random (excludes author and passers); SLA in working hours. */
export const TabbedTable: Story = { name: 'A · Tabbed table', render: () => <QueueTable /> }

/** (B) Inbox + preview. Opens on a service-account PR whose author could not be resolved ("author unknown"). */
export const InboxAuthorUnknown: Story = { name: 'B · Inbox (author unknown)', render: () => <Inbox initial="Q-5521" /> }

/** (B) The same inbox on a hotfix whose author was resolved by AI from a Co-authored-by trailer. */
export const InboxAuthorResolved: Story = { name: 'B · Inbox (AI-resolved author)', render: () => <Inbox initial="Q-5502" /> }

/** (B) The queue item a low-trust "Not a blocker" creates (Kenji, Trust 41 < 60 on F1): only owners with trust ≥ 60 on payments-api are assignable; the author is excluded. */
export const InboxBlockerSecondReviewer: Story = { name: 'B · Inbox (blocker: second trusted reviewer)', render: () => <Inbox initial="Q-5523" /> }
