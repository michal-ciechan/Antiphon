import { Anchor, Badge, Group, Loader, Paper, Stack, Table, Text, Title } from '@mantine/core'
import { Link } from 'react-router'
import { useComplexityChains, type ComplexityChainDto } from '../../api/complexityChains'

/**
 * Read-only view of Hard/Medium/Easy chains (CARD-0090, CARD-0332). Writes are
 * scripts/complexity-chain.ps1. CARD-0333 builds the real grid; this panel only
 * labels a cell distinctly from the any-role row so two Hard rows are not twins.
 */

function chainRowLabel(chain: ComplexityChainDto): string {
  return chain.role ? `${chain.role}/${chain.complexity}` : `${chain.complexity} (any role)`
}

function chainRowKey(chain: ComplexityChainDto): string {
  return `${chain.role}-${chain.complexity}`
}

export function ComplexityChainPanel() {
  const snapshot = useComplexityChains()

  const chains = snapshot.data?.chains ?? []

  return (
    <Paper withBorder p="md" data-testid="complexity-chain-panel">
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Title order={5}>Complexity chains</Title>
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
            Could not load complexity chains.
          </Text>
        ) : chains.every((c) => c.candidates.length === 0) ? (
          <Text size="sm" c="dimmed">
            No chains set. Defaults stay empty until a human writes them with complexity-chain.ps1
            set.
          </Text>
        ) : (
          chains.map((chain) => (
            <Stack key={chainRowKey(chain)} gap={4}>
              <Group gap="xs">
                <Text size="sm" fw={600}>
                  {chainRowLabel(chain)}
                </Text>
                <Badge size="xs" variant="light">
                  {chain.source}/{chain.provenance ?? 'none'}
                </Badge>
              </Group>
              {chain.candidates.length === 0 ? (
                <Text size="xs" c="dimmed">
                  (empty)
                </Text>
              ) : (
                <Table striped withRowBorders={false} layout="fixed">
                  <Table.Tbody>
                    {chain.candidates.map((c, i) => (
                      <Table.Tr key={`${c.agentKind}-${c.modelLevel}-${i}`}>
                        <Table.Td>
                          {c.agentKind}/{c.modelLevel} ({c.alias})
                        </Table.Td>
                        <Table.Td>
                          <Text size="sm" c={c.availableNow ? 'teal' : 'red'}>
                            {c.availableNow ? 'available' : c.unavailableReason ?? 'unavailable'}
                          </Text>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Stack>
          ))
        )}
      </Stack>
    </Paper>
  )
}
