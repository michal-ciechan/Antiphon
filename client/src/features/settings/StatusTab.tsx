import {
  Stack,
  Group,
  Text,
  Badge,
  Paper,
  Title,
  Table,
  ActionIcon,
  Tooltip,
  Loader,
  Alert,
  ThemeIcon,
} from '@mantine/core'
import { TbRefresh, TbAlertCircle, TbCircleCheck, TbCircleX, TbDatabase } from 'react-icons/tb'
import { useGitHubStatus, useRefreshGitHubRepos } from '../../api/projects'
import { useQueryClient } from '@tanstack/react-query'

function formatDate(iso: string | null): string {
  if (!iso) return 'Never'
  const d = new Date(iso)
  return d.toLocaleString()
}

function age(iso: string | null): string {
  if (!iso) return '—'
  const diffMs = Date.now() - new Date(iso).getTime()
  const diffMin = Math.floor(diffMs / 60_000)
  if (diffMin < 1) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  const diffH = Math.floor(diffMin / 60)
  return `${diffH}h ${diffMin % 60}m ago`
}

export function StatusTab() {
  const { data: status, isLoading, error, refetch } = useGitHubStatus()
  const refreshRepos = useRefreshGitHubRepos()
  const queryClient = useQueryClient()

  const handleRefreshCache = async () => {
    await refreshRepos.mutateAsync()
    await queryClient.invalidateQueries({ queryKey: ['github', 'status'] })
  }

  if (isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading status">
        {error.message}
      </Alert>
    )
  }

  return (
    <Stack gap="lg">
      {/* GitHub Integration */}
      <Paper withBorder p="md">
        <Group mb="sm" justify="space-between">
          <Group gap="xs">
            <ThemeIcon variant="light" color="blue" size="sm">
              <TbDatabase size={14} />
            </ThemeIcon>
            <Title order={5}>GitHub Integration</Title>
          </Group>
          <Tooltip label="Refresh status">
            <ActionIcon variant="subtle" onClick={() => refetch()}>
              <TbRefresh />
            </ActionIcon>
          </Tooltip>
        </Group>

        <Table withRowBorders={false} verticalSpacing={4}>
          <Table.Tbody>
            <Table.Tr>
              <Table.Td w={160}>
                <Text size="sm" c="dimmed">
                  Enabled
                </Text>
              </Table.Td>
              <Table.Td>
                <Badge variant="light" color={status?.enabled ? 'green' : 'gray'} size="sm">
                  {status?.enabled ? 'Yes' : 'No'}
                </Badge>
              </Table.Td>
            </Table.Tr>

            <Table.Tr>
              <Table.Td>
                <Text size="sm" c="dimmed">
                  Base URL
                </Text>
              </Table.Td>
              <Table.Td>
                <Text size="sm" ff="monospace">
                  {status?.baseUrl ?? '—'}
                </Text>
              </Table.Td>
            </Table.Tr>

            <Table.Tr>
              <Table.Td>
                <Text size="sm" c="dimmed">
                  Connectivity
                </Text>
              </Table.Td>
              <Table.Td>
                <Group gap="xs">
                  {status?.connected ? (
                    <ThemeIcon color="green" variant="transparent" size="sm">
                      <TbCircleCheck />
                    </ThemeIcon>
                  ) : (
                    <ThemeIcon color="red" variant="transparent" size="sm">
                      <TbCircleX />
                    </ThemeIcon>
                  )}
                  <Text size="sm">
                    {status?.connected
                      ? `Connected as ${status.authenticatedAs ?? 'unknown'}`
                      : (status?.error ?? 'Not connected')}
                  </Text>
                </Group>
              </Table.Td>
            </Table.Tr>
          </Table.Tbody>
        </Table>
      </Paper>

      {/* Repo Cache */}
      <Paper withBorder p="md">
        <Group mb="sm" justify="space-between">
          <Group gap="xs">
            <ThemeIcon variant="light" color="violet" size="sm">
              <TbDatabase size={14} />
            </ThemeIcon>
            <Title order={5}>Repository Cache</Title>
          </Group>
          <Tooltip label="Refresh cache now">
            <ActionIcon
              variant="subtle"
              loading={refreshRepos.isPending}
              onClick={handleRefreshCache}
              disabled={!status?.enabled}
            >
              <TbRefresh />
            </ActionIcon>
          </Tooltip>
        </Group>

        <Table withRowBorders={false} verticalSpacing={4}>
          <Table.Tbody>
            <Table.Tr>
              <Table.Td w={160}>
                <Text size="sm" c="dimmed">
                  Repos cached
                </Text>
              </Table.Td>
              <Table.Td>
                <Text size="sm" fw={500}>
                  {status?.repoCache.count ?? 0}
                </Text>
              </Table.Td>
            </Table.Tr>

            <Table.Tr>
              <Table.Td>
                <Text size="sm" c="dimmed">
                  Last refreshed
                </Text>
              </Table.Td>
              <Table.Td>
                <Group gap="xs">
                  <Text size="sm">{formatDate(status?.repoCache.lastRefreshed ?? null)}</Text>
                  {status?.repoCache.lastRefreshed && (
                    <Text size="xs" c="dimmed">
                      ({age(status.repoCache.lastRefreshed)})
                    </Text>
                  )}
                </Group>
              </Table.Td>
            </Table.Tr>

            <Table.Tr>
              <Table.Td>
                <Text size="sm" c="dimmed">
                  Cache TTL
                </Text>
              </Table.Td>
              <Table.Td>
                <Text size="sm">{status?.repoCache.ttlMinutes ?? 15} minutes</Text>
              </Table.Td>
            </Table.Tr>

            <Table.Tr>
              <Table.Td>
                <Text size="sm" c="dimmed">
                  Cache state
                </Text>
              </Table.Td>
              <Table.Td>
                {status?.repoCache.lastRefreshed ? (
                  <Badge
                    variant="light"
                    color={status.repoCache.isStale ? 'orange' : 'green'}
                    size="sm"
                  >
                    {status.repoCache.isStale ? 'Stale' : 'Fresh'}
                  </Badge>
                ) : (
                  <Badge variant="light" color="gray" size="sm">
                    Empty
                  </Badge>
                )}
              </Table.Td>
            </Table.Tr>
          </Table.Tbody>
        </Table>
      </Paper>
    </Stack>
  )
}
