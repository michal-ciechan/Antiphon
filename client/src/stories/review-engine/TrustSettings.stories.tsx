import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import {
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  NumberInput,
  Paper,
  SegmentedControl,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Textarea,
  Tooltip,
} from '@mantine/core'
import { TbAdjustments, TbCheck, TbDatabase, TbHistory, TbLock, TbPin, TbPlusMinus, TbShieldCheck, TbX } from 'react-icons/tb'
import {
  BYPASS_CATEGORIES,
  TEAMS,
  TRUST,
  TRUST_THRESHOLDS,
  TRUST_THRESHOLD_REVISIONS,
  effectiveTrust,
  personName,
  type BypassCategory,
  type SettingRevision,
  type TeamKey,
  type ThresholdKey,
  type TrustAdjustment,
  type TrustScopeKind,
  type TrustScore,
} from './mockData'
import { PersonChip, SectionLabel } from './shared'

/**
 * PROPOSAL MOCK — not production code. Trust and policy settings for the review engine
 * (`docs/features/012-review-engine/design.md`, trust and thresholds). Everything here is stored in
 * Antiphon (database rows with revisions), never in repo files.
 *   A — per-user trust per scope (repo, team, area): computed value, manual adjustment (offset or
 *       pinned, with reason, who, when, optional expiry), effective value; "Adjust" for leads and maintainers
 *   B — thresholds (instance defaults + team overrides, revision history) and bypass categories
 */

/** The mock viewer: Hugo Martin, payments-core lead and payments-api maintainer. */
const VIEWER = 'hmartin'
const VIEWER_SCOPES = new Set(['payments-api', 'payments-core', 'ledger'])

const SCOPE_KIND_META: Record<TrustScopeKind, { label: string; color: string }> = {
  repo: { label: 'repo', color: 'active' },
  team: { label: 'team', color: 'grape' },
  area: { label: 'area', color: 'teal' },
}

/** The team whose threshold overrides apply to a scope. */
function teamOfScope(t: TrustScore): TeamKey {
  if (t.scope === 'antiphon' || t.scope === 'platform-runtime') return 'platform'
  if (t.scope === 'web-portal' || t.scope === 'web-experience') return 'web'
  return 'payments'
}

function thresholdValue(key: ThresholdKey, team: TeamKey, overrides?: Partial<Record<ThresholdKey, Partial<Record<TeamKey, number>>>>): number {
  const t = TRUST_THRESHOLDS.find((x) => x.key === key)!
  return overrides?.[key]?.[team] ?? t.team[team] ?? t.instance
}

function AdjustmentText({ adj }: { adj?: TrustAdjustment }) {
  if (!adj)
    return (
      <Text size="xs" c="dimmed">
        none
      </Text>
    )
  return (
    <Box>
      <Group gap={4} wrap="nowrap">
        <Badge size="xs" variant="light" color={adj.kind === 'pinned' ? 'indigo' : adj.value < 0 ? 'orange' : 'success'} leftSection={adj.kind === 'pinned' ? <TbPin size={10} /> : <TbPlusMinus size={10} />}>
          {adj.kind === 'pinned' ? `pinned ${adj.value}` : `offset ${adj.value > 0 ? '+' : ''}${adj.value}`}
        </Badge>
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
          {personName(adj.by)} · {adj.at}
          {adj.expires ? ` · expires ${adj.expires}` : ' · no expiry'}
        </Text>
      </Group>
      <Text size="xs" lineClamp={2}>
        {adj.reason}
      </Text>
    </Box>
  )
}

function Allows({ v, team }: { v: number; team: TeamKey }) {
  return (
    <Group gap={3} wrap="nowrap">
      {TRUST_THRESHOLDS.map((th) => {
        const need = thresholdValue(th.key, team)
        const ok = v >= need
        return (
          <Tooltip key={th.key} label={`${th.label} (≥ ${need}): ${ok ? th.above : th.below}`} multiline w={300}>
            <Badge size="xs" variant={ok ? 'light' : 'outline'} color={ok ? 'success' : 'gray'} px={4} leftSection={ok ? <TbCheck size={9} /> : <TbX size={9} />}>
              {need}
            </Badge>
          </Tooltip>
        )
      })}
    </Group>
  )
}

// ---------------------------------------------------------------- (A) per-user trust scores

