import { Anchor, Button, Group, Loader, Paper, Stack, Table, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { Link } from 'react-router'
import { getApiErrorMessage } from '../../api/client'
import { useClearModelAvailabilityHold, useModelAvailability } from '../../api/modelAvailability'
import { ModelAvailabilityHoldForm } from './ModelAvailabilityHoldForm'

function untilLabel(value: string | null): string {
  return value ? value : 'until cleared'
}

/**
 * Thin operator table on the attention tab (CARD-0309 S3). Writes PUT/DELETE
 * /api/model-availability. Empty state is one line: all models available.
 */
export function ModelAvailabilityPanel() {
  const snapshot = useModelAvailability()
  const clear = useClearModelAvailabilityHold()
  const holds = snapshot.data?.holds ?? []
  const available = snapshot.data?.available ?? []

  return (
    <Paper withBorder p="md" data-testid="model-availability-panel">
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Title order={5}>Model holds</Title>
          <Anchor component={Link} to="/settings?tab=routing" size="sm">
            Manage routing settings
          </Anchor>
        </Group>
        {snapshot.isLoading ? (
          <Group justify="center" py="sm">
            <Loader size="sm" />
          </Group>
        ) : snapshot.error ? (
          <Text size="sm" c="dimmed">
            Could not load model holds.
          </Text>
        ) : (
          <>
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
            <ModelAvailabilityHoldForm />
          </>
        )}
      </Stack>
    </Paper>
  )
}
