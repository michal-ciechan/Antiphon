/**
 * PROPOSAL MOCK — not production code. Artifact and node-input views for the review-engine
 * Storybook prototypes (`docs/features/012-review-engine/design.md` §3.6). Components only, so
 * RunDetail's node panel can import ArtifactChip, ArtifactDetail, NodeIOTabs and
 * InputCountBadge. Nothing outside `src/stories/review-engine/` imports this file.
 */
import { Badge, Box, Button, Code, Divider, Group, Paper, Stack, Table, Tabs, Text, Tooltip, UnstyledButton } from '@mantine/core'
import type { ReactNode } from 'react'
import {
  TbAlertTriangle,
  TbArrowBarToRight,
  TbArrowRight,
  TbBinaryTree,
  TbBolt,
  TbBraces,
  TbCheck,
  TbClock,
  TbDatabase,
  TbExternalLink,
  TbFileCode,
  TbFileText,
  TbGitPullRequest,
  TbPencil,
  TbSettings,
  TbUser,
  TbX,
} from 'react-icons/tb'
import {
  ARTIFACT_KIND_META,
  INPUT_KIND_META,
  VALIDATION_META,
  artifactById,
  nodeIO,
  nodeInstances,
  ruleText,
  type ArtifactInstance,
  type ArtifactKind,
  type InputKind,
  type NodeInput,
  type RuleResult,
  type Validation,
} from './artifacts'
import { NODE_KIND_META, RUN618_NODES } from './mockData'
import { SectionLabel } from './shared'

const KIND_ICON: Record<ArtifactKind, (size: number) => ReactNode> = {
  decision: (s) => <TbBinaryTree size={s} />,
  'abf-document': (s) => <TbFileCode size={s} />,
  'json-record': (s) => <TbBraces size={s} />,
  file: (s) => <TbFileText size={s} />,
  'code-host-object': (s) => <TbGitPullRequest size={s} />,
  event: (s) => <TbBolt size={s} />,
}

const INPUT_ICON: Record<InputKind, (size: number) => ReactNode> = {
  artifact: (s) => <TbArrowBarToRight size={s} />,
  config: (s) => <TbSettings size={s} />,
  context: (s) => <TbDatabase size={s} />,
  human: (s) => <TbUser size={s} />,
}

export function ArtifactKindIcon({ kind, size = 13 }: { kind: ArtifactKind; size?: number }) {
  return (
    <Box c={`${ARTIFACT_KIND_META[kind].color}.4`} style={{ display: 'flex', flexShrink: 0 }}>
      {KIND_ICON[kind](size)}
    </Box>
  )
}

export function ValidationDot({ status, size = 8 }: { status: Validation; size?: number }) {
  return (
    <Tooltip label={`validation: ${VALIDATION_META[status].label}`} withArrow>
      <Box
        w={size}
        h={size}
        style={{
          borderRadius: size,
          flexShrink: 0,
          background: status === 'pending' ? 'transparent' : `var(--mantine-color-${VALIDATION_META[status].color}-5)`,
          border: status === 'pending' ? '1.5px dashed var(--mantine-color-gray-5)' : undefined,
        }}
      />
    </Tooltip>
  )
}

function RuleIcon({ status }: { status: RuleResult['status'] }) {
  if (status === 'pass') return <TbCheck size={13} color="var(--mantine-color-success-5)" />
  if (status === 'warn') return <TbAlertTriangle size={13} color="var(--mantine-color-warning-5)" />
  if (status === 'fail') return <TbX size={13} color="var(--mantine-color-danger-5)" />
  return <TbClock size={13} color="var(--mantine-color-gray-5)" />
}

