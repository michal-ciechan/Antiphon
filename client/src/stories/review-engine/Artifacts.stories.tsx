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
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { TbArrowRight, TbCheck, TbExternalLink, TbSearch } from 'react-icons/tb'
import {
  ARTIFACTS,
  ARTIFACT_KINDS,
  ARTIFACT_KIND_META,
  PACK_INSTANCES,
  RUN618_INSTANCES,
  VALIDATION_META,
  instanceByKey,
  instancesOf,
  nodeIO,
  type ArtifactInstance,
  type Validation,
} from './artifacts'
import { ArtifactChip, ArtifactDetail, ArtifactKindIcon, InputCountBadge, NodeIOTabs, RuleList, ValidationDot, type ChipHighlight } from './ArtifactViews'
import { MODELS, NODE_KIND_META, NODE_STATUS_META, RUN618_NODES, nodePurpose, type ModelKey } from './mockData'
import { ModelBadge, SectionLabel } from './shared'

/**
 * PROPOSAL MOCK — not production code. Artifacts for
 * `docs/features/012-review-engine/design.md` §3.6: every node declares named, typed inputs and
 * outputs; each artifact has a definition (kind, format, fields, producer and readers, validation
 * rules, storage, where to edit it) and each run's instances are validated. Run: payments-api#618,
 * iteration 3, with the concurrency reviewer still running.
 *   A — catalogue: artifact kinds grouped by kind, list + definition
 *   B — lineage: steps and the artifacts flowing between them; click a step for its Inputs/Outputs
 *   C — pack validation: the four context packs side by side with the cross-pack prefix check
 */

function counts(list: ArtifactInstance[]): Record<Validation, number> {
  const c: Record<Validation, number> = { valid: 0, warning: 0, failed: 0, pending: 0 }
  for (const i of list) c[i.status]++
  return c
}

function StatusSummary({ list }: { list: ArtifactInstance[] }) {
  const c = counts(list)
  return (
    <Group gap="sm">
      {(Object.keys(c) as Validation[]).map((v) => (
        <Group key={v} gap={4} wrap="nowrap">
          <ValidationDot status={v} />
          <Text size="xs" c={c[v] ? undefined : 'dimmed'}>
            {c[v]} {VALIDATION_META[v].label}
          </Text>
        </Group>
      ))}
    </Group>
  )
}

// ---------------------------------------------------------------- (A) catalogue

function Catalogue() {
  const [sel, setSel] = useState('categorisation')
  const [q, setQ] = useState('')
  const [mode, setMode] = useState<string>('definition')
  const [instKey, setInstKey] = useState<string | null>(null)
  const insts = instancesOf(sel)
  const inst = mode === 'instance' ? (insts.find((i) => i.key === instKey) ?? insts[0]) : undefined
  const match = (id: string) => {
    const a = ARTIFACTS.find((x) => x.id === id)!
    return !q || `${a.id} ${a.title} ${a.format} ${a.producedBy.join(' ')}`.toLowerCase().includes(q.toLowerCase())
  }
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Text fw={700} size="lg">
            Artifacts · pr-review
          </Text>
          <Text size="xs" c="dimmed">
            {ARTIFACTS.length} artifact kinds · what each step outputs, how it is checked, and where its definition lives
          </Text>
        </Box>
        <TextInput size="xs" w={260} placeholder="Find an artifact, format or step" leftSection={<TbSearch size={12} />} value={q} onChange={(e) => setQ(e.currentTarget.value)} />
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: '340px minmax(0, 1fr)', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p={6}>
          {ARTIFACT_KINDS.map((k) => {
            const list = ARTIFACTS.filter((a) => a.kind === k && match(a.id))
            if (list.length === 0) return null
            return (
              <Box key={k} mb="xs">
                <Tooltip label={ARTIFACT_KIND_META[k].blurb} multiline w={300} position="right">
                  <Group gap={6} mb={4}>
                    <ArtifactKindIcon kind={k} />
                    <Text size="xs" fw={700} tt="uppercase" c="dimmed" style={{ letterSpacing: 0.8 }}>
                      {ARTIFACT_KIND_META[k].label}
                    </Text>
                  </Group>
                </Tooltip>
                <Stack gap={3}>
                  {list.map((a) => {
                    const its = instancesOf(a.id)
                    const c = counts(its)
                    return (
                      <UnstyledButton
                        key={a.id}
                        onClick={() => {
                          setSel(a.id)
                          setInstKey(null)
                        }}
                        p={6}
                        style={{ borderRadius: 4, background: sel === a.id ? 'var(--mantine-color-dark-5)' : undefined }}
                      >
                        <Group justify="space-between" wrap="nowrap" gap={6}>
                          <Box style={{ minWidth: 0 }}>
                            <Text size="sm" fw={500} truncate>
                              {a.title}
                            </Text>
                            <Text size="10px" c="dimmed" ff="monospace" truncate>
                              {a.format} · {a.producedBy.join(', ')}
                            </Text>
                          </Box>
                          <Group gap={3} wrap="nowrap">
                            {its.map((i) => (
                              <ValidationDot key={i.key} status={i.status} size={7} />
                            ))}
                            {its.length === 0 && (
                              <Text size="10px" c="dimmed">
                                not in it3
                              </Text>
                            )}
                            {c.warning > 0 && (
                              <Badge size="xs" color="warning" variant="light">
                                {c.warning}
                              </Badge>
                            )}
                          </Group>
                        </Group>
                      </UnstyledButton>
                    )
                  })}
                </Stack>
              </Box>
            )
          })}
        </Paper>
        <Paper withBorder radius="md" p="sm">
          <Group justify="space-between" mb="xs">
            <SegmentedControl
              size="xs"
              value={mode}
              onChange={setMode}
              data={[
                { value: 'definition', label: 'Definition' },
                { value: 'instance', label: `#618 it3 instances (${insts.length})`, disabled: insts.length === 0 },
              ]}
            />
            {mode === 'instance' && insts.length > 1 && (
              <Group gap={4}>
                {insts.map((i) => (
                  <Button key={i.key} size="compact-xs" variant={(inst?.key ?? '') === i.key ? 'filled' : 'default'} onClick={() => setInstKey(i.key)} rightSection={<ValidationDot status={i.status} size={7} />}>
                    {i.label}
                  </Button>
                ))}
              </Group>
            )}
          </Group>
          <ArtifactDetail artifactId={sel} instance={inst} />
        </Paper>
      </Box>
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) lineage

