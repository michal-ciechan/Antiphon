import { Badge, Box, Code, Group, Paper, Stack, Table, Text } from '@mantine/core'
import { TbAlertTriangle } from 'react-icons/tb'
import type { DiagnosticSeverity, NodeExecutionDto, WorkflowDiagnosticDto, WorkflowNodeDto, WorkflowValidationDto } from '../../../../api/reviewEngine'
import { CostText } from '../../components/CostText'
import { SectionLabel } from '../../components/SectionLabel'
import { ModelBadge } from '../../components/ReviewBadges'
import { NODE_KIND_META, NODE_STATUS_META, tolerant } from '../../reviewMeta'
import { fmtDuration, fmtTokens } from '../../reviewFormat'
import { splitExecutionOutput } from './executionOutput'
import classes from './NodeSidePanel.module.css'

const WRAP_OUTPUT = { whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' } as const

function paramText(v: unknown): string {
  if (typeof v === 'string') return v
  return JSON.stringify(v)
}

function ExecRow({ e, current, now, showItem, showOutcome }: { e: NodeExecutionDto; current: boolean; now: number; showItem: boolean; showOutcome: boolean }) {
  const meta = NODE_STATUS_META[e.status]
  return (
    <Table.Tr bg={current ? 'var(--mantine-color-default-hover)' : undefined}>
      <Table.Td>
        <Text size="xs" ff="monospace">
          it{e.iteration}
        </Text>
      </Table.Td>
      {showItem && (
        <Table.Td>
          <Text size="xs" fw={600}>
            {e.itemKey ?? '—'}
          </Text>
        </Table.Td>
      )}
      <Table.Td>
        <Text size="xs" fw={600} style={{ color: meta.hex }}>
          {meta.label}
        </Text>
      </Table.Td>
      {showOutcome && (
        <Table.Td>
          <Text size="xs" ff="monospace" c={e.outcome ? 'blue.3' : 'dimmed'}>
            {e.outcome ? `→ ${e.outcome}` : '—'}
          </Text>
        </Table.Td>
      )}
      <Table.Td>{e.model ? <ModelBadge model={e.model} /> : <Text size="xs" c="dimmed">—</Text>}</Table.Td>
      <Table.Td ta="right">
        <Text size="xs" ff="monospace" c="dimmed">
          {e.inputTokens ? `${fmtTokens(e.inputTokens)}/${fmtTokens(e.outputTokens)}` : '—'}
        </Text>
      </Table.Td>
      <Table.Td ta="right">
        <CostText usd={e.costUsd} split={e.transport === 'Http' ? { actual: e.costUsd, estimate: 0 } : { actual: 0, estimate: e.costUsd }} />
      </Table.Td>
      <Table.Td ta="right">
        <Text size="xs" c="dimmed">
          {fmtDuration(e.startedAt, e.completedAt, now)}
        </Text>
      </Table.Td>
    </Table.Tr>
  )
}

function ExecutionOutput({ execution, current }: { execution: NodeExecutionDto; current: boolean }) {
  const { body, warnings } = splitExecutionOutput(execution.summary)
  const hasBody = body.trim().length > 0
  if (!hasBody && warnings.length === 0) return null

  return (
    <Paper withBorder p="xs" style={{
      background: 'var(--mantine-color-default)',
      color: 'var(--mantine-color-text)',
      overflowWrap: 'anywhere',
    }}>
      <Text size="xs" fw={600} c={current ? undefined : 'dimmed'} mb={4}>
        it{execution.iteration}{execution.itemKey ? ` · ${execution.itemKey}` : ''}
      </Text>
      {hasBody && (
        <Text component="pre" m={0} size="xs" ff="monospace" style={WRAP_OUTPUT}>
          {body}
        </Text>
      )}
      {warnings.length > 0 && (
        <Box mt={hasBody ? 'xs' : 0} className={classes.warnings}>
          <Group gap={4} mb={2}>
            <TbAlertTriangle size={14} aria-hidden="true" />
            <Text size="xs" fw={700}>Warnings</Text>
          </Group>
          <Text component="ul" aria-label="Warnings" my={0} pl="md" size="xs" style={WRAP_OUTPUT}>
            {warnings.map((warning, index) => <li key={index}>{warning}</li>)}
          </Text>
        </Box>
      )}
    </Paper>
  )
}

/**
 * What a step is, its parameters, and every execution of it in this run: fan-out executions are
 * grouped by `itemKey` within each iteration, and each shows the outgoing `outcome` it took.
 */
export function NodeSidePanel({
  def,
  execs,
  iteration,
  now,
}: {
  def: WorkflowNodeDto
  execs: NodeExecutionDto[]
  iteration: number
  now: number
}) {
  const kind = NODE_KIND_META[def.kind]
  const params = Object.entries(def.params)
  // Only a step that does work has a failure policy; gates, waits and fan-outs never continue past one.
  const failurePolicy = def.kind === 'Gate' || def.kind === 'Wait' || def.kind === 'FanOut'
    ? null
    : def.params.onFailure === 'continue'
      ? `continues${typeof def.params.fallback === 'string' ? ` along ${def.params.fallback}` : ''}, marked failed, continued`
      : 'stops the run'
  const showItem = execs.some((e) => e.itemKey)
  const showOutcome = execs.some((e) => e.outcome)
  const rows = [...execs].sort((a, b) => a.iteration - b.iteration || (a.itemKey ?? '').localeCompare(b.itemKey ?? ''))
  return (
    <Stack gap="sm" miw={0} style={{ overflowWrap: 'anywhere' }}>
      <Box>
        <Group gap={6}>
          <Badge size="sm" color={kind.color} variant="light">
            {kind.label}
          </Badge>
          <Text fw={700}>{def.id}</Text>
          {def.run && (
            <Text size="xs" ff="monospace" c="dimmed">
              run: {def.run}
            </Text>
          )}
        </Group>
        {def.description ? (
          <Text size="sm" mt={4}>
            {def.description}
          </Text>
        ) : (
          <Text size="xs" c="warning.4" mt={4}>
            No description.
          </Text>
        )}
        {failurePolicy && (
          <Text size="xs" c="dimmed" mt={4} data-testid="review-node-on-failure">
            On failure: {failurePolicy}. Review:Policy:OnFailure:{def.id} overrides this per instance.
          </Text>
        )}
      </Box>
      {params.length > 0 && (
        <Box>
          <SectionLabel>Parameters</SectionLabel>
          <Table fz="xs" verticalSpacing={2} withRowBorders={false} style={{ tableLayout: 'fixed' }}>
            <Table.Tbody>
              {params.map(([k, v]) => (
                <Table.Tr key={k}>
                  <Table.Td w={90} c="dimmed">
                    {k}
                  </Table.Td>
                  <Table.Td>
                    <Code fz={11} style={WRAP_OUTPUT}>{paramText(v)}</Code>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Box>
      )}
      <Box>
        <SectionLabel right={<Text size="xs" c="dimmed">{execs.length}</Text>}>Executions</SectionLabel>
        {execs.length === 0 ? (
          <Text size="xs" c="dimmed">
            Not reached in this run.
          </Text>
        ) : (
          <>
            <Box
              role="region"
              aria-label="Execution history"
              tabIndex={0}
              className={classes.executionHistory}
              style={{
                '--execution-history-focus-color': 'var(--mantine-color-text)',
                overflowX: 'auto',
                maxWidth: '100%',
                maxHeight: 420,
              }}
            >
              <Table fz="xs" verticalSpacing={3} w="max-content" miw="100%" style={{ whiteSpace: 'nowrap' }}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>It</Table.Th>
                    {showItem && <Table.Th>Item</Table.Th>}
                    <Table.Th>Status</Table.Th>
                    {showOutcome && <Table.Th>Took</Table.Th>}
                    <Table.Th>Model</Table.Th>
                    <Table.Th ta="right">Tokens</Table.Th>
                    <Table.Th ta="right">Cost</Table.Th>
                    <Table.Th ta="right">Time</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {rows.map((e) => (
                    <ExecRow key={e.id} e={e} current={e.iteration === iteration} now={now} showItem={showItem} showOutcome={showOutcome} />
                  ))}
                </Table.Tbody>
              </Table>
            </Box>
            <Stack gap={6} mt={6}>
              {rows.filter((e) => e.summary).map((e) => (
                <ExecutionOutput key={e.id} execution={e} current={e.iteration === iteration} />
              ))}
            </Stack>
          </>
        )}
      </Box>
    </Stack>
  )
}

const SEVERITY_META: Record<DiagnosticSeverity, { label: string; plural: string; color: string }> = tolerant(
  {
    Error: { label: 'error', plural: 'errors', color: 'danger' },
    Warning: { label: 'warning', plural: 'warnings', color: 'warning' },
  },
  (value) => ({ label: value, plural: value, color: 'gray' }),
)

function DiagnosticRow({ d }: { d: WorkflowDiagnosticDto }) {
  const where = [...d.nodeIds, ...(d.edge ? [`${d.edge.from} → ${d.edge.to}`] : [])]
  return (
    <Box py={2}>
      <Text size="xs" c={`${SEVERITY_META[d.severity].color}.4`}>
        <Text span ff="monospace" size="xs">
          {d.code}
        </Text>{' '}
        {d.message}
      </Text>
      {where.length > 0 && (
        <Text size="10px" ff="monospace" c="dimmed">
          {where.join(' · ')}
        </Text>
      )}
    </Box>
  )
}

/**
 * Result of POST /api/review-workflows/validate for the graph on screen, errors first then
 * warnings (`severity`). Only errors make the graph invalid.
 */
export function ValidationPanel({ result }: { result: WorkflowValidationDto }) {
  const groups = (['Error', 'Warning'] as const).map((severity) => ({ severity, items: result.diagnostics.filter((d) => d.severity === severity) }))
  return (
    <Paper withBorder p="xs" radius="md">
      <SectionLabel right={<Badge size="xs" color={result.valid ? 'success' : 'danger'}>{result.valid ? 'valid' : 'invalid'}</Badge>}>Validation</SectionLabel>
      {result.diagnostics.length === 0 && (
        <Text size="xs" c="dimmed">
          No diagnostics: every cycle has a gate, a wait, or an iteration limit.
        </Text>
      )}
      {groups
        .filter((g) => g.items.length > 0)
        .map((g) => (
          <Box key={g.severity} mt={4}>
            <Badge size="xs" variant="light" color={SEVERITY_META[g.severity].color}>
              {g.items.length} {g.items.length === 1 ? SEVERITY_META[g.severity].label : SEVERITY_META[g.severity].plural}
            </Badge>
            {g.items.map((d, i) => (
              <DiagnosticRow key={`${d.code}:${i}`} d={d} />
            ))}
          </Box>
        ))}
    </Paper>
  )
}
