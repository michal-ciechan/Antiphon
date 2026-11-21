import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import {
  Badge,
  Box,
  Button,
  Divider,
  Drawer,
  Group,
  Paper,
  Stack,
  Table,
  Tabs,
  Text,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { TbEye, TbFilter, TbScale, TbTrash } from 'react-icons/tb'
import { CHECKS, REASON_META, type Check, type CheckStatus } from './mockData'
import { FlagBadges, SectionLabel, SeverityBadge } from './shared'

/**
 * PROPOSAL MOCK — not production code. Check catalogue for
 * `docs/features/012-review-engine/design.md` §9 check catalogue and §17 Checks page: selection
 * rate, precision per scope, last hit, shadow candidates awaiting promotion, noisy checks to
 * retire or narrow.
 *   A — catalogue table with lifecycle tabs and a detail drawer
 *   B — precision × volume map: where each check sits, and what to do about each quadrant
 */

const STATUS_COLOR: Record<CheckStatus, string> = { candidate: 'cyan', shadow: 'indigo', active: 'success', retired: 'gray' }
const PROMOTE_PRECISION = 0.7
const NOISY_PRECISION = 0.35

function precisionColor(p: number): string {
  if (p >= PROMOTE_PRECISION) return 'success.4'
  if (p >= 0.5) return 'yellow.4'
  return 'danger.4'
}

function OutcomeBar({ c }: { c: Check }) {
  const total = c.accepted + c.dismissed + c.ignored
  if (total === 0)
    return (
      <Text size="xs" c="dimmed">
        —
      </Text>
    )
  return (
    <Tooltip label={`${c.accepted} accepted · ${c.dismissed} dismissed · ${c.ignored} ignored (merged with the finding open)`}>
      <Group gap={0} w={110} h={8} wrap="nowrap" style={{ borderRadius: 4, overflow: 'hidden' }}>
        <Box h={8} w={`${(c.accepted / total) * 100}%`} bg="success.6" />
        <Box h={8} w={`${(c.dismissed / total) * 100}%`} bg="danger.6" />
        <Box h={8} w={`${(c.ignored / total) * 100}%`} bg="dark.3" />
      </Group>
    </Tooltip>
  )
}

function CheckDetail({ c }: { c: Check }) {
  return (
    <Stack gap="sm">
      <Group gap={6}>
        <SeverityBadge severity={c.severity} />
        <FlagBadges flags={c.flags} />
        <Badge color={STATUS_COLOR[c.status]} variant="light">
          {c.status}
        </Badge>
        {c.requiresHuman && (
          <Badge color="pink" variant="outline" size="sm">
            requiresHuman
          </Badge>
        )}
      </Group>
      <Text size="xs" ff="monospace" c="dimmed">
        .antiphon/review/checks/{c.category}/{c.id}.md · prompt hash 7f3a91c (covers @model sections only)
      </Text>
      <Box>
        <SectionLabel># @humans: Why this check exists</SectionLabel>
        <Text size="sm">{c.humansWhy}</Text>
      </Box>
      <Box>
        <SectionLabel># @maintainers: Rationale</SectionLabel>
        <Text size="sm">Provenance: {c.provenance}.</Text>
      </Box>
      <Box>
        <SectionLabel>Precision by repo (30d)</SectionLabel>
        {Object.entries(c.byRepo).length === 0 && (
          <Text size="xs" c="dimmed">
            No data yet.
          </Text>
        )}
        {Object.entries(c.byRepo).map(([repo, p]) => (
          <Group key={repo} justify="space-between">
            <Text size="xs" ff="monospace">
              {repo}
            </Text>
            <Text size="xs" c={precisionColor(p!)}>
              {Math.round(p! * 100)}%
            </Text>
          </Group>
        ))}
      </Box>
      <Box>
        <SectionLabel>Revisions</SectionLabel>
        <Text size="xs">v3 · 2026-11-21 · narrowed triggers; replay 9/10, FP 6%</Text>
        <Text size="xs" c="dimmed">
          v2 · 2026-10-02 · flagged Dapper too (40% FP)
        </Text>
      </Box>
      {c.shadowEval && (
        <Paper p="xs" withBorder radius="sm" style={{ borderColor: 'var(--mantine-color-indigo-7)' }}>
          <Text size="xs">
            Shadow evaluation: {c.shadowEval}. Promotion needs precision ≥ {PROMOTE_PRECISION * 100}% on ≥ 10 hits.
          </Text>
          <Button size="xs" mt={6} disabled={c.precision < PROMOTE_PRECISION}>
            Promote to active (opens a PR)
          </Button>
        </Paper>
      )}
      {c.status === 'active' && c.precision < NOISY_PRECISION && (
        <Group gap={6}>
          <Button size="xs" variant="light" leftSection={<TbFilter size={12} />}>
            Narrow triggers
          </Button>
          <Button size="xs" variant="light" color="danger" leftSection={<TbTrash size={12} />}>
            Retire (PR)
          </Button>
        </Group>
      )}
    </Stack>
  )
}

// ---------------------------------------------------------------- (A) catalogue

function Catalogue() {
  const [tab, setTab] = useState<string | null>('all')
  const [sel, setSel] = useState<Check | null>(null)
  const filtered = CHECKS.filter((c) => {
    if (tab === 'shadow') return c.status === 'shadow' || c.status === 'candidate'
    if (tab === 'noisy') return c.status === 'active' && c.precision < NOISY_PRECISION
    if (tab === 'retired') return c.status === 'retired'
    return c.status === 'active'
  })
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Text fw={700} size="lg">
            Checks
          </Text>
          <Text size="xs" c="dimmed">
            payments-api effective set (common@v14 → payments-core → repo) · 1,184 checks indexed · last 30 days
          </Text>
        </Box>
      </Group>
      <Tabs value={tab} onChange={setTab}>
        <Tabs.List>
          <Tabs.Tab value="all">Active {CHECKS.filter((c) => c.status === 'active').length}</Tabs.Tab>
          <Tabs.Tab value="shadow" leftSection={<TbEye size={14} />}>
            Shadow &amp; candidates {CHECKS.filter((c) => c.status === 'shadow' || c.status === 'candidate').length}
          </Tabs.Tab>
          <Tabs.Tab value="noisy" color="danger">
            Noisy {CHECKS.filter((c) => c.status === 'active' && c.precision < NOISY_PRECISION).length}
          </Tabs.Tab>
          <Tabs.Tab value="retired">Retired {CHECKS.filter((c) => c.status === 'retired').length}</Tabs.Tab>
        </Tabs.List>
      </Tabs>
      <Paper withBorder radius="md">
        <Table.ScrollContainer minWidth={1250} type="native">
          <Table verticalSpacing={6} fz="sm" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={90}>Check</Table.Th>
                <Table.Th>Title</Table.Th>
                <Table.Th w={150}>Severity</Table.Th>
                <Table.Th w={90}>Status</Table.Th>
                <Table.Th w={90} ta="right">
                  Selected
                </Table.Th>
                <Table.Th w={80} ta="right">
                  Posted
                </Table.Th>
                <Table.Th w={130}>Outcomes</Table.Th>
                <Table.Th w={90} ta="right">
                  Precision
                </Table.Th>
                <Table.Th w={140}>Top dismissal</Table.Th>
                <Table.Th w={80}>Last hit</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {filtered.map((c) => (
                <Table.Tr key={c.id} style={{ cursor: 'pointer' }} onClick={() => setSel(c)}>
                  <Table.Td>
                    <Text size="xs" ff="monospace">
                      {c.id}
                    </Text>
                    <Text size="10px" c="dimmed">
                      {c.category}
                    </Text>
                  </Table.Td>
                  <Table.Td maw={380}>
                    <Text size="sm" truncate>
                      {c.title}
                    </Text>
                    <Text size="xs" c="dimmed" truncate>
                      {c.shadowEval ?? c.provenance}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <SeverityBadge severity={c.severity} size="xs" />
                      <FlagBadges flags={c.flags} />
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="xs" color={STATUS_COLOR[c.status]} variant="light">
                      {c.status}
                    </Badge>
                  </Table.Td>
                  <Table.Td ta="right">
                    <Text size="sm" ff="monospace">
                      {c.selected30d}
                    </Text>
                  </Table.Td>
                  <Table.Td ta="right">
                    <Text size="sm" ff="monospace" c={c.status === 'shadow' ? 'dimmed' : undefined}>
                      {c.status === 'shadow' ? 'never' : c.posted30d}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <OutcomeBar c={c} />
                  </Table.Td>
                  <Table.Td ta="right">
                    <Text size="sm" ff="monospace" fw={600} c={c.precision ? precisionColor(c.precision) : 'dimmed'}>
                      {c.precision ? `${Math.round(c.precision * 100)}%` : '—'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" c="dimmed">
                      {c.topDismissReason ? REASON_META[c.topDismissReason].label : '—'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" c="dimmed">
                      {c.lastHit}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Paper>
      <Text size="xs" c="dimmed">
        Precision = accepted ÷ (accepted + dismissed) on posted findings. Shadow checks are selected and evaluated but never posted. Noisy = active with precision under{' '}
        {NOISY_PRECISION * 100}%.
      </Text>
      <Drawer opened={!!sel} onClose={() => setSel(null)} position="right" size={440} title={sel && <Text fw={700}>{sel.id} · {sel.title}</Text>}>
        {sel && <CheckDetail c={sel} />}
      </Drawer>
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) precision × volume map

const MAP_W = 720
const MAP_H = 420
const PAD = 40

function PrecisionMap() {
  const plotted = CHECKS.filter((c) => c.status !== 'candidate')
  const [sel, setSel] = useState<Check>(CHECKS.find((c) => c.id === 'CHK-0510')!)
  const maxVol = 80
  const x = (c: Check) => PAD + (Math.min(maxVol, c.status === 'shadow' ? c.selected30d : c.posted30d) / maxVol) * (MAP_W - PAD * 2)
  const y = (c: Check) => MAP_H - PAD - c.precision * (MAP_H - PAD * 2)
  const midX = PAD + (15 / maxVol) * (MAP_W - PAD * 2)
  const midY = MAP_H - PAD - PROMOTE_PRECISION * (MAP_H - PAD * 2)
  const noisyY = MAP_H - PAD - NOISY_PRECISION * (MAP_H - PAD * 2)
  const quadrant = (c: Check) => {
    const vol = c.status === 'shadow' ? c.selected30d : c.posted30d
    if (c.status === 'shadow') return c.precision >= PROMOTE_PRECISION ? 'Ready to promote' : 'Keep in shadow'
    if (c.precision < NOISY_PRECISION) return vol >= 15 ? 'Noisy: narrow or retire' : 'Weak and quiet'
    if (c.precision >= PROMOTE_PRECISION) return vol >= 15 ? 'Workhorse' : 'Sharp, rare'
    return 'Watch'
  }
  return (
    <Stack gap="sm">
      <Group gap={6}>
        <TbScale size={18} />
        <Text fw={700} size="lg">
          Checks · precision × volume
        </Text>
        <Text size="xs" c="dimmed">
          payments-api · 30 days · x = findings posted (shadow: times selected) · y = precision
        </Text>
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: `${MAP_W + 2}px minmax(0, 1fr)`, gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p={0}>
          <svg width={MAP_W} height={MAP_H} style={{ display: 'block' }}>
            <rect x={midX} y={PAD} width={MAP_W - PAD - midX} height={midY - PAD} fill="rgba(51,201,107,0.06)" />
            <rect x={midX} y={noisyY} width={MAP_W - PAD - midX} height={MAP_H - PAD - noisyY} fill="rgba(233,54,54,0.08)" />
            <line x1={PAD} y1={midY} x2={MAP_W - PAD} y2={midY} stroke="#373a40" strokeDasharray="4 4" />
            <line x1={PAD} y1={noisyY} x2={MAP_W - PAD} y2={noisyY} stroke="#5c2a2a" strokeDasharray="4 4" />
            <line x1={midX} y1={PAD} x2={midX} y2={MAP_H - PAD} stroke="#373a40" strokeDasharray="4 4" />
            <line x1={PAD} y1={MAP_H - PAD} x2={MAP_W - PAD} y2={MAP_H - PAD} stroke="#5c5f66" />
            <line x1={PAD} y1={PAD} x2={PAD} y2={MAP_H - PAD} stroke="#5c5f66" />
            <text x={MAP_W - PAD - 4} y={PAD + 14} fill="#33c96b" fontSize={11} textAnchor="end">
              workhorses
            </text>
            <text x={MAP_W - PAD - 4} y={MAP_H - PAD - 8} fill="#e93636" fontSize={11} textAnchor="end">
              noisy: narrow or retire
            </text>
            <text x={PAD + 6} y={PAD + 14} fill="#909296" fontSize={11}>
              sharp but rare
            </text>
            <text x={PAD - 6} y={midY + 4} fill="#909296" fontSize={10} textAnchor="end">
              70%
            </text>
            <text x={PAD - 6} y={noisyY + 4} fill="#909296" fontSize={10} textAnchor="end">
              35%
            </text>
            <text x={PAD - 6} y={PAD + 4} fill="#909296" fontSize={10} textAnchor="end">
              100%
            </text>
            <text x={MAP_W - PAD} y={MAP_H - PAD + 16} fill="#909296" fontSize={10} textAnchor="end">
              {maxVol}+ posted
            </text>
            {plotted.map((c) => {
              const fill = c.status === 'shadow' ? 'none' : c.status === 'retired' ? '#5c5f66' : c.flags.length ? '#f06595' : '#3b7bf5'
              const r = 5 + Math.min(10, c.selected30d / 20)
              return (
                <g key={c.id} style={{ cursor: 'pointer' }} onClick={() => setSel(c)}>
                  <circle
                    cx={x(c)}
                    cy={y(c)}
                    r={r}
                    fill={fill}
                    fillOpacity={0.75}
                    stroke={c.status === 'shadow' ? '#748ffc' : sel.id === c.id ? '#fff' : '#1a1b1e'}
                    strokeWidth={sel.id === c.id ? 2.5 : c.status === 'shadow' ? 2 : 1}
                    strokeDasharray={c.status === 'shadow' ? '3 2' : undefined}
                  />
                  <text x={x(c) + r + 3} y={y(c) + 4} fill="#c1c2c5" fontSize={10}>
                    {c.id.replace('CHK-', '')}
                  </text>
                </g>
              )
            })}
          </svg>
          <Group gap="md" px="sm" pb={6}>
            <Group gap={4}>
              <Box w={10} h={10} bg="active.6" style={{ borderRadius: 5 }} />
              <Text size="xs" c="dimmed">
                active
              </Text>
            </Group>
            <Group gap={4}>
              <Box w={10} h={10} bg="pink.6" style={{ borderRadius: 5 }} />
              <Text size="xs" c="dimmed">
                active, bypass flag
              </Text>
            </Group>
            <Group gap={4}>
              <Box w={10} h={10} style={{ borderRadius: 5, border: '2px dashed #748ffc' }} />
              <Text size="xs" c="dimmed">
                shadow
              </Text>
            </Group>
            <Group gap={4}>
              <Box w={10} h={10} bg="gray.6" style={{ borderRadius: 5 }} />
              <Text size="xs" c="dimmed">
                retired
              </Text>
            </Group>
            <Text size="xs" c="dimmed">
              size = times selected
            </Text>
          </Group>
        </Paper>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="sm">
            <Group justify="space-between" mb={6}>
              <Text fw={700}>
                {sel.id} · {sel.title}
              </Text>
              <Badge variant="light" color={quadrant(sel).startsWith('Noisy') ? 'danger' : quadrant(sel).startsWith('Ready') || quadrant(sel) === 'Workhorse' ? 'success' : 'gray'}>
                {quadrant(sel)}
              </Badge>
            </Group>
            <CheckDetail c={sel} />
          </Paper>
          <Paper withBorder radius="md" p="sm">
            <SectionLabel>Act on a quadrant</SectionLabel>
            {['Noisy: narrow or retire', 'Ready to promote', 'Keep in shadow'].map((q) => {
              const cs = CHECKS.filter((c) => c.status !== 'candidate' && c.status !== 'retired' && quadrant(c) === q)
              return (
                <Box key={q} mb={6}>
                  <Text size="xs" fw={700} c={q.startsWith('Noisy') ? 'danger.4' : q.startsWith('Ready') ? 'success.4' : 'indigo.4'}>
                    {q} · {cs.length}
                  </Text>
                  {cs.map((c) => (
                    <UnstyledButton key={c.id} onClick={() => setSel(c)} display="block">
                      <Text size="xs">
                        <Text span ff="monospace" c="dimmed" size="xs">
                          {c.id}
                        </Text>{' '}
                        {c.title} · {Math.round(c.precision * 100)}%
                      </Text>
                    </UnstyledButton>
                  ))}
                </Box>
              )
            })}
            <Divider my={6} />
            <Text size="xs" c="dimmed">
              Every action arrives as a PR to the layer that owns the check (repo, team or common), with the evidence threads linked.
            </Text>
          </Paper>
        </Stack>
      </Box>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Checks',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Catalogue with lifecycle tabs (active, shadow & candidates, noisy, retired); click a row for the detail drawer. */
export const CatalogueTable: Story = { name: 'A · Catalogue', render: () => <Catalogue /> }

/** (B) Precision × volume map with quadrant actions: narrow/retire the noisy, promote ready shadows. */
export const PrecisionVolumeMap: Story = { name: 'B · Precision × volume map', render: () => <PrecisionMap /> }