const COLUMNS: { nodes: string[]; note?: string }[] = [
  { nodes: ['human-approve'], note: 'iteration 2' },
  { nodes: ['intake'] },
  { nodes: ['delta'] },
  { nodes: ['categorise'] },
  { nodes: ['select'] },
  { nodes: ['group'] },
  { nodes: ['packs'] },
  { nodes: ['reviewer:security', 'reviewer:concurrency', 'reviewer:data', 'reviewer:general'] },
  { nodes: ['collate'] },
  { nodes: ['verify'] },
  { nodes: ['publish'] },
  { nodes: ['route'] },
]

type Sel = { type: 'artifact'; key: string } | { type: 'def'; artifact: string } | { type: 'node'; id: string }

function NodeCard({ id, ring, selected, onClick }: { id: string; ring?: string; selected: boolean; onClick: () => void }) {
  const def = RUN618_NODES.find((n) => n.id === id)!
  const k = NODE_KIND_META[def.kind]
  const st = def.status
  const statusColor = NODE_STATUS_META[st].color
  const edge = selected ? '#fff' : ring ? `var(--mantine-color-${ring}-5)` : 'var(--mantine-color-dark-4)'
  return (
    <UnstyledButton onClick={onClick} w="100%" aria-pressed={selected}>
      <Paper
        px={6}
        py={5}
        radius="sm"
        style={{
          borderStyle: 'solid',
          borderWidth: '3px 1px 1px 1px',
          borderColor: `var(--mantine-color-${k.color}-6) ${edge} ${edge} ${edge}`,
          boxShadow: selected || ring ? `0 0 0 1px ${edge}` : undefined,
          background: 'var(--mantine-color-dark-7)',
        }}
      >
        <Group gap={4} wrap="nowrap" justify="space-between">
          <Text size="xs" fw={700} ff="monospace" truncate>
            {id.replace('reviewer:', 'rev:')}
          </Text>
          <InputCountBadge nodeId={id} />
        </Group>
        <Group gap={4} wrap="nowrap">
          <Text size="10px" c="dimmed">
            {k.label}
          </Text>
          <Text size="10px" c={`${statusColor}.4`}>
            · {NODE_STATUS_META[st].label}
          </Text>
        </Group>
      </Paper>
    </UnstyledButton>
  )
}

