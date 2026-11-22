import { Badge, Box, Code, Group, Paper, ScrollArea, Stack, Table, Text } from '@mantine/core'
import type { NodeExecutionDto, WorkflowNodeDto, WorkflowValidationDto } from '../../../../api/reviewEngine'
import { CostText } from '../../components/CostText'
import { SectionLabel } from '../../components/SectionLabel'
import { ModelBadge } from '../../components/ReviewBadges'
import { NODE_KIND_META, NODE_STATUS_META } from '../../reviewMeta'
import { fmtDuration, fmtTokens } from '../../reviewFormat'

function paramText(v: unknown): string {
  if (typeof v === 'string') return v
  return JSON.stringify(v)
}

function ExecRow({ e, current, now }: { e: NodeExecutionDto; current: boolean; now: number }) {
  const meta = NODE_STATUS_META[e.status]
  return (
    <Table.Tr bg={current ? 'dark.6' : undefined}>
      <Table.Td>
        <Text size="xs" ff="monospace">
          it{e.iteration}
        </Text>
      </Table.Td>
      <Table.Td>
        <Text size="xs" fw={600} style={{ color: meta.hex }}>
          {meta.label}
        </Text>
      </Table.Td>
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

/** What a step is, its parameters, and every execution of it in this run. */
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
  return (
    <Stack gap="sm">
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
      </Box>
      {params.length > 0 && (
        <Box>
          <SectionLabel>Parameters</SectionLabel>
          <Table fz="xs" verticalSpacing={2} withRowBorders={false}>
            <Table.Tbody>
              {params.map(([k, v]) => (
                <Table.Tr key={k}>
                  <Table.Td w={90} c="dimmed">
                    {k}
                  </Table.Td>
                  <Table.Td>
                    <Code fz={11}>{paramText(v)}</Code>
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
          <ScrollArea.Autosize mah={420}>
            <Table fz="xs" verticalSpacing={3}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>It</Table.Th>
                  <Table.Th>Status</Table.Th>
                  <Table.Th>Model</Table.Th>
                  <Table.Th ta="right">Tokens</Table.Th>
                  <Table.Th ta="right">Cost</Table.Th>
                  <Table.Th ta="right">Time</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {execs.map((e) => (
                  <ExecRow key={e.id} e={e} current={e.iteration === iteration} now={now} />
                ))}
              </Table.Tbody>
            </Table>
            <Stack gap={4} mt={6}>
              {execs
                .filter((e) => e.summary)
                .map((e) => (
                  <Text key={e.id} size="xs" c={e.iteration === iteration ? undefined : 'dimmed'}>
                    <Text span ff="monospace" size="xs" c="dimmed">
                      it{e.iteration}
                    </Text>{' '}
                    {e.summary}
                  </Text>
                ))}
            </Stack>
          </ScrollArea.Autosize>
        )}
      </Box>
    </Stack>
  )
}

/** Result of POST /api/review-workflows/validate for the graph on screen. */
export function ValidationPanel({ result }: { result: WorkflowValidationDto }) {
  return (
    <Paper withBorder p="xs" radius="md">
      <SectionLabel right={<Badge size="xs" color={result.valid ? 'success' : 'danger'}>{result.valid ? 'valid' : 'invalid'}</Badge>}>Validation</SectionLabel>
      {result.diagnostics.length === 0 && (
        <Text size="xs" c="dimmed">
          No diagnostics: every cycle has a gate, a wait, or an iteration limit.
        </Text>
      )}
      {result.diagnostics.map((d, i) => (
        <Text key={i} size="xs" c={d.code.startsWith('warn.') ? 'warning.4' : 'danger.4'}>
          <Text span ff="monospace" size="xs">
            {d.code}
          </Text>{' '}
          {d.message}
        </Text>
      ))}
    </Paper>
  )
}
