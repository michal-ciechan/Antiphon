import {
  Button,
  Group,
  Loader,
  Paper,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { getApiErrorMessage } from '../../api/client'
import {
  useClearModelAvailabilityHold,
  useModelAvailability,
  usePutModelAvailabilityHold,
} from '../../api/modelAvailability'

const KINDS = [
  { value: 'ClaudeCode', label: 'ClaudeCode' },
  { value: 'Grok', label: 'Grok' },
  { value: 'Codex', label: 'Codex' },
]

const ALIASES_BY_KIND: Record<string, { value: string; label: string }[]> = {
  ClaudeCode: [
    { value: '*', label: '* (kind-wide)' },
    { value: 'fable', label: 'fable' },
    { value: 'opus', label: 'opus' },
    { value: 'sonnet', label: 'sonnet' },
    { value: 'haiku', label: 'haiku' },
  ],
  Grok: [
    { value: '*', label: '* (kind-wide)' },
    { value: 'grok-4.6', label: 'grok-4.6' },
  ],
  Codex: [
    { value: '*', label: '* (kind-wide)' },
    { value: 'gpt-5.6-sol', label: 'gpt-5.6-sol' },
    { value: 'gpt-5.6-terra', label: 'gpt-5.6-terra' },
    { value: 'gpt-5.6-luna', label: 'gpt-5.6-luna' },
  ],
}

function untilLabel(value: string | null): string {
  return value ? value : 'until cleared'
}

function toUtcIso(local: string): string {
  const parsed = new Date(local)
  return parsed.toISOString()
}

/**
 * Thin operator table on the attention tab (CARD-0309 S3). Writes PUT/DELETE
 * /api/model-availability. Empty state is one line: all models available.
 */
export function ModelAvailabilityPanel() {
  const snapshot = useModelAvailability()
  const hold = usePutModelAvailabilityHold()
  const clear = useClearModelAvailabilityHold()
  const [kind, setKind] = useState<string | null>('ClaudeCode')
  const [alias, setAlias] = useState<string | null>('fable')
  const [untilLocal, setUntilLocal] = useState('')
  const [reason, setReason] = useState('')

  const aliases = ALIASES_BY_KIND[kind ?? 'ClaudeCode'] ?? ALIASES_BY_KIND.ClaudeCode
  const holds = snapshot.data?.holds ?? []
  const available = snapshot.data?.available ?? []

  if (snapshot.isLoading) {
    return (
      <Paper withBorder p="md" data-testid="model-availability-panel">
        <Group justify="center" py="sm">
          <Loader size="sm" />
        </Group>
      </Paper>
    )
  }

  if (snapshot.error) {
    return (
      <Paper withBorder p="md" data-testid="model-availability-panel">
        <Text size="sm" c="dimmed">
          Could not load model holds.
        </Text>
      </Paper>
    )
  }

  const submitHold = () => {
    if (!kind || !alias) return
    const body: { disabledUntil?: string; reason?: string } = {}
    if (untilLocal.trim()) body.disabledUntil = toUtcIso(untilLocal)
    if (reason.trim()) body.reason = reason.trim()
    hold.mutate(
      { kind, alias, body },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: `${kind} ${alias} held` })
          setReason('')
        },
        onError: (error) =>
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Could not hold that model'),
          }),
      },
    )
  }

  return (
    <Paper withBorder p="md" data-testid="model-availability-panel">
      <Stack gap="sm">
        <Title order={5}>Model holds</Title>
        {holds.length === 0 ? (
          <Text size="sm" c="dimmed">
            All models available.
          </Text>
        ) : (
          <Table striped highlightOnHover withRowBorders={false} layout="fixed">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Kind</Table.Th>
                <Table.Th>Alias</Table.Th>
                <Table.Th>Source</Table.Th>
                <Table.Th>Until</Table.Th>
                <Table.Th>Reason</Table.Th>
                <Table.Th w={88} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {holds.map((row) => (
                <Table.Tr key={row.id}>
                  <Table.Td>{row.kind}</Table.Td>
                  <Table.Td>{row.modelAlias}</Table.Td>
                  <Table.Td>{row.source}</Table.Td>
                  <Table.Td>{untilLabel(row.disabledUntil)}</Table.Td>
                  <Table.Td>
                    <Text size="sm" lineClamp={2}>
                      {row.reason}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Button
                      size="compact-xs"
                      variant="subtle"
                      onClick={() =>
                        clear.mutate(
                          { kind: row.kind, alias: row.modelAlias },
                          {
                            onSuccess: () =>
                              notifications.show({ color: 'green', message: 'Hold cleared' }),
                            onError: (error) =>
                              notifications.show({
                                color: 'red',
                                message: getApiErrorMessage(error, 'Could not clear the hold'),
                              }),
                          },
                        )
                      }
                    >
                      Clear
                    </Button>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
        <Text size="xs" c="dimmed">
          available: {available.length === 0 ? '(none)' : available.join(', ')}
        </Text>
        <Group gap="xs" align="flex-end" wrap="wrap">
          <Select
            label="Kind"
            data={KINDS}
            value={kind}
            onChange={(value) => {
              setKind(value)
              const next = ALIASES_BY_KIND[value ?? '']?.[1]?.value ?? '*'
              setAlias(next)
            }}
            w={140}
          />
          <Select
            label="Alias"
            data={aliases}
            value={alias}
            onChange={setAlias}
            w={160}
          />
          <TextInput
            label="Until (local)"
            type="datetime-local"
            value={untilLocal}
            onChange={(event) => setUntilLocal(event.currentTarget.value)}
            w={210}
          />
          <TextInput
            label="Reason"
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
            placeholder="optional"
            style={{ flex: 1, minWidth: 160 }}
          />
          <Button onClick={submitHold} loading={hold.isPending} disabled={!kind || !alias}>
            Hold
          </Button>
        </Group>
      </Stack>
    </Paper>
  )
}