/** One artifact instance's rule results: icon, rule text, this run's detail. */
export function RuleList({ artifactId, results }: { artifactId: string; results: RuleResult[] }) {
  return (
    <Stack gap={4}>
      {results.map((r) => (
        <Group key={r.rule} gap={6} wrap="nowrap" align="flex-start">
          <Box mt={2}>
            <RuleIcon status={r.status} />
          </Box>
          <Box style={{ minWidth: 0 }}>
            <Text size="xs">{r.rule === 'produced' ? 'Not produced yet' : ruleText(artifactId, r.rule)}</Text>
            <Text size="xs" c={r.status === 'warn' ? 'warning.4' : r.status === 'fail' ? 'danger.4' : 'dimmed'}>
              {r.detail}
            </Text>
          </Box>
        </Group>
      ))}
    </Stack>
  )
}

export type ChipHighlight = 'producer' | 'consumer' | 'input' | 'output'

const HIGHLIGHT_COLOR: Record<ChipHighlight, string> = {
  producer: 'active',
  consumer: 'success',
  input: 'cyan',
  output: 'grape',
}

/** Artifact name + kind icon + validation dot. Clickable when onClick is given. */
export function ArtifactChip({
  artifactId,
  instance,
  selected = false,
  highlight,
  onClick,
  showSummary = false,
}: {
  artifactId: string
  instance?: ArtifactInstance
  selected?: boolean
  highlight?: ChipHighlight
  onClick?: () => void
  showSummary?: boolean
}) {
  const def = artifactById(artifactId)
  if (!def) return null
  const kind = ARTIFACT_KIND_META[def.kind]
  const ring = selected ? '#fff' : highlight ? `var(--mantine-color-${HIGHLIGHT_COLOR[highlight]}-5)` : 'var(--mantine-color-dark-4)'
  const body = (
    <Paper
      px={6}
      py={4}
      radius="sm"
      style={{
        borderStyle: 'solid',
        borderWidth: '1px 1px 1px 3px',
        borderColor: `${ring} ${ring} ${ring} var(--mantine-color-${kind.color}-6)`,
        boxShadow: selected || highlight ? `0 0 0 1px ${ring}` : undefined,
        background: selected ? 'var(--mantine-color-dark-5)' : 'var(--mantine-color-dark-6)',
        opacity: instance?.status === 'pending' ? 0.75 : 1,
      }}
    >
      <Group gap={5} wrap="nowrap">
        <ArtifactKindIcon kind={def.kind} size={12} />
        <Text size="xs" fw={600} truncate style={{ flex: 1 }}>
          {instance?.label ?? def.title}
        </Text>
        {instance && <ValidationDot status={instance.status} />}
      </Group>
      {showSummary && instance && (
        <Text size="10px" c="dimmed" truncate mt={1}>
          {instance.summary}
        </Text>
      )}
    </Paper>
  )
  return onClick ? (
    <UnstyledButton onClick={onClick} w="100%" aria-pressed={selected}>
      {body}
    </UnstyledButton>
  ) : (
    body
  )
}

function NodeLink({ id, onNode }: { id: string; onNode?: (id: string) => void }) {
  const def = RUN618_NODES.find((n) => n.id === id) ?? RUN618_NODES.find((n) => n.id.startsWith(`${id}:`))
  const color = def ? NODE_KIND_META[def.kind].color : 'gray'
  const content = (
    <Badge size="sm" variant="light" color={color} radius="sm" tt="none" ff="monospace" style={{ cursor: onNode ? 'pointer' : undefined }}>
      {id}
    </Badge>
  )
  return onNode ? (
    <UnstyledButton onClick={() => onNode(id)} display="inline-block">
      {content}
    </UnstyledButton>
  ) : (
    content
  )
}