function Lineage() {
  const [sel, setSel] = useState<Sel>({ type: 'artifact', key: 'context-pack:data' })
  const selInst = sel.type === 'artifact' ? instanceByKey(sel.key) : undefined

  const nodeRing = (id: string): string | undefined => {
    if (selInst) {
      if (selInst.producedBy === id) return 'active'
      if (selInst.consumedBy.includes(id)) return 'success'
    }
    return undefined
  }
  const chipHighlight = (i: ArtifactInstance): ChipHighlight | undefined => {
    if (sel.type !== 'node') return undefined
    if (i.producedBy === sel.id) return 'output'
    if (i.consumedBy.includes(sel.id)) return 'input'
    return undefined
  }
  const openArtifact = (artifact: string, instanceKey?: string) => {
    if (instanceKey) return setSel({ type: 'artifact', key: instanceKey })
    const nodeId = sel.type === 'node' ? sel.id : undefined
    const inst = RUN618_INSTANCES.find((i) => i.artifact === artifact && (!nodeId || i.consumedBy.includes(nodeId) || i.producedBy === nodeId))
    setSel(inst ? { type: 'artifact', key: inst.key } : { type: 'def', artifact })
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end" wrap="nowrap">
        <Box>
          <Text fw={700} size="lg">
            Artifact lineage · payments-api#618 · iteration 3
          </Text>
          <Text size="xs" c="dimmed">
            Steps (top of each column) and the artifacts they produced. Click an artifact: its producer rings blue, its readers green. Click a step: its inputs ring cyan, its
            outputs purple, and the panel lists every input it decides from.
          </Text>
        </Box>
        <StatusSummary list={RUN618_INSTANCES} />
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 480px', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p="xs">
          <Box>
            <Group gap={0} wrap="wrap" align="flex-start" pb={6} style={{ rowGap: 14 }}>
              {COLUMNS.map((col, ci) => (
                <Group key={ci} gap={0} wrap="nowrap" align="flex-start">
                  <Stack gap={6} w={150}>
                    {col.note && (
                      <Text size="10px" c="pink.4" ta="center">
                        {col.note}
                      </Text>
                    )}
                    {col.nodes.map((n) => (
                      <Stack key={n} gap={4}>
                        <NodeCard id={n} ring={nodeRing(n)} selected={sel.type === 'node' && sel.id === n} onClick={() => setSel({ type: 'node', id: n })} />
                        {RUN618_INSTANCES.filter((i) => i.producedBy === n).map((i) => (
                          <Box key={i.key} pl={8} style={{ borderLeft: '1px dashed var(--mantine-color-dark-3)' }}>
                            <ArtifactChip
                              artifactId={i.artifact}
                              instance={i}
                              selected={sel.type === 'artifact' && sel.key === i.key}
                              highlight={chipHighlight(i)}
                              onClick={() => setSel({ type: 'artifact', key: i.key })}
                            />
                          </Box>
                        ))}
                      </Stack>
                    ))}
                  </Stack>
                  {ci < COLUMNS.length - 1 && (
                    <Box w={18} pt={col.note ? 30 : 14} c="dimmed" style={{ display: 'flex', justifyContent: 'center' }}>
                      <TbArrowRight size={12} />
                    </Box>
                  )}
                </Group>
              ))}
            </Group>
          </Box>
          <Text size="10px" c="dimmed" mt={4}>
            Not reached in iteration 3: await-fixes, base-drift, needs-human-approval, bot-approve, human-review, base-moved, update-branch, await-merge. Their artifacts are in the catalogue.
          </Text>
        </Paper>
        <Paper withBorder radius="md" p="sm" mah="calc(100vh - 140px)" style={{ overflow: 'auto' }}>
          {sel.type === 'node' ? (
            <Stack gap="xs">
              <Group gap={6}>
                <Badge color={NODE_KIND_META[RUN618_NODES.find((n) => n.id === sel.id)!.kind].color} variant="light">
                  {RUN618_NODES.find((n) => n.id === sel.id)!.kind}
                </Badge>
                <Text fw={700} ff="monospace">
                  {sel.id}
                </Text>
              </Group>
              <Text size="sm">{nodePurpose(sel.id)}</Text>
              {nodeIO(sel.id) && <NodeIOTabs nodeId={sel.id} onArtifact={openArtifact} />}
            </Stack>
          ) : sel.type === 'artifact' && selInst ? (
            <ArtifactDetail artifactId={selInst.artifact} instance={selInst} onNode={(id) => setSel({ type: 'node', id: id === 'reviewer' ? 'reviewer:concurrency' : id })} />
          ) : sel.type === 'def' ? (
            <ArtifactDetail artifactId={sel.artifact} onNode={(id) => setSel({ type: 'node', id })} />
          ) : null}
        </Paper>
      </Box>
    </Stack>
  )
}

// ---------------------------------------------------------------- (C) pack validation

