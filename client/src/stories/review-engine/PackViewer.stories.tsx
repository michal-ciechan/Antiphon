import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import {
  Badge,
  Box,
  Button,
  Group,
  Paper,
  Progress,
  SegmentedControl,
  Stack,
  Table,
  Text,
  Tooltip,
} from '@mantine/core'
import { TbCheck, TbEye, TbFileSearch, TbPlus, TbQuote, TbX } from 'react-icons/tb'
import {
  PACK_BUDGET,
  PACK_FILES,
  PACK_OPENED_NOT_IN_PACK,
  PACK_REVIEWERS,
  PACK_SOURCE_META,
  fmtTokens,
  type PackFile,
  type PackSource,
} from './mockData'
import { ModelBadge, SectionLabel } from './shared'

/**
 * PROPOSAL MOCK — not production code. Context pack viewer for
 * `docs/features/012-review-engine/design.md` §4.6 context packs, §4.7 reviewers, §13 context
 * signals: per reviewer, which files went into its one-file pack and why (ledger row: source,
 * reason, score, tokens), what the budget cut, and what the reviewer opened that the pack missed.
 *   A — ledger table + "opened but not in pack" panel
 *   B — the pack as one file: a token map of its sections, plus source hit rates
 */

function ReviewerPicker({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <SegmentedControl
      size="xs"
      value={value}
      onChange={onChange}
      data={PACK_REVIEWERS.map((r) => ({
        value: r.group,
        label: (
          <Group gap={4} wrap="nowrap">
            <Text size="xs">{r.group}</Text>
            <Text size="10px" c="dimmed">
              {r.status === 'running' ? '● running' : r.status}
            </Text>
          </Group>
        ),
      }))}
    />
  )
}

function SourceBadge({ s }: { s: PackSource }) {
  return (
    <Badge size="xs" variant="light" color={PACK_SOURCE_META[s].color} radius="sm" tt="none">
      {PACK_SOURCE_META[s].label}
    </Badge>
  )
}

function Header({ group, setGroup }: { group: string; setGroup: (g: string) => void }) {
  const r = PACK_REVIEWERS.find((x) => x.group === group)!
  const total = PACK_BUDGET.prefixUsed + r.suffixTokens
  return (
    <Stack gap={6}>
      <Group justify="space-between" align="flex-end" wrap="nowrap">
        <Box>
          <Text fw={700} size="lg">
            Context pack · payments-api#618 · iteration 3
          </Text>
          <Text size="xs" c="dimmed" ff="monospace">
            artifacts/RR-3118/it3/pack-{group}.md · sha256:{group.length}e0c41a… · staged to server2-b
          </Text>
        </Box>
        <ReviewerPicker value={group} onChange={setGroup} />
      </Group>
      <Paper withBorder p="xs" radius="md">
        <Group justify="space-between" mb={4}>
          <Group gap={8}>
            <ModelBadge model={r.model} withProvider />
            <Text size="xs">
              checks: <Text span ff="monospace" size="xs">{r.checks.join(', ')}</Text>
            </Text>
          </Group>
          <Text size="xs" ff="monospace">
            {fmtTokens(total)} tokens · prompt cache hit {Math.round(r.cacheHit * 100)}%
          </Text>
        </Group>
        <Progress.Root size={16}>
          <Tooltip label="Shared prefix: byte-identical across this run's 4 reviewers, so it is cached once">
            <Progress.Section value={(PACK_BUDGET.prefixUsed / (PACK_BUDGET.prefixBudget + 8000)) * 100} color="active">
              <Progress.Label>shared prefix {fmtTokens(PACK_BUDGET.prefixUsed)} / {fmtTokens(PACK_BUDGET.prefixBudget)}</Progress.Label>
            </Progress.Section>
          </Tooltip>
          <Tooltip label={`Group suffix for ${group}: its checks' @model sections + output contract`}>
            <Progress.Section value={(r.suffixTokens / (PACK_BUDGET.prefixBudget + 8000)) * 100} color="grape">
              <Progress.Label>suffix {fmtTokens(r.suffixTokens)}</Progress.Label>
            </Progress.Section>
          </Tooltip>
        </Progress.Root>
      </Paper>
    </Stack>
  )
}

