import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import {
  Alert,
  Badge,
  Box,
  Group,
  Paper,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Tooltip,
} from '@mantine/core'
import { TbAlertTriangle, TbEyeOff, TbFlag, TbInfoCircle, TbTrophy } from 'react-icons/tb'
import { DAILY, REVIEWER_STATS, fmtUsd, outcomeScore, personName, type ReviewerStat } from './mockData'
import { Kpi, PersonChip, SectionLabel, Spark } from './shared'

/**
 * PROPOSAL MOCK — not production code. Reviewer stats for
 * `docs/features/012-review-engine/design.md` §15: daily / weekly / monthly rollups, a team
 * leaderboard that ranks outcomes, not volume, the private rubber-stamp indicator, and wrong
 * dismissals counting against the person who dismissed (§11.6).
 *   A — team leaderboard (payments-core), outcome columns scored, volume columns shown but greyed
 *   B — one person's card as their team lead sees it, with the private panel
 *   C — team health over time (backlog, time to decision, accepted vs dismissed, cost)
 */

type Period = 'day' | 'week' | 'month'
const PERIOD_LABEL: Record<Period, string> = { day: 'Fri 20 Nov', week: 'Week 47 (16–20 Nov)', month: 'November 2026 (to date)' }
const PERIOD_DIV: Record<Period, number> = { day: 14, week: 3.2, month: 1 }

function scaled(s: ReviewerStat, p: Period): ReviewerStat {
  const d = PERIOD_DIV[p]
  const r = (v: number) => Math.round(v / d)
  return {
    ...s,
    gatesDecided: r(s.gatesDecided),
    sentBack: r(s.sentBack),
    steers: r(s.steers),
    suggestionsMerged: r(s.suggestionsMerged),
    humanIssuesConfirmed: r(s.humanIssuesConfirmed),
    learningsMerged: r(s.learningsMerged),
    learningsPromoted: r(s.learningsPromoted),
    disputesWon: r(s.disputesWon),
    disputesLost: r(s.disputesLost),
    wrongDismissals: p === 'month' ? s.wrongDismissals : p === 'week' ? s.wrongDismissals : 0,
    comments: r(s.comments),
    words: r(s.words),
    rubberStamps: p === 'day' ? Math.min(1, s.rubberStamps) : s.rubberStamps,
  }
}

function PeriodToggle({ value, onChange }: { value: Period; onChange: (p: Period) => void }) {
  return (
    <SegmentedControl
      size="xs"
      value={value}
      onChange={(v) => onChange(v as Period)}
      data={[
        { value: 'day', label: 'Daily' },
        { value: 'week', label: 'Weekly' },
        { value: 'month', label: 'Monthly' },
      ]}
    />
  )
}

const SCORE_HELP =
  'Outcome score = 2 × suggestions merged + 5 × human-raised issues confirmed + 4 × learnings merged + 6 × learnings promoted + 2 × disputes won − 1 × disputes lost − 8 × wrong dismissals. Comments, words and gate counts never score on their own.'

// ---------------------------------------------------------------- (A) leaderboard

function Num({ v, neg = false, strong = false }: { v: number; neg?: boolean; strong?: boolean }) {
  return (
    <Text size="sm" ff="monospace" ta="right" fw={strong ? 700 : 400} c={v === 0 ? 'dimmed' : neg ? 'danger.4' : undefined}>
      {neg && v > 0 ? `−${v}` : v}
    </Text>
  )
}