/** The full definition of an artifact, optionally with one instance's validation results. */
export function ArtifactDetail({
  artifactId,
  instance,
  onNode,
}: {
  artifactId: string
  instance?: ArtifactInstance
  onNode?: (id: string) => void
}) {
  const def = artifactById(artifactId)
  if (!def) return <Text size="sm">Unknown artifact {artifactId}</Text>
  const kind = ARTIFACT_KIND_META[def.kind]
  return (
    <Stack gap="sm">
      <Box>
        <Group gap={8} wrap="nowrap">
          <ArtifactKindIcon kind={def.kind} size={18} />
          <Text fw={700} size="lg">
            {instance?.label ?? def.title}
          </Text>
          {instance && (
            <Badge size="sm" color={VALIDATION_META[instance.status].color} variant="light">
              {VALIDATION_META[instance.status].label}
            </Badge>
          )}
        </Group>
        <Group gap={6} mt={4}>
          <Tooltip label={kind.blurb} multiline w={300}>
            <Badge size="xs" color={kind.color} variant="outline">
              {kind.label}
            </Badge>
          </Tooltip>
          <Code fz={10}>{def.id}</Code>
          <Code fz={10}>{def.format}</Code>
          {def.perGroup && (
            <Badge size="xs" variant="light" color="gray">
              one per reviewer group
            </Badge>
          )}
        </Group>
      </Box>
      <Text size="sm">{def.purpose}</Text>

      <Box>
        <SectionLabel>Flow</SectionLabel>
        <Group gap={6} wrap="wrap">
          <Text size="xs" c="dimmed">
            produced by
          </Text>
          {def.producedBy.map((n) => (
            <NodeLink key={n} id={n} onNode={onNode} />
          ))}
          <TbArrowRight size={12} />
          <Text size="xs" c="dimmed">
            read by
          </Text>
          {def.consumedBy.map((n) => (
            <NodeLink key={n} id={n} onNode={onNode} />
          ))}
        </Group>
      </Box>

      <Box>
        <SectionLabel>Fields</SectionLabel>
        <Table fz="xs" verticalSpacing={3} horizontalSpacing={6} withRowBorders>
          <Table.Tbody>
            {def.fields.map((f) => (
              <Table.Tr key={f.name}>
                <Table.Td w="42%" style={{ verticalAlign: 'top' }}>
                  <Text size="xs" ff="monospace" fw={600}>
                    {f.name}
                  </Text>
                  <Text size="xs" ff="monospace" c="cyan.3">
                    {f.type}
                  </Text>
                </Table.Td>
                <Table.Td style={{ verticalAlign: 'top' }}>
                  <Text size="xs" c="dimmed">
                    {f.description}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Box>

      <Box>
        <SectionLabel>{instance ? `Validation · ${instance.label} · iteration ${instance.iteration}` : 'Validation rules'}</SectionLabel>
        <Stack gap={4}>
          {instance
            ? <RuleList artifactId={def.id} results={instance.results} />
            : def.rules.map((r) => (
                <Group key={r.id} gap={6} wrap="nowrap" align="flex-start">
                  <Text size="xs" c="dimmed" ff="monospace" w={110} style={{ flexShrink: 0 }}>
                    {r.id}
                  </Text>
                  <Text size="xs">{r.text}</Text>
                </Group>
              ))}
        </Stack>
      </Box>

      <Box>
        <SectionLabel>Where to edit</SectionLabel>
        <Stack gap={4}>
          {def.editIn.map((e) => (
            <Paper key={e.what} p={6} radius="sm" bg="dark.6">
              <Group justify="space-between" wrap="nowrap" gap={6}>
                <Box style={{ minWidth: 0 }}>
                  <Text size="xs" fw={600}>
                    {e.what}
                  </Text>
                  <Text size="xs" ff="monospace" c="indigo.3" truncate>
                    {e.path}
                  </Text>
                </Box>
                <Group gap={4} wrap="nowrap">
                  <Badge size="xs" variant="outline" color="gray" tt="none">
                    {e.layers}
                  </Badge>
                  <Tooltip label="Opens the layered file view (or settings); changes arrive as a PR">
                    <Button size="compact-xs" variant="subtle" leftSection={<TbPencil size={11} />}>
                      Edit
                    </Button>
                  </Tooltip>
                </Group>
              </Group>
            </Paper>
          ))}
        </Stack>
      </Box>

      <Group gap="lg">
        <Box>
          <Text size="xs" c="dimmed">
            Stored in
          </Text>
          <Text size="xs" ff="monospace">
            {instance && instance.ref !== '—' ? instance.ref : def.storage}
          </Text>
        </Box>
        <Box>
          <Text size="xs" c="dimmed">
            Retention
          </Text>
          <Text size="xs">{def.retention}</Text>
        </Box>
        {instance?.size && (
          <Box>
            <Text size="xs" c="dimmed">
              Size
            </Text>
            <Text size="xs">{instance.size}</Text>
          </Box>
        )}
      </Group>

      <Box>
        <SectionLabel>Example · payments-api#618 · iteration 3</SectionLabel>
        <Code block fz={10} style={{ maxHeight: 260, overflow: 'auto' }}>
          {def.example}
        </Code>
      </Box>
      {def.link && (
        <Group>
          <Button size="xs" variant="light" leftSection={<TbExternalLink size={12} />}>
            {def.link}
          </Button>
        </Group>
      )}
    </Stack>
  )
}

function InputRow({ input, onArtifact }: { input: NodeInput; onArtifact?: (artifactId: string) => void }) {
  const meta = INPUT_KIND_META[input.kind]
  const clickable = !!input.artifact && !!onArtifact
  const row = (
    <Group gap={8} wrap="nowrap" align="flex-start" py={5} px={4} style={{ borderTop: '1px solid var(--mantine-color-dark-5)' }}>
      <Tooltip label={meta.label}>
        <Box c={`${meta.color}.4`} mt={2} style={{ display: 'flex' }}>
          {INPUT_ICON[input.kind](14)}
        </Box>
      </Tooltip>
      <Box style={{ flex: 1, minWidth: 0 }}>
        <Group gap={6} wrap="nowrap" justify="space-between">
          <Text size="xs" fw={600} truncate>
            {input.name}
          </Text>
          {input.layer && (
            <Badge size="xs" variant="outline" color="gray" tt="none" style={{ flexShrink: 0 }}>
              {input.layer}
            </Badge>
          )}
        </Group>
        <Text size="xs" ff="monospace" c={input.kind === 'artifact' ? 'cyan.3' : 'indigo.3'} truncate>
          {input.source}
        </Text>
        <Text size="xs" c="dimmed">
          Decides: {input.decides}
        </Text>
        <Text size="xs">
          <Text span c="dimmed" size="xs">
            #618 it3:
          </Text>{' '}
          {input.example}
        </Text>
      </Box>
      {input.kind === 'config' && (
        <Tooltip label="Edit in the layered file view; arrives as a PR">
          <Button size="compact-xs" variant="subtle" leftSection={<TbPencil size={11} />}>
            Edit
          </Button>
        </Tooltip>
      )}
    </Group>
  )
  return clickable ? (
    <UnstyledButton w="100%" onClick={() => onArtifact!(input.artifact!)}>
      {row}
    </UnstyledButton>
  ) : (
    row
  )
}

/** Everything a step decides from: artifacts from earlier steps, then configuration and context. */
export function InputsPanel({ nodeId, onArtifact }: { nodeId: string; onArtifact?: (artifactId: string) => void }) {
  const io = nodeIO(nodeId)
  if (!io)
    return (
      <Text size="xs" c="dimmed">
        No inputs defined for {nodeId}.
      </Text>
    )
  const fromSteps = io.inputs.filter((i) => i.kind === 'artifact' || (i.kind === 'human' && i.artifact))
  const config = io.inputs.filter((i) => !fromSteps.includes(i))
  return (
    <Stack gap="xs">
      <Paper p={6} radius="sm" bg="dark.6" style={{ borderLeft: '3px solid var(--mantine-color-active-6)' }}>
        <Text size="xs" c="dimmed">
          This step decides
        </Text>
        <Text size="sm" fw={600}>
          {io.decides}
        </Text>
      </Paper>
      <Box>
        <SectionLabel right={<Text size="xs" c="dimmed">{fromSteps.length}</Text>}>From earlier steps (artifacts)</SectionLabel>
        {fromSteps.map((i) => (
          <InputRow key={i.name} input={i} onArtifact={onArtifact} />
        ))}
        {fromSteps.length === 0 && (
          <Text size="xs" c="dimmed">
            None: this step starts from configuration and context.
          </Text>
        )}
      </Box>
      <Box>
        <SectionLabel right={<Text size="xs" c="dimmed">{config.length}</Text>}>Configuration and context</SectionLabel>
        {config.map((i) => (
          <InputRow key={i.name} input={i} onArtifact={onArtifact} />
        ))}
      </Box>
    </Stack>
  )
}

/** What a step produces: artifact definitions, plus this run's instances with validation. */
export function OutputsPanel({ nodeId, onArtifact }: { nodeId: string; onArtifact?: (artifactId: string, instanceKey?: string) => void }) {
  const io = nodeIO(nodeId)
  const { outputs } = nodeInstances(nodeId)
  if (!io) return null
  return (
    <Stack gap="xs">
      {io.outputs.map((a) => {
        const def = artifactById(a)
        const mine = outputs.filter((o) => o.artifact === a)
        return (
          <Box key={a}>
            <ArtifactChip artifactId={a} onClick={onArtifact ? () => onArtifact(a) : undefined} />
            <Text size="xs" c="dimmed" mt={2}>
              {def?.purpose}
            </Text>
            {mine.length > 0 ? (
              <Stack gap={3} mt={4} pl={10}>
                {mine.map((m) => (
                  <ArtifactChip key={m.key} artifactId={a} instance={m} showSummary onClick={onArtifact ? () => onArtifact(a, m.key) : undefined} />
                ))}
              </Stack>
            ) : (
              <Text size="xs" c="dimmed" fs="italic" mt={2} pl={10}>
                No instance in iteration 3.
              </Text>
            )}
          </Box>
        )
      })}
    </Stack>
  )
}

/** Inputs | Outputs tabs for a node panel. */
export function NodeIOTabs({
  nodeId,
  onArtifact,
  defaultTab = 'inputs',
}: {
  nodeId: string
  onArtifact?: (artifactId: string, instanceKey?: string) => void
  defaultTab?: 'inputs' | 'outputs'
}) {
  const io = nodeIO(nodeId)
  return (
    <Tabs defaultValue={defaultTab} keepMounted={false}>
      <Tabs.List>
        <Tabs.Tab value="inputs">Inputs {io ? `(${io.inputs.length})` : ''}</Tabs.Tab>
        <Tabs.Tab value="outputs">Outputs {io ? `(${io.outputs.length})` : ''}</Tabs.Tab>
      </Tabs.List>
      <Tabs.Panel value="inputs" pt="xs">
        <InputsPanel nodeId={nodeId} onArtifact={onArtifact ? (a) => onArtifact(a) : undefined} />
      </Tabs.Panel>
      <Tabs.Panel value="outputs" pt="xs">
        <OutputsPanel nodeId={nodeId} onArtifact={onArtifact} />
      </Tabs.Panel>
    </Tabs>
  )
}

/** Small icon + input count for a graph node. */
export function InputCountBadge({ nodeId }: { nodeId: string }) {
  const io = nodeIO(nodeId)
  if (!io) return null
  const fromSteps = io.inputs.filter((i) => i.artifact).length
  return (
    <Tooltip label={`${io.inputs.length} inputs: ${fromSteps} from earlier steps, ${io.inputs.length - fromSteps} configuration/context`}>
      <Group gap={2} wrap="nowrap" style={{ flexShrink: 0 }}>
        <TbArrowBarToRight size={10} color="var(--mantine-color-cyan-4)" />
        <Text size="10px" c="dimmed" ff="monospace">
          {io.inputs.length}
        </Text>
      </Group>
    </Tooltip>
  )
}

/** Divider-labelled section helper re-used by stories. */
export function DetailDivider({ label }: { label: string }) {
  return <Divider label={<Text size="xs">{label}</Text>} labelPosition="left" />
}