// ---------------------------------------------------------------- (A) ledger

function Ledger() {
  const [group, setGroup] = useState('concurrency')
  const [showCut, setShowCut] = useState(true)
  const rows = PACK_FILES.filter((f) => showCut || f.included)
  return (
    <Stack gap="sm">
      <Header group={group} setGroup={setGroup} />
      <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 380px', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md">
          <Group justify="space-between" px="sm" py={6}>
            <Text size="xs" c="dimmed">
              Context ledger · one row per candidate file · included in score order until the budget ran out
            </Text>
            <SegmentedControl
              size="xs"
              value={showCut ? 'all' : 'in'}
              onChange={(v) => setShowCut(v === 'all')}
              data={[
                { value: 'all', label: 'All candidates' },
                { value: 'in', label: 'Included only' },
              ]}
            />
          </Group>
          <Table.ScrollContainer minWidth={900} type="native">
            <Table verticalSpacing={5} fz="sm" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w={28} />
                  <Table.Th>File</Table.Th>
                  <Table.Th w={140}>Source</Table.Th>
                  <Table.Th>Reason</Table.Th>
                  <Table.Th w={60} ta="right">
                    Score
                  </Table.Th>
                  <Table.Th w={70} ta="right">
                    Tokens
                  </Table.Th>
                  <Table.Th w={90}>Used</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {rows.map((f, i) => {
                  const firstCut = !f.included && (i === 0 || rows[i - 1].included)
                  return (
                    <LedgerRow key={f.path} f={f} firstCut={firstCut} />
                  )
                })}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </Paper>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="sm" style={{ borderColor: 'var(--mantine-color-warning-8)' }}>
            <SectionLabel>Opened or wanted, but not in the pack</SectionLabel>
            <Stack gap="sm">
              {PACK_OPENED_NOT_IN_PACK.map((o) => (
                <Box key={o.path}>
                  <Group gap={6} wrap="nowrap">
                    <Badge size="xs" color={o.kind === 'opened' ? 'warning' : 'gray'} variant="light">
                      {o.kind}
                    </Badge>
                    <Text size="xs" ff="monospace" truncate>
                      {o.path}
                    </Text>
                  </Group>
                  <Text size="xs" mt={2}>
                    {o.by}: "{o.reason}"
                  </Text>
                  <Text size="xs" c="cyan.4" mt={2}>
                    {o.suggestion}
                  </Text>
                  {o.kind === 'opened' && (
                    <Button size="compact-xs" variant="light" mt={4} leftSection={<TbPlus size={12} />}>
                      Propose related.yaml rule (PR)
                    </Button>
                  )}
                </Box>
              ))}
            </Stack>
          </Paper>
          <Paper withBorder radius="md" p="sm">
            <SectionLabel>Never touched</SectionLabel>
            <Text size="xs">
              {PACK_FILES.filter((f) => f.included && !f.cited && !f.opened && f.source !== 'diff').length} included files were never cited or opened:{' '}
              {PACK_FILES.filter((f) => f.included && !f.cited && !f.opened && f.source !== 'diff')
                .map((f) => f.path.split('/').pop())
                .join(', ')}
              . Each one lowers its source's hit rate.
            </Text>
          </Paper>
        </Stack>
      </Box>
    </Stack>
  )
}