function Leaderboard() {
  const [period, setPeriod] = useState<Period>('month')
  const [asLead, setAsLead] = useState(true)
  const rows = REVIEWER_STATS.map((s) => scaled(s, period))
    .map((s) => ({ s, score: outcomeScore(s) }))
    .sort((a, b) => b.score - a.score)
  const max = Math.max(...rows.map((r) => r.score), 1)
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Group gap={6}>
            <TbTrophy size={18} color="var(--mantine-color-yellow-5)" />
            <Text fw={700} size="lg">
              payments-core · reviewer outcomes
            </Text>
          </Group>
          <Text size="xs" c="dimmed">
            {PERIOD_LABEL[period]} · ranked by outcome score, not volume · durations in working hours
          </Text>
        </Box>
        <Group gap="sm">
          <SegmentedControl
            size="xs"
            value={asLead ? 'lead' : 'member'}
            onChange={(v) => setAsLead(v === 'lead')}
            data={[
              { value: 'lead', label: 'As team lead (Hugo)' },
              { value: 'member', label: 'As member (Alice)' },
            ]}
          />
          <PeriodToggle value={period} onChange={setPeriod} />
        </Group>
      </Group>
      <Paper withBorder radius="md">
        <Table.ScrollContainer minWidth={1300} type="native">
          <Table verticalSpacing={6} fz="sm" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={30}>#</Table.Th>
                <Table.Th w={160}>Reviewer</Table.Th>
                <Table.Th w={190}>
                  <Tooltip multiline w={360} label={SCORE_HELP}>
                    <Group gap={4}>
                      Outcome score <TbInfoCircle size={12} />
                    </Group>
                  </Tooltip>
                </Table.Th>
                <Table.Th ta="right">Suggestions merged</Table.Th>
                <Table.Th ta="right">Human issues confirmed</Table.Th>
                <Table.Th ta="right">Learnings merged / promoted</Table.Th>
                <Table.Th ta="right">Disputes won / lost</Table.Th>
                <Table.Th ta="right">
                  <Tooltip multiline w={300} label="A dismissed finding later linked to a bug or incident by a Debug or why-missed investigation. −8 each.">
                    <span>Wrong dismissals</span>
                  </Tooltip>
                </Table.Th>
                <Table.Th ta="right">Decision p50 / p90</Table.Th>
                <Table.Th ta="right" c="dimmed">
                  Gates
                </Table.Th>
                <Table.Th ta="right" c="dimmed">
                  Comments · words
                </Table.Th>
                <Table.Th w={70}>
                  <Tooltip multiline w={280} label="Fast approvals of large PRs. Shown to the person and their team lead only. Never ranked.">
                    <Group gap={4} wrap="nowrap">
                      <TbEyeOff size={12} /> Private
                    </Group>
                  </Tooltip>
                </Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map(({ s, score }, i) => {
                const canSeePrivate = asLead || s.login === 'asmith'
                return (
                  <Table.Tr key={s.login} bg={s.login === 'asmith' ? 'rgba(59,123,245,0.07)' : undefined}>
                    <Table.Td>
                      <Text fw={700} c={i === 0 ? 'yellow.5' : 'dimmed'}>
                        {i + 1}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <PersonChip login={s.login} size="xs" />
                    </Table.Td>
                    <Table.Td>
                      <Group gap={6} wrap="nowrap">
                        <Box w={110} h={8} bg="dark.5" style={{ borderRadius: 4, overflow: 'hidden' }}>
                          <Box h={8} w={`${Math.max(0, (score / max) * 100)}%`} bg={score < 0 ? 'danger.6' : 'success.6'} />
                        </Box>
                        <Text size="sm" ff="monospace" fw={700}>
                          {score}
                        </Text>
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Num v={s.suggestionsMerged} />
                    </Table.Td>
                    <Table.Td>
                      <Num v={s.humanIssuesConfirmed} />
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="monospace" ta="right">
                        {s.learningsMerged} / {s.learningsPromoted}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="monospace" ta="right">
                        {s.disputesWon} /{' '}
                        <Text span c={s.disputesLost ? 'warning.4' : 'dimmed'} ff="monospace" size="sm">
                          {s.disputesLost}
                        </Text>
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      {s.wrongDismissals > 0 ? (
                        <Tooltip label={s.login === 'ksato' ? 'payments-api#571 F2 dismissed as false positive (14 Oct) → linked to INC-2291 (9 Nov)' : 'payments-api#588 F1 dismissed as by design (21 Oct) → linked to CARD-0412 bugfix'}>
                          <Group gap={4} justify="flex-end" wrap="nowrap">
                            <TbAlertTriangle size={12} color="var(--mantine-color-danger-4)" />
                            <Num v={s.wrongDismissals} neg />
                          </Group>
                        </Tooltip>
                      ) : (
                        <Num v={0} />
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="monospace" ta="right">
                        {s.p50Wh.toFixed(1)} / {s.p90Wh.toFixed(1)}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="monospace" ta="right" c="dimmed">
                        {s.gatesDecided}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" ff="monospace" ta="right" c="dimmed">
                        {s.comments} · {s.words.toLocaleString()}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      {!canSeePrivate ? (
                        <Text size="xs" c="dark.3">
                          —
                        </Text>
                      ) : s.rubberStamps > 0 ? (
                        <Tooltip multiline w={300} label={`${s.rubberStamps} approvals of PRs over 800 changed lines in under 10 working minutes. Visible to ${personName(s.login)} and the team lead.`}>
                          <Badge size="sm" color="warning" variant="light" leftSection={<TbFlag size={10} />}>
                            {s.rubberStamps}
                          </Badge>
                        </Tooltip>
                      ) : (
                        <Text size="xs" c="dimmed">
                          none
                        </Text>
                      )}
                    </Table.Td>
                  </Table.Tr>
                )
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Paper>
      <Text size="xs" c="dimmed">
        Greyed columns are context, not score: a reviewer who approves 22 gates in 0.2 wh p50 does not outrank one who confirms 5 real issues. Leaderboards are per team and per month.
        {!asLead && ' As a member you see the private column only for yourself.'}
      </Text>
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) person card (lead's view)

function PersonCard() {
  const [period, setPeriod] = useState<Period>('month')
  const s = scaled(REVIEWER_STATS.find((x) => x.login === 'ksato')!, period)
  const team = REVIEWER_STATS.map((x) => scaled(x, period))
  const teamP50 = team.reduce((a, x) => a + x.p50Wh, 0) / team.length
  return (
    <Stack gap="sm" maw={1100}>
      <Group justify="space-between">
        <Group gap="sm">
          <PersonChip login="ksato" />
          <Text size="xs" c="dimmed">
            payments-core · viewed by Hugo Martin (team lead) · {PERIOD_LABEL[period]}
          </Text>
        </Group>
        <PeriodToggle value={period} onChange={setPeriod} />
      </Group>
      <SimpleGrid cols={5} spacing="xs">
        <Kpi label="Outcome score" value={outcomeScore(s)} accent={outcomeScore(s) < 5 ? 'warning' : 'success'} sub="rank 4 of 5 in team" />
        <Kpi label="Suggestions merged" value={s.suggestionsMerged} sub="team median 8" />
        <Kpi label="Gates decided" value={s.gatesDecided} sub="most in team (volume, not scored)" accent="gray" />
        <Kpi label="Decision p50" value={`${s.p50Wh.toFixed(1)} wh`} sub={`team ${teamP50.toFixed(1)} wh`} />
        <Kpi label="Wrong dismissals" value={<Text span c="danger.4" fw={700} size="xl">{s.wrongDismissals}</Text>} sub="−8 each" accent="danger" />
      </SimpleGrid>
      <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 380px', gap: 12 }}>
        <Stack gap="sm">
          <Paper withBorder p="sm" radius="md">
            <SectionLabel>Gates decided per day vs findings accepted on those PRs</SectionLabel>
            <Group gap="xl" align="flex-end">
              <Box>
                <Spark values={DAILY.map((d) => Math.round(d.gatesClosed / 3))} width={300} height={46} />
                <Text size="xs" c="dimmed">
                  gates decided
                </Text>
              </Box>
              <Box>
                <Spark values={DAILY.map((d) => Math.round(d.accepted / 6))} width={300} height={46} color="var(--mantine-color-success-5)" />
                <Text size="xs" c="dimmed">
                  findings accepted
                </Text>
              </Box>
            </Group>
          </Paper>
          <Paper withBorder p="sm" radius="md">
            <SectionLabel>Wrong dismissal (counts against)</SectionLabel>
            <Stack gap={4}>
              <Text size="sm">
                <b>payments-api#571 · F2</b> "Refund status read before the PSP webhook is persisted" (major, CHK-0388)
              </Text>
              <Text size="xs" c="dimmed">
                Dismissed 14 Oct as <b>false positive</b>, scope this instance: "The webhook always lands first in practice."
              </Text>
              <Text size="xs" c="danger.4">
                Linked 9 Nov by the why-missed investigation for INC-2291 (duplicate refund emails). CHK-0388 weight raised; the dismissal reason is queued for team review.
              </Text>
            </Stack>
          </Paper>
          <Paper withBorder p="sm" radius="md">
            <SectionLabel>Decisions</SectionLabel>
            <Group gap="xl">
              <Text size="sm">
                Approved <b>{s.gatesDecided - s.sentBack}</b>
              </Text>
              <Text size="sm">
                Sent back <b>{s.sentBack}</b>
              </Text>
              <Text size="sm">
                Another round with steer <b>{s.steers}</b>
              </Text>
              <Text size="sm">
                Dismissed: false positive 6 · by design 2 · style 1
              </Text>
            </Group>
          </Paper>
        </Stack>
        <Paper withBorder p="sm" radius="md" style={{ borderColor: 'var(--mantine-color-warning-8)' }}>
          <Group gap={6} mb={6}>
            <TbEyeOff size={14} />
            <Text size="xs" fw={700} tt="uppercase" c="dimmed">
              Private · Kenji and team lead only
            </Text>
          </Group>
          <Alert color="warning" variant="light" p={8} icon={<TbFlag size={16} />} title={<Text size="sm" fw={700}>Rubber-stamp indicator: {s.rubberStamps}</Text>}>
            <Text size="xs">Approvals of large PRs much faster than the team norm. A conversation prompt, not a ranking.</Text>
          </Alert>
          <Stack gap={6} mt="sm">
            {[
              ['payments-api#598', 'Settlement export v2', '1,240 lines', '6 wm'],
              ['payments-api#603', 'Ledger: split refund entries', '910 lines', '4 wm'],
              ['payments-api#607', 'PSP adapter: retry budget', '860 lines', '8 wm'],
            ]
              .slice(0, s.rubberStamps)
              .map(([pr, t, size, time]) => (
                <Box key={pr}>
                  <Text size="xs" ff="monospace" c="dimmed">
                    {pr}
                  </Text>
                  <Text size="sm">{t}</Text>
                  <Text size="xs" c="warning.4">
                    {size} approved in {time} (team p50 for this size: 1.4 wh)
                  </Text>
                </Box>
              ))}
          </Stack>
        </Paper>
      </Box>
    </Stack>
  )
}

// ---------------------------------------------------------------- (C) team health

function Bars({ values, color, height = 60, labels }: { values: number[]; color: string; height?: number; labels?: string[] }) {
  const max = Math.max(...values, 1)
  return (
    <Group gap={3} align="flex-end" h={height + 14} wrap="nowrap">
      {values.map((v, i) => (
        <Tooltip key={i} label={`${labels?.[i] ?? i}: ${v}`}>
          <Stack gap={1} align="center">
            <Box w={18} h={Math.max(2, (v / max) * height)} bg={color} style={{ borderRadius: 2, opacity: labels?.[i]?.startsWith('S') ? 0.4 : 1 }} />
            <Text size="9px" c="dimmed">
              {labels?.[i]}
            </Text>
          </Stack>
        </Tooltip>
      ))}
    </Group>
  )
}

function TeamHealth() {
  const [period, setPeriod] = useState<Period>('day')
  const days = DAILY
  const dows = days.map((d) => d.dow)
  const acc = days.reduce((a, d) => a + d.accepted, 0)
  const dis = days.reduce((a, d) => a + d.dismissed, 0)
  const cost = days.reduce((a, d) => a + d.costApi + d.costSeatEq, 0)
  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Box>
          <Text fw={700} size="lg">
            payments-core · review health
          </Text>
          <Text size="xs" c="dimmed">
            Last 14 days · weekends shaded · percentiles from raw events (daily p90s are never averaged)
          </Text>
        </Box>
        <PeriodToggle value={period} onChange={setPeriod} />
      </Group>
      <SimpleGrid cols={5} spacing="xs">
        <Kpi label="Gate backlog (EOD)" value={days[days.length - 1].backlog} sub="oldest 5.6 wh · 2 SLA breaches" accent="pink" />
        <Kpi label="Time to decision p50" value="1.5 wh" sub="p90 4.1 wh" />
        <Kpi label="Bot precision" value={`${Math.round((acc / (acc + dis)) * 100)}%`} sub={`${acc} accepted · ${dis} dismissed`} accent="success" />
        <Kpi label="Rounds per PR" value="1.8" sub="open → bot-clean 1.1 wd · → merge 2.3 wd" />
        <Kpi label="Cost per accepted finding" value={`≈${fmtUsd(cost / acc)}`} sub="API + seat equivalent" accent="cyan" />
      </SimpleGrid>
      <SimpleGrid cols={3} spacing="sm">
        <Paper withBorder p="sm" radius="md">
          <SectionLabel>Gates closed per day</SectionLabel>
          <Bars values={days.map((d) => d.gatesClosed)} labels={dows} color="var(--mantine-color-pink-6)" />
        </Paper>
        <Paper withBorder p="sm" radius="md">
          <SectionLabel>Findings accepted per day</SectionLabel>
          <Bars values={days.map((d) => d.accepted)} labels={dows} color="var(--mantine-color-success-6)" />
        </Paper>
        <Paper withBorder p="sm" radius="md">
          <SectionLabel>Findings dismissed per day</SectionLabel>
          <Bars values={days.map((d) => d.dismissed)} labels={dows} color="var(--mantine-color-gray-6)" />
        </Paper>
      </SimpleGrid>
      <Paper withBorder p="sm" radius="md">
        <SectionLabel>Dismissed by reason (14 days)</SectionLabel>
        <Group gap="lg">
          {[
            ['False positive', 31],
            ['By design', 12],
            ['Style preference', 9],
            ['Out of scope', 7],
            ['Fix later', 5],
            ['Accepted risk', 3],
            ['Duplicate', 2],
          ].map(([r, n]) => (
            <Box key={r as string}>
              <Text size="lg" fw={700} ff="monospace">
                {n}
              </Text>
              <Text size="xs" c="dimmed">
                {r}
              </Text>
            </Box>
          ))}
        </Group>
      </Paper>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Reviewer Stats',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Team leaderboard ranked by outcome score; volume greyed; private rubber-stamp column respects who is viewing. */
export const TeamLeaderboard: Story = { name: 'A · Team leaderboard (outcomes)', render: () => <Leaderboard /> }

/** (B) One person's card as the team lead sees it: high volume, low outcome, a wrong dismissal, and the private rubber-stamp panel. */
export const PersonCardLeadView: Story = { name: 'B · Person card (lead view)', render: () => <PersonCard /> }

/** (C) Team health over time: backlog, time to decision, accepted vs dismissed, dismissal reasons, cost per accepted finding. */
export const TeamHealthView: Story = { name: 'C · Team health', render: () => <TeamHealth /> }
