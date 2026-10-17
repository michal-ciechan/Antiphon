import { useState } from 'react'
import {
  Alert,
  Badge,
  Group,
  Loader,
  NumberInput,
  Paper,
  Stack,
  Switch,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbAlertCircle } from 'react-icons/tb'
import { getApiErrorMessage } from '../../api/client'
import {
  KEEP_SIX_OPERATOR_REASON,
  type KeepSixOrchestratorDto,
  useKeepSix,
  usePutKeepSix,
} from '../../api/keepSix'

const BADGE_COLOR: Record<string, string> = {
  off: 'gray',
  healthy: 'green',
  'under-target': 'orange',
}

const TARGET_MIN = 1
const TARGET_MAX = 24

function formatInstant(iso: string | null): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toISOString()
}

function countText(row: KeepSixOrchestratorDto): string {
  if (row.countedOpen == null || row.effectiveTarget == null) return '—'
  return `${row.countedOpen} of ${row.effectiveTarget}`
}

function holdText(row: KeepSixOrchestratorDto, now: number): string {
  if (!row.holdReason) return '—'
  if (!row.holdUntil) return row.holdReason
  const until = Date.parse(row.holdUntil)
  if (Number.isNaN(until) || until <= now) return `${row.holdReason} (expired)`
  return `${row.holdReason} until ${formatInstant(row.holdUntil)}`
}

function TargetInput({
  row,
  disabled,
  onCommit,
}: {
  row: KeepSixOrchestratorDto
  disabled: boolean
  onCommit: (target: number) => void
}) {
  const [value, setValue] = useState<number | string>(row.target)

  return (
    <NumberInput
      aria-label={`Target for ${row.name}`}
      value={value}
      onChange={setValue}
      min={TARGET_MIN}
      max={TARGET_MAX}
      clampBehavior="strict"
      allowDecimal={false}
      disabled={disabled}
      w={88}
      onBlur={() => {
        const next = typeof value === 'number' ? value : Number(value)
        if (!Number.isInteger(next) || next < TARGET_MIN || next > TARGET_MAX || next === row.target) return
        onCommit(next)
      }}
    />
  )
}

/**
 * Per-orchestrator reminder switch. Pause and Tick on the panel below still dispatch cards.
 */
export function KeepSixPanel() {
  const keepSix = useKeepSix()
  const put = usePutKeepSix()
  const now = keepSix.dataUpdatedAt

  const commit = (row: KeepSixOrchestratorDto, patch: { enabled?: boolean; target?: number }) => {
    put.mutate({
      agentId: row.agentId,
      body: {
        expectedRevision: row.revision,
        enabled: patch.enabled ?? row.enabled,
        target: patch.target ?? row.target,
        reason: KEEP_SIX_OPERATOR_REASON,
        provenance: 'Human',
      },
    }, {
      onError: (error) => notifications.show({
        color: 'red',
        message: getApiErrorMessage(error, 'Could not update auto-processing'),
      }),
    })
  }

  if (keepSix.isLoading) {
    return (
      <Group justify="center" py="md">
        <Loader size="sm" />
      </Group>
    )
  }

  if (keepSix.error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading auto-processing">
        {keepSix.error instanceof Error ? keepSix.error.message : 'No auto-processing rows returned.'}
      </Alert>
    )
  }

  const rows = keepSix.data ?? []

  return (
    <Paper withBorder p="md" data-testid="keep-six-panel">
      <Stack gap="sm">
        <div>
          <Title order={3}>Auto-processing</Title>
          <Text size="xs" c="dimmed">
            Reminds a standing orchestrator when in-flight work is under its target. The server does not dispatch.
          </Text>
        </div>
        {rows.length === 0 ? (
          <Text size="sm" c="dimmed">No standing orchestrators.</Text>
        ) : (
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Orchestrator</Table.Th>
                <Table.Th>Auto-processing</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>In flight</Table.Th>
                <Table.Th>Dispatchable</Table.Th>
                <Table.Th>Last reminder</Table.Th>
                <Table.Th>Last dispatch</Table.Th>
                <Table.Th>Hold</Table.Th>
                <Table.Th>Target</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => (
                <Table.Tr key={row.agentId} data-testid={`keep-six-row-${row.agentId}`}>
                  <Table.Td>
                    <Text size="sm" fw={600}>{row.name}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Switch
                      label="Auto-processing"
                      aria-label={`Auto-processing ${row.name}`}
                      checked={row.enabled}
                      disabled={put.isPending}
                      onChange={(event) => commit(row, { enabled: event.currentTarget.checked })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <Badge
                      data-testid="keep-six-badge"
                      color={BADGE_COLOR[row.badge ?? 'off'] ?? 'gray'}
                      variant="light"
                    >
                      {row.badge ?? 'off'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" data-testid="keep-six-count">{countText(row)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" data-testid="keep-six-dispatchable">
                      {row.dispatchableBacklog == null ? '—' : row.dispatchableBacklog}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" data-testid="keep-six-reminder">{formatInstant(row.lastReminderEnqueuedAt)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" data-testid="keep-six-dispatch">{formatInstant(row.lastDispatchAt)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" data-testid="keep-six-hold">{holdText(row, now)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <TargetInput
                      key={`${row.agentId}:${row.revision}:${row.target}`}
                      row={row}
                      disabled={put.isPending}
                      onCommit={(target) => commit(row, { target })}
                    />
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>
    </Paper>
  )
}