function LedgerRow({ f, firstCut }: { f: PackFile; firstCut: boolean }) {
  return (
    <>
      {firstCut && (
        <Table.Tr>
          <Table.Td colSpan={7} py={2} bg="rgba(233,54,54,0.08)">
            <Text size="10px" c="danger.4" ta="center" fw={700}>
              ✂ budget cut: prefix at {fmtTokens(PACK_BUDGET.prefixUsed)} of {fmtTokens(PACK_BUDGET.prefixBudget)}; the next file did not fit
            </Text>
          </Table.Td>
        </Table.Tr>
      )}
      <Table.Tr style={{ opacity: f.included ? 1 : 0.5 }}>
        <Table.Td>{f.included ? <TbCheck size={14} color="var(--mantine-color-success-5)" /> : <TbX size={14} color="var(--mantine-color-danger-5)" />}</Table.Td>
        <Table.Td maw={320}>
          <Text size="xs" ff="monospace" truncate>
            {f.path}
          </Text>
          <Text size="10px" c="dimmed">
            {f.mode}
          </Text>
        </Table.Td>
        <Table.Td>
          <SourceBadge s={f.source} />
        </Table.Td>
        <Table.Td maw={340}>
          <Text size="xs" c="dimmed" lineClamp={2}>
            {f.reason}
          </Text>
        </Table.Td>
        <Table.Td ta="right">
          <Text size="xs" ff="monospace">
            {f.score.toFixed(2)}
          </Text>
        </Table.Td>
        <Table.Td ta="right">
          <Text size="xs" ff="monospace">
            {fmtTokens(f.tokens)}
          </Text>
        </Table.Td>
        <Table.Td>
          <Group gap={4} wrap="nowrap">
            {f.cited ? (
              <Tooltip label={`Cited in ${f.cited} finding(s) or check rows`}>
                <Badge size="xs" color="success" variant="light" leftSection={<TbQuote size={9} />}>
                  {f.cited}
                </Badge>
              </Tooltip>
            ) : null}
            {f.opened && (
              <Tooltip label="Reviewer opened the file in the checkout too">
                <Badge size="xs" color="gray" variant="light" leftSection={<TbEye size={9} />}>
                  open
                </Badge>
              </Tooltip>
            )}
          </Group>
        </Table.Td>
      </Table.Tr>
    </>
  )
}

// ---------------------------------------------------------------- (B) token map + source hit rates

const OUTLINE: { section: string; tokens: number; color: string; note: string }[] = [
  { section: 'PR metadata, intent, categorisation, blast radius', tokens: 1_900, color: 'gray', note: 'round 3 · steer from Hugo Martin' },
  { section: 'Outstanding findings (recheck)', tokens: 1_300, color: 'warning', note: '2 open findings with anchors, so they are not re-reported' },
]