function PackValidation() {
  const budget = 68_000
  const prefixHash = '9c41e2a7'
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end">
        <Box>
          <Text fw={700} size="lg">
            Context packs · validation · payments-api#618 · iteration 3
          </Text>
          <Text size="xs" c="dimmed">
            BuildPacks validates every pack before it is staged to a runner. A failed rule fails the node and opens an attention item; warnings are shown here and on the run
            page.
          </Text>
        </Box>
        <StatusSummary list={RUN618_INSTANCES.filter((i) => i.artifact === 'context-pack')} />
      </Group>
      <Paper withBorder radius="md" p="xs" style={{ borderLeft: '3px solid var(--mantine-color-success-6)' }}>
        <Group justify="space-between" wrap="nowrap">
          <Box>
            <Group gap={6}>
              <TbCheck size={14} color="var(--mantine-color-success-5)" />
              <Text size="sm" fw={600}>
                Cross-pack check: shared prefix byte-identical in 4/4 packs
              </Text>
            </Group>
            <Text size="xs" c="dimmed">
              Same bytes → same sha256 → the prompt cache applies to every reviewer after the first.
            </Text>
          </Box>
          <Group gap="md" wrap="nowrap">
            {PACK_INSTANCES.map((p) => (
              <Box key={p.group} ta="center">
                <Text size="10px" c="dimmed">
                  {p.group}
                </Text>
                <Text size="xs" ff="monospace" c="success.4">
                  {prefixHash}…
                </Text>
              </Box>
            ))}
          </Group>
        </Group>
      </Paper>
      <SimpleGrid cols={4} spacing="sm">
        {PACK_INSTANCES.map((p) => {
          const inst = instanceByKey(`context-pack:${p.group}`)!
          return (
            <Paper key={p.group} withBorder radius="md" p="sm" style={{ borderTop: `3px solid var(--mantine-color-${VALIDATION_META[p.status].color}-6)` }}>
              <Group justify="space-between" wrap="nowrap">
                <Text fw={700}>{p.group}</Text>
                <Badge size="sm" variant="light" color={VALIDATION_META[p.status].color}>
                  {VALIDATION_META[p.status].label}
                </Badge>
              </Group>
              <Group gap={6} mt={4}>
                <ModelBadge model={p.model as ModelKey} withProvider />
                <Text size="10px" c="dimmed" ff="monospace">
                  pack-{p.group}.md
                </Text>
              </Group>
              <Box mt="xs">
                <Group justify="space-between">
                  <Text size="xs" c="dimmed">
                    Tokens
                  </Text>
                  <Text size="xs" ff="monospace">
                    {((p.prefixTokens + p.suffixTokens) / 1000).toFixed(1)}k / {budget / 1000}k
                  </Text>
                </Group>
                <Progress.Root size={10} mt={2}>
                  <Tooltip label={`shared prefix ${(p.prefixTokens / 1000).toFixed(1)}k (cached)`}>
                    <Progress.Section value={(p.prefixTokens / budget) * 100} color="active" />
                  </Tooltip>
                  <Tooltip label={`group suffix ${(p.suffixTokens / 1000).toFixed(1)}k`}>
                    <Progress.Section value={(p.suffixTokens / budget) * 100} color="grape" />
                  </Tooltip>
                </Progress.Root>
              </Box>
              <Box mt="xs">
                <SectionLabel>Assigned checks</SectionLabel>
                <Group gap={4}>
                  {p.checks.map((c) => (
                    <Badge key={c} size="xs" variant="outline" color="gray" ff="monospace" tt="none">
                      {c}
                    </Badge>
                  ))}
                </Group>
              </Box>
              <Box mt="xs">
                <SectionLabel>Rules</SectionLabel>
                <RuleList artifactId="context-pack" results={inst.results} />
              </Box>
              <Button size="compact-xs" variant="light" mt="sm" leftSection={<TbExternalLink size={11} />}>
                Open in pack viewer
              </Button>
            </Paper>
          )
        })}
      </SimpleGrid>
      <Text size="xs" c="dimmed">
        Reviewer tier models: {Object.values(MODELS).filter((m) => m.role.startsWith('reviewer')).map((m) => m.name).join(', ')}. Rules are defined once per artifact kind (see the
        catalogue) and evaluated per instance.
      </Text>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Artifacts',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Every artifact kind the pr-review workflow produces: definition, fields, rules, where to edit, an example; switch to this run's instances. */
export const CatalogueView: Story = { name: 'A · Catalogue', render: () => <Catalogue /> }

/** (B) Lineage for #618 iteration 3: steps and the artifacts between them. Click a step for its Inputs/Outputs tabs; click an artifact for its definition and validation. */
export const LineageView: Story = { name: 'B · Lineage + step inputs', render: () => <Lineage /> }

/** (C) The four context packs side by side with their validation checklists and the cross-pack prefix-hash check. */
export const PackValidationView: Story = { name: 'C · Pack validation', render: () => <PackValidation /> }
