import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  Loader,
  Paper,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Text,
  ThemeIcon,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbAlertCircle, TbPlayerPause, TbPlayerPlay, TbRefresh, TbRotateClockwise, TbServer2 } from 'react-icons/tb'
import {
  useOrchestratorState,
  usePauseOrchestrator,
  useResumeOrchestrator,
  useRunOrchestratorTick,
} from '../../api/orchestrator'

function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  if (hours > 0) return `${hours}h ${minutes % 60}m`
  if (minutes > 0) return `${minutes}m ${seconds % 60}s`
  return `${seconds}s`
}

function formatDate(iso: string | null): string {
  if (!iso) return '-'
  return new Date(iso).toLocaleString()
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 2,
  }).format(value)
}

function SummaryMetric({ label, value }: { label: string; value: string | number }) {
  return (
    <Paper withBorder p="md">
      <Text size="xs" c="dimmed" tt="uppercase" fw={700}>{label}</Text>
      <Text size="xl" fw={700}>{value}</Text>
    </Paper>
  )
}

export function OrchestratorPanel() {
  const state = useOrchestratorState()
  const pause = usePauseOrchestrator()
  const resume = useResumeOrchestrator()
  const tick = useRunOrchestratorTick()

  const handlePauseResume = () => {
    const mutation = state.data?.paused ? resume : pause
    mutation.mutate(undefined, {
      onError: (error) => notifications.show({
        color: 'red',
        message: error instanceof Error ? error.message : 'Orchestrator command failed',
      }),
    })
  }

  const handleTick = () => {
    tick.mutate(undefined, {
      onSuccess: (result) => notifications.show({
        color: result.failures > 0 ? 'orange' : 'green',
        message: `Tick dispatched ${result.dispatched} card${result.dispatched === 1 ? '' : 's'}`,
      }),
      onError: (error) => notifications.show({
        color: 'red',
        message: error instanceof Error ? error.message : 'Tick failed',
      }),
    })
  }

  if (state.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (state.error || !state.data) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading orchestrator state">
        {state.error instanceof Error ? state.error.message : 'No orchestrator state returned.'}
      </Alert>
    )
  }

  const data = state.data
  const totalTokens = data.totals.tokensIn + data.totals.tokensOut

  return (
    <Stack gap="lg">
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <ThemeIcon variant="light" color="blue">
            <TbServer2 size={18} />
          </ThemeIcon>
          <Title order={2}>Orchestrator</Title>
          <Badge color={data.enabled ? 'green' : 'gray'} variant="light">
            {data.enabled ? 'Enabled' : 'Disabled'}
          </Badge>
          <Badge color={data.paused ? 'orange' : 'green'} variant="light">
            {data.paused ? 'Paused' : 'Running'}
          </Badge>
        </Group>
        <Group gap="xs">
          <Tooltip label="Refresh state">
            <ActionIcon variant="subtle" onClick={() => state.refetch()} loading={state.isFetching}>
              <TbRefresh />
            </ActionIcon>
          </Tooltip>
          <Button
            leftSection={data.paused ? <TbPlayerPlay size={16} /> : <TbPlayerPause size={16} />}
            variant="light"
            onClick={handlePauseResume}
            loading={pause.isPending || resume.isPending}
          >
            {data.paused ? 'Resume' : 'Pause'}
          </Button>
          <Button
            leftSection={<TbRotateClockwise size={16} />}
            onClick={handleTick}
            loading={tick.isPending}
            disabled={!data.enabled}
          >
            Tick
          </Button>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 2, md: 6 }} spacing="sm">
        <SummaryMetric label="Running" value={data.runningSessions} />
        <SummaryMetric label="Retry Queue" value={data.retryQueueLength} />
        <SummaryMetric label="Tokens" value={totalTokens.toLocaleString()} />
        <SummaryMetric label="Cost" value={formatCurrency(data.totals.costUsd)} />
        <SummaryMetric label="Active Runtime" value={formatDuration(data.totals.activeRuntimeSeconds)} />
        <SummaryMetric label="Dispatch Limit" value={`${data.limits.maxDispatchesPerTick}/tick`} />
      </SimpleGrid>

      <Paper withBorder p="md">
        <Group justify="space-between" mb="sm">
          <Title order={4}>Running Sessions</Title>
          <Text size="sm" c="dimmed">Poll {data.limits.pollIntervalSeconds}s</Text>
        </Group>
        <ScrollArea>
          <Table striped highlightOnHover miw={820}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Card</Table.Th>
                <Table.Th>Board</Table.Th>
                <Table.Th>Agent</Table.Th>
                <Table.Th>Phase</Table.Th>
                <Table.Th>Turns</Table.Th>
                <Table.Th>Runtime</Table.Th>
                <Table.Th>Tokens</Table.Th>
                <Table.Th>Live</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.running.length === 0 ? (
                <Table.Tr>
                  <Table.Td colSpan={8}>
                    <Text size="sm" c="dimmed">No running sessions</Text>
                  </Table.Td>
                </Table.Tr>
              ) : data.running.map((session) => (
                <Table.Tr key={session.sessionId}>
                  <Table.Td>
                    <Text size="sm" fw={600}>{session.cardIdentifier}</Text>
                    <Text size="xs" c="dimmed">{session.cardTitle}</Text>
                  </Table.Td>
                  <Table.Td>{session.boardName}</Table.Td>
                  <Table.Td>
                    <Text size="sm">{session.definitionName}</Text>
                    <Text size="xs" c="dimmed">{session.agentKind}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="light">{session.phase ?? session.status}</Badge>
                  </Table.Td>
                  <Table.Td>{session.turnCount}</Table.Td>
                  <Table.Td>{formatDuration(session.runtimeSeconds)}</Table.Td>
                  <Table.Td>{(session.tokensIn + session.tokensOut).toLocaleString()}</Table.Td>
                  <Table.Td>
                    <Badge size="sm" color={session.live ? 'green' : 'gray'} variant="light">
                      {session.live ? `seq ${session.lastSequence}` : 'No'}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Paper withBorder p="md">
        <Title order={4} mb="sm">Retry Queue</Title>
        <ScrollArea>
          <Table striped highlightOnHover miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Card</Table.Th>
                <Table.Th>Board</Table.Th>
                <Table.Th>Attempts</Table.Th>
                <Table.Th>Next Retry</Table.Th>
                <Table.Th>Last Error</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.retryQueue.length === 0 ? (
                <Table.Tr>
                  <Table.Td colSpan={5}>
                    <Text size="sm" c="dimmed">No due retries</Text>
                  </Table.Td>
                </Table.Tr>
              ) : data.retryQueue.map((retry) => (
                <Table.Tr key={retry.cardId}>
                  <Table.Td>
                    <Text size="sm" fw={600}>{retry.cardIdentifier}</Text>
                    <Text size="xs" c="dimmed">{retry.cardTitle}</Text>
                  </Table.Td>
                  <Table.Td>{retry.boardName}</Table.Td>
                  <Table.Td>{retry.attemptCount} / {retry.maxAttempts}</Table.Td>
                  <Table.Td>{formatDate(retry.nextRetryAt)}</Table.Td>
                  <Table.Td>
                    <Text size="sm" lineClamp={2}>{retry.lastError ?? '-'}</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>
    </Stack>
  )
}