function TokenMap() {
  const [group, setGroup] = useState('concurrency')
  const r = PACK_REVIEWERS.find((x) => x.group === group)!
  const included = PACK_FILES.filter((f) => f.included)
  const total = OUTLINE.reduce((a, o) => a + o.tokens, 0) + included.reduce((a, f) => a + f.tokens, 0) + r.suffixTokens
  const sources = (Object.keys(PACK_SOURCE_META) as PackSource[]).filter((s) => PACK_FILES.some((f) => f.source === s))
  return (
    <Stack gap="sm">
      <Header group={group} setGroup={setGroup} />
      <Paper withBorder radius="md" p="sm">
        <SectionLabel>The pack as one file (width ∝ tokens, in file order)</SectionLabel>
        <Group gap={2} wrap="nowrap" h={54} align="stretch">
          {OUTLINE.map((o) => (
            <Tooltip key={o.section} label={`${o.section} · ${fmtTokens(o.tokens)} · ${o.note}`}>
              <Box style={{ flex: o.tokens / total, minWidth: 6, background: `var(--mantine-color-${o.color}-8)`, borderRadius: 3 }} />
            </Tooltip>
          ))}
          {included.map((f) => (
            <Tooltip key={f.path} label={`${f.path} · ${PACK_SOURCE_META[f.source].label} · ${fmtTokens(f.tokens)} · ${f.reason}`}>
              <Box
                style={{
                  flex: f.tokens / total,
                  minWidth: 4,
                  background: `var(--mantine-color-${PACK_SOURCE_META[f.source].color}-7)`,
                  borderRadius: 3,
                  outline: f.cited ? '2px solid var(--mantine-color-success-4)' : undefined,
                  outlineOffset: -2,
                  opacity: f.cited || f.opened || f.source === 'diff' ? 1 : 0.45,
                }}
              />
            </Tooltip>
          ))}
          <Box w={2} bg="white" />
          <Tooltip label={`Group suffix (${group}): ${r.checks.join(', ')} @model sections + review-findings/1 contract`}>
            <Box style={{ flex: r.suffixTokens / total, minWidth: 8, background: 'var(--mantine-color-grape-8)', borderRadius: 3 }} />
          </Tooltip>
        </Group>
        <Group justify="space-between" mt={4}>
          <Text size="10px" c="dimmed">
            ← shared prefix (cached across reviewers)
          </Text>
          <Text size="10px" c="dimmed">
            group suffix →
          </Text>
        </Group>
        <Group gap="md" mt={6}>
          {sources.map((s) => (
            <Group key={s} gap={4}>
              <Box w={10} h={10} bg={`${PACK_SOURCE_META[s].color}.7`} style={{ borderRadius: 2 }} />
              <Text size="xs" c="dimmed">
                {PACK_SOURCE_META[s].label}
              </Text>
            </Group>
          ))}
          <Text size="xs" c="dimmed">
            · green outline = cited · faded = never touched
          </Text>
        </Group>
      </Paper>
      <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)', gap: 12 }}>
        <Paper withBorder radius="md" p="sm">
          <SectionLabel>Source hit rate (repo, 30 days) → reweights the lookup</SectionLabel>
          <Stack gap={6}>
            {sources
              .filter((s) => s !== 'diff')
              .sort((a, b) => PACK_SOURCE_META[b].hitRate - PACK_SOURCE_META[a].hitRate)
              .map((s) => {
                const m = PACK_SOURCE_META[s]
                return (
                  <Box key={s}>
                    <Group justify="space-between">
                      <SourceBadge s={s} />
                      <Text size="xs" ff="monospace">
                        {Math.round(m.hitRate * 100)}% cited or opened
                      </Text>
                    </Group>
                    <Progress value={m.hitRate * 100} size={5} color={m.hitRate >= 0.5 ? 'success' : m.hitRate >= 0.3 ? 'yellow' : 'danger'} mt={2} />
                  </Box>
                )
              })}
          </Stack>
          <Text size="xs" c="dimmed" mt={6}>
            git co-change and model suggestions earn least here; the learn stage proposes lowering their weight for payments-api.
          </Text>
        </Paper>
        <Paper withBorder radius="md" p="sm">
          <Group gap={6} mb={4}>
            <TbFileSearch size={14} />
            <Text size="xs" fw={700} c="dimmed" tt="uppercase">
              Pack misses this iteration
            </Text>
          </Group>
          <Stack gap={8}>
            {PACK_OPENED_NOT_IN_PACK.map((o) => {
              const cut = PACK_FILES.find((f) => f.path === o.path)
              return (
                <Box key={o.path}>
                  <Text size="xs" ff="monospace">
                    {o.path}
                  </Text>
                  <Text size="xs" c={cut ? 'warning.4' : 'dimmed'}>
                    {cut ? `Was a candidate (${PACK_SOURCE_META[cut.source].label}, score ${cut.score.toFixed(2)}) but the budget cut it.` : o.kind === 'missing' ? 'Does not exist in the repo.' : 'Never a candidate: no source proposed it.'}
                  </Text>
                </Box>
              )
            })}
          </Stack>
        </Paper>
      </Box>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Pack Viewer',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) The context ledger per reviewer: source, reason, score, tokens, cut line, cited/opened; plus files the reviewer opened that the pack missed. */
export const LedgerTable: Story = { name: 'A · Ledger', render: () => <Ledger /> }

/** (B) The pack drawn as one file (prefix sections, files, suffix) with source hit rates and why each miss happened. */
export const TokenMapView: Story = { name: 'B · Token map + hit rates', render: () => <TokenMap /> }