function AdjustModal({ row, onClose, onSave }: { row: TrustScore | null; onClose: () => void; onSave: (adj: TrustAdjustment | undefined) => void }) {
  const [kind, setKind] = useState<'offset' | 'pinned' | 'clear'>(row?.adj?.kind ?? 'offset')
  const [value, setValue] = useState<number>(row?.adj?.value ?? 0)
  const [reason, setReason] = useState('')
  const [expires, setExpires] = useState(row?.adj?.expires ?? '')
  if (!row) return null
  const preview = kind === 'clear' ? row.computed : Math.max(0, Math.min(100, kind === 'pinned' ? value : row.computed + value))
  const team = teamOfScope(row)
  return (
    <Modal opened onClose={onClose} title={<Text fw={700}>Adjust trust · {personName(row.login)} · {row.scopeKind} {row.scope}</Text>} size={520}>
      <Stack gap="sm">
        <Text size="xs" c="dimmed">
          Computed {row.computed} from {row.basis}. An adjustment never edits the computed value: it sits on top and is shown wherever the score is.
        </Text>
        <SegmentedControl
          size="xs"
          value={kind}
          onChange={(v) => setKind(v as 'offset' | 'pinned' | 'clear')}
          data={[
            { value: 'offset', label: 'Offset (±)' },
            { value: 'pinned', label: 'Pin to a value' },
            { value: 'clear', label: 'Remove adjustment' },
          ]}
        />
        {kind !== 'clear' && (
          <NumberInput
            size="xs"
            label={kind === 'offset' ? 'Offset added to the computed score' : 'Pinned score (ignores the computed value)'}
            min={kind === 'offset' ? -50 : 0}
            max={kind === 'offset' ? 50 : 100}
            value={value}
            onChange={(v) => setValue(typeof v === 'number' ? v : Number(v) || 0)}
          />
        )}
        <Textarea size="xs" label="Reason (required, shown in the tooltip everywhere the score appears)" autosize minRows={2} value={reason} onChange={(e) => setReason(e.currentTarget.value)} />
        {kind !== 'clear' && <TextInput size="xs" label="Expires (optional)" placeholder="YYYY-MM-DD · blank = until removed" value={expires} onChange={(e) => setExpires(e.currentTarget.value)} />}
        <Paper withBorder p="xs" radius="sm">
          <Group justify="space-between">
            <Text size="sm">
              Effective {effectiveTrust(row)} → <b>{preview}</b>
            </Text>
            <Allows v={preview} team={team} />
          </Group>
        </Paper>
        <Group justify="flex-end">
          <Button size="xs" variant="subtle" color="gray" onClick={onClose}>
            Cancel
          </Button>
          <Button
            size="xs"
            disabled={!reason.trim()}
            onClick={() => onSave(kind === 'clear' ? undefined : { kind, value, reason: reason.trim(), by: VIEWER, at: '2026-11-22', expires: expires.trim() || undefined })}
          >
            Save adjustment
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

function TrustScores() {
  const [rows, setRows] = useState<TrustScore[]>(TRUST)
  const [kind, setKind] = useState<'all' | TrustScopeKind>('all')
  const [q, setQ] = useState('')
  const [editing, setEditing] = useState<TrustScore | null>(null)
  const [log, setLog] = useState<string[]>([])
  const shown = rows.filter((r) => (kind === 'all' || r.scopeKind === kind) && (!q || `${personName(r.login)} ${r.login} ${r.scope}`.toLowerCase().includes(q.toLowerCase())))
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Group gap={6}>
            <TbShieldCheck size={18} />
            <Text fw={700} size="lg">
              Trust scores
            </Text>
          </Group>
          <Text size="xs" c="dimmed">
            Per person, per scope. Computed nightly from outcomes over 180 days; leads and maintainers can offset or pin a score with a reason. Viewing as Hugo Martin
            (payments-core lead, payments-api maintainer).
          </Text>
        </Box>
        <Group gap="sm">
          <TextInput size="xs" placeholder="Filter people or scopes" value={q} onChange={(e) => setQ(e.currentTarget.value)} />
          <SegmentedControl
            size="xs"
            value={kind}
            onChange={(v) => setKind(v as 'all' | TrustScopeKind)}
            data={[
              { value: 'all', label: 'All scopes' },
              { value: 'repo', label: 'Repo' },
              { value: 'team', label: 'Team' },
              { value: 'area', label: 'Area' },
            ]}
          />
        </Group>
      </Group>
      <Alert color="indigo" variant="light" p={8} icon={<TbDatabase size={16} />}>
        <Text size="xs">
          Stored in Antiphon (database), not in repo files. Every adjustment is a revision with who, when, reason and optional expiry; an expired adjustment
          drops off and the computed value applies again. The most specific scope wins for an action: area, then repo, then team.
        </Text>
      </Alert>
      <Paper withBorder radius="md">
        <Table verticalSpacing={6} fz="sm" highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th w={170}>Person</Table.Th>
              <Table.Th w={170}>Scope</Table.Th>
              <Table.Th ta="right" w={80}>
                Computed
              </Table.Th>
              <Table.Th>Manual adjustment</Table.Th>
              <Table.Th ta="right" w={80}>
                Effective
              </Table.Th>
              <Table.Th w={170}>
                <Tooltip multiline w={300} label="One chip per threshold (clear a blocker, wide dismissal, decide a gate, random assignment), showing the value it needs. Green: allowed.">
                  <span>Allows (thresholds)</span>
                </Tooltip>
              </Table.Th>
              <Table.Th w={90} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {shown.map((r) => {
              const can = VIEWER_SCOPES.has(r.scope) && r.login !== VIEWER
              const eff = effectiveTrust(r)
              return (
                <Table.Tr key={`${r.login}:${r.scopeKind}:${r.scope}`}>
                  <Table.Td>
                    <PersonChip login={r.login} size="xs" />
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <Badge size="xs" variant="light" color={SCOPE_KIND_META[r.scopeKind].color}>
                        {SCOPE_KIND_META[r.scopeKind].label}
                      </Badge>
                      <Text size="xs" ff="monospace">
                        {r.scope}
                      </Text>
                    </Group>
                  </Table.Td>
                  <Table.Td ta="right">
                    <Tooltip label={r.basis}>
                      <Text ff="monospace" size="sm" c="dimmed" style={{ cursor: 'help' }}>
                        {r.computed}
                      </Text>
                    </Tooltip>
                  </Table.Td>
                  <Table.Td>
                    <AdjustmentText adj={r.adj} />
                  </Table.Td>
                  <Table.Td ta="right">
                    <Text ff="monospace" fw={700} size="sm" c={r.adj ? (r.adj.kind === 'pinned' ? 'indigo.3' : r.adj.value < 0 ? 'orange.4' : 'success.4') : undefined}>
                      {eff}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Allows v={eff} team={teamOfScope(r)} />
                  </Table.Td>
                  <Table.Td>
                    <Tooltip
                      disabled={can}
                      label={r.login === VIEWER ? "You can't adjust your own score" : 'Only leads and maintainers of this scope can adjust it'}
                    >
                      <Button size="compact-xs" variant="light" disabled={!can} leftSection={can ? <TbAdjustments size={12} /> : <TbLock size={12} />} onClick={() => setEditing(r)}>
                        Adjust
                      </Button>
                    </Tooltip>
                  </Table.Td>
                </Table.Tr>
              )
            })}
          </Table.Tbody>
        </Table>
      </Paper>
      {log.length > 0 && (
        <Paper withBorder radius="md" p="xs">
          <SectionLabel>Saved this session (mock)</SectionLabel>
          {log.map((l) => (
            <Text key={l} size="xs">
              {l}
            </Text>
          ))}
        </Paper>
      )}
      {editing && (
        <AdjustModal
          key={`${editing.login}:${editing.scope}`}
          row={editing}
          onClose={() => setEditing(null)}
          onSave={(adj) => {
            setRows((rs) => rs.map((x) => (x === editing ? { ...x, adj } : x)))
            setLog((l) => [
              ...l,
              `${personName(editing.login)} · ${editing.scopeKind} ${editing.scope}: ${adj ? `${adj.kind} ${adj.value} (${adj.reason})${adj.expires ? `, expires ${adj.expires}` : ''}` : 'adjustment removed'} · by Hugo Martin, 2026-11-22`,
            ])
            setEditing(null)
          }}
        />
      )}
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) thresholds and bypass categories

const TEAM_COLS: TeamKey[] = ['payments', 'platform', 'web']

function RevisionTable({ revs }: { revs: SettingRevision[] }) {
  return (
    <Table fz="xs" verticalSpacing={3}>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>rev</Table.Th>
          <Table.Th>edited</Table.Th>
          <Table.Th>by</Table.Th>
          <Table.Th>layer</Table.Th>
          <Table.Th>change</Table.Th>
          <Table.Th>reason</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {revs.map((r) => (
          <Table.Tr key={r.rev}>
            <Table.Td ff="monospace">r{r.rev}</Table.Td>
            <Table.Td ff="monospace" style={{ whiteSpace: 'nowrap' }}>
              {r.at}
            </Table.Td>
            <Table.Td style={{ whiteSpace: 'nowrap' }}>{personName(r.by)}</Table.Td>
            <Table.Td>
              <Badge size="xs" variant="light" color={r.layer === 'instance' ? 'gray' : 'grape'}>
                {r.layer === 'instance' ? 'instance' : TEAMS[r.layer].slug}
              </Badge>
            </Table.Td>
            <Table.Td ff="monospace">{r.change}</Table.Td>
            <Table.Td c="dimmed">{r.reason ?? '—'}</Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  )
}

function BypassList() {
  const [cats, setCats] = useState<BypassCategory[]>(BYPASS_CATEGORIES)
  return (
    <Paper withBorder radius="md" p="sm">
      <SectionLabel right={<Badge size="xs" variant="light" color="gray">instance · payments-core inherits</Badge>}>Bypass categories</SectionLabel>
      <Text size="xs" c="dimmed" mb={6}>
        Findings flagged with an enabled category post in every round regardless of the round's severity/confidence bar, and the adjudicator can hold
        but can't concede them alone (a dispute escalates to the owning team). In PR Review they carry a "bypasses round bar" badge.
      </Text>
      <Stack gap={6}>
        {cats.map((c) => (
          <Group key={c.key} justify="space-between" wrap="nowrap">
            <Box>
              <Text size="sm" ff="monospace" fw={600}>
                {c.label}
              </Text>
              <Text size="xs" c="dimmed">
                {c.hint}
                {c.changed && ` · last changed by ${personName(c.changed.by)} on ${c.changed.at}`}
              </Text>
            </Box>
            <Switch size="sm" checked={c.on} onChange={(e) => { const on = e.currentTarget.checked; setCats((cs) => cs.map((x) => (x.key === c.key ? { ...x, on, changed: { by: VIEWER, at: '2026-11-22' } } : x))) }} />
          </Group>
        ))}
      </Stack>
    </Paper>
  )
}

function Thresholds() {
  const [team, setTeam] = useState<TeamKey>('payments')
  const [staged, setStaged] = useState<Partial<Record<ThresholdKey, Partial<Record<TeamKey, number>>>>>({})
  const [reason, setReason] = useState('')
  const [saved, setSaved] = useState(false)
  const canEdit = team === 'payments'
  const n = Object.values(staged).reduce((a, m) => a + Object.keys(m ?? {}).length, 0)
  return (
    <Stack gap="sm">
      <Box>
        <Text fw={700} size="lg">
          Trust thresholds
        </Text>
        <Text size="xs" c="dimmed">
          Each threshold gates one action. At or above it a person acts alone; below it the action needs a second reviewer, becomes a proposal, or isn't
          offered.
        </Text>
      </Box>
      <Alert color="indigo" variant="light" p={8} icon={<TbDatabase size={16} />}>
        <Text size="xs">
          Editable settings stored in Antiphon (database rows with revisions), not in repo files: instance defaults set by the instance admin, team
          overrides set by that team's lead. The effective value is the team override if there is one, else the instance default.
        </Text>
      </Alert>
      <Group gap="sm">
        <Text size="xs" c="dimmed">
          Team column
        </Text>
        <SegmentedControl size="xs" value={team} onChange={(v) => { setTeam(v as TeamKey); setSaved(false) }} data={TEAM_COLS.map((t) => ({ value: t, label: TEAMS[t].slug }))} />
        <Text size="xs" c="dimmed">
          {canEdit ? 'Viewing as Hugo Martin, payments-core lead: this column is editable.' : 'Read only for you.'}
        </Text>
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 360px', gap: 12, alignItems: 'start' }}>
        <Stack gap="sm">
          <Paper withBorder radius="md">
            <Table verticalSpacing={6} fz="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w={170}>Threshold</Table.Th>
                  <Table.Th ta="right" w={70}>
                    Instance
                  </Table.Th>
                  <Table.Th w={110}>{TEAMS[team].slug}</Table.Th>
                  <Table.Th ta="right" w={70}>
                    Effective
                  </Table.Th>
                  <Table.Th>At or above</Table.Th>
                  <Table.Th>Below</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {TRUST_THRESHOLDS.map((th) => {
                  const st = staged[th.key]?.[team]
                  const override = st ?? th.team[team]
                  const eff = override ?? th.instance
                  return (
                    <Table.Tr key={th.key}>
                      <Table.Td>
                        <Text size="sm" fw={600}>
                          {th.label}
                        </Text>
                        <Text size="xs" c="dimmed">
                          gates: {th.gates}
                        </Text>
                      </Table.Td>
                      <Table.Td ta="right">
                        <Text ff="monospace" size="sm" c={override === undefined ? undefined : 'dimmed'}>
                          {th.instance}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Group gap={4} wrap="nowrap">
                          <NumberInput
                            size="xs"
                            w={64}
                            min={0}
                            max={100}
                            placeholder="—"
                            disabled={!canEdit}
                            value={override ?? ''}
                            onChange={(v) => {
                              setSaved(false)
                              setStaged((m) => {
                                const num = typeof v === 'number' ? v : Number(v)
                                const next = { ...m, [th.key]: { ...(m[th.key] ?? {}) } }
                                if (v === '' || Number.isNaN(num) || num === th.team[team]) delete next[th.key]![team]
                                else next[th.key]![team] = num
                                return next
                              })
                            }}
                          />
                          {st !== undefined && (
                            <Badge size="xs" color="warning" variant="light">
                              staged
                            </Badge>
                          )}
                        </Group>
                      </Table.Td>
                      <Table.Td ta="right">
                        <Text ff="monospace" fw={700} size="sm" c={st !== undefined ? 'warning.4' : override !== undefined ? 'grape.4' : undefined}>
                          {eff}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs" c="success.4">
                          {th.above}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Badge size="xs" variant="outline" color="orange" mb={2}>
                          {th.belowKind === 'second-reviewer' ? 'second reviewer' : th.belowKind === 'proposal' ? 'becomes a proposal' : th.belowKind === 'not-assignable' ? "can't be assigned" : 'not allowed'}
                        </Badge>
                        <Text size="xs" c="orange.3">
                          {th.below}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          </Paper>
          {canEdit && (
            <Paper withBorder radius="md" p="xs">
              <Group gap="sm" align="flex-end" wrap="nowrap">
                <TextInput size="xs" style={{ flex: 1 }} label="Reason for this revision (required)" value={reason} onChange={(e) => setReason(e.currentTarget.value)} />
                <Button size="xs" variant="subtle" color="gray" disabled={n === 0} onClick={() => setStaged({})}>
                  Discard
                </Button>
                <Button size="xs" disabled={n === 0 || !reason.trim()} onClick={() => { setSaved(true); setStaged({}); setReason('') }}>
                  Save as r{TRUST_THRESHOLD_REVISIONS[0].rev + 1}
                </Button>
              </Group>
              {saved && (
                <Text size="xs" c="success.4" mt={6}>
                  Saved r{TRUST_THRESHOLD_REVISIONS[0].rev + 1} (mock). Applies to the next action checked; open queue items keep the rule they were created under.
                </Text>
              )}
            </Paper>
          )}
          <Paper withBorder radius="md" p="xs">
            <Group gap={6} mb={4}>
              <TbHistory size={14} />
              <Text size="xs" fw={700} tt="uppercase" c="dimmed">
                Revision history · edited by / when
              </Text>
            </Group>
            <RevisionTable revs={TRUST_THRESHOLD_REVISIONS} />
          </Paper>
        </Stack>
        <BypassList />
      </Box>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Trust Settings',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Trust per person per scope: computed, manual adjustment (offset or pinned, reason, who, when, expiry), effective. "Adjust" opens a small form. */
export const Scores: Story = {
  name: 'A · Trust scores (per scope)',
  render: () => (
    <Box p="md">
      <TrustScores />
    </Box>
  ),
}

/** (B) Thresholds with the action each gates and the behaviour above vs below; instance defaults, team overrides, revisions. Bypass categories alongside. */
export const ThresholdsAndBypass: Story = {
  name: 'B · Thresholds and bypass categories',
  render: () => (
    <Box p="md">
      <Thresholds />
    </Box>
  ),
}
