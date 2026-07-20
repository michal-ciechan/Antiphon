import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Drawer,
  Group,
  Loader,
  Paper,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useState } from 'react'
import {
  TbAlertCircle,
  TbHistory,
  TbLayoutKanban,
  TbPlayerPlay,
  TbPlayerStop,
  TbPlus,
  TbSettings,
  TbShieldCheck,
  TbShieldPause,
  TbShieldX,
  TbTerminal2,
} from 'react-icons/tb'
import { Link } from 'react-router'
import type { AgentIncidentDto, AgentSummaryDto } from '../../api/agents'
import { useAgent, useAgentIncidents, useAgentList, useStartAgent, useStopAgent } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { AgentAddWorkModal } from './AgentAddWorkModal'
import { AgentCliModal } from './AgentCliModal'
import { AgentCreateModal } from './AgentCreateModal'
import { AgentSettingsModal } from './AgentSettingsModal'

export function AgentsPage() {
  const agents = useAgentList()
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null)
  const selected = useAgent(selectedAgentId)
  const [createOpen, setCreateOpen] = useState(false)
  const [addWorkOpen, setAddWorkOpen] = useState(false)
  const [settingsAgent, setSettingsAgent] = useState<AgentSummaryDto | null>(null)
  const [terminalAgent, setTerminalAgent] = useState<AgentSummaryDto | null>(null)
  const [incidentsOpen, setIncidentsOpen] = useState(false)
  const startAgent = useStartAgent(selectedAgentId ?? '')
  const stopAgent = useStopAgent(selectedAgentId ?? '')
  const incidents = useAgentIncidents(selectedAgentId, incidentsOpen)

  useEffect(() => {
    if (!selectedAgentId && agents.data?.[0]) {
      setSelectedAgentId(agents.data[0].id)
    }
  }, [agents.data, selectedAgentId])

  const handleAgentDeleted = (agentId: string) => {
    if (selectedAgentId === agentId) {
      setSelectedAgentId(null)
    }
  }

  return (
    <Box p="md">
      <Stack gap="md">
        <Group justify="space-between" align="flex-end">
          <Title order={2}>Agents</Title>
          <Button leftSection={<TbPlus size={16} />} onClick={() => setCreateOpen(true)}>
            New Agent
          </Button>
        </Group>

        {agents.isLoading && (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        )}

        {agents.error && (
          <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
            {agents.error instanceof Error ? agents.error.message : 'Agents failed to load'}
          </Alert>
        )}

        {!agents.isLoading && agents.data?.length === 0 && (
          <Paper withBorder p="xl">
            <Text ta="center" c="dimmed">
              No agents yet.
            </Text>
          </Paper>
        )}

        <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}>
          {(agents.data ?? []).map((agent) => (
            <Box key={agent.id} pos="relative">
              <UnstyledButton
                aria-label={`Agent ${agent.name}`}
                aria-pressed={selectedAgentId === agent.id}
                onClick={() => setSelectedAgentId(agent.id)}
                style={{
                  display: 'block',
                  width: '100%',
                }}
              >
                <Paper
                  withBorder
                  p="md"
                  style={{
                    outline: selectedAgentId === agent.id ? '1px solid var(--mantine-color-active-5)' : undefined,
                  }}
                >
                  <Stack gap="xs">
                    <Group justify="space-between" align="flex-start" wrap="nowrap" pr={56}>
                      <Text fw={700} lineClamp={1} style={{ flex: 1, minWidth: 0 }}>
                        {agent.name}
                      </Text>
                      <Group gap={4} style={{ flexShrink: 0 }}>
                        <SupervisionBadge agent={agent} compact />
                        <Badge variant="light">{agent.status}</Badge>
                      </Group>
                    </Group>
                    <Text size="xs" c="dimmed" lineClamp={1}>
                      {agent.workingDirectory}
                    </Text>
                    <Group justify="space-between">
                      <Text size="sm">{agent.queueLength} queued</Text>
                      <Badge color="gray" variant="outline">
                        {agent.assignmentPolicy}
                      </Badge>
                    </Group>
                  </Stack>
                </Paper>
              </UnstyledButton>
              <Tooltip
                label={
                  agent.liveSession?.status === 'Running'
                    ? 'Open running terminal'
                    : agent.liveSession
                      ? `Terminal ${agent.liveSession.status.toLowerCase()}…`
                      : 'No terminal — start agent'
                }
                openDelay={400}
                withArrow
              >
                <ActionIcon
                  variant="subtle"
                  color={
                    agent.liveSession?.status === 'Running' ? 'green' : agent.liveSession ? 'yellow' : 'gray'
                  }
                  aria-label={`Terminal ${agent.name}`}
                  onClick={() => setTerminalAgent(agent)}
                  pos="absolute"
                  top={8}
                  right={36}
                >
                  <TbTerminal2 size={18} />
                </ActionIcon>
              </Tooltip>
              <ActionIcon
                variant="subtle"
                color="gray"
                aria-label={`Settings ${agent.name}`}
                onClick={() => setSettingsAgent(agent)}
                pos="absolute"
                top={8}
                right={8}
              >
                <TbSettings size={18} />
              </ActionIcon>
            </Box>
          ))}
        </SimpleGrid>

        {selected.isLoading && selectedAgentId && (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        )}

        {selected.error && (
          <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
            {selected.error instanceof Error ? selected.error.message : 'Agent detail failed to load'}
          </Alert>
        )}

        {selected.data && (
          <Paper withBorder p="md">
            <Group justify="space-between" mb="sm" align="flex-start">
              <Stack gap={2}>
                <Group gap="xs">
                  <Title order={3}>{selected.data.name}</Title>
                  <Badge variant="light">{selected.data.status}</Badge>
                  <SupervisionBadge agent={selected.data} />
                </Group>
                <Text size="sm" c="dimmed">
                  {selected.data.workingDirectory}
                </Text>
                {selected.data.boardId && (
                  <Anchor component={Link} to={`/boards/${selected.data.boardId}`} size="sm">
                    <Group gap={4} align="center">
                      <TbLayoutKanban size={14} />
                      {selected.data.boardName ?? 'Board'}
                    </Group>
                  </Anchor>
                )}
                {selected.data.details && <Text size="sm">{selected.data.details}</Text>}
              </Stack>
              <Group gap="sm" align="center">
                {selected.data.status === 'Working' ? (
                  <Button
                    variant="light"
                    color="red"
                    leftSection={<TbPlayerStop size={16} />}
                    loading={stopAgent.isPending}
                    onClick={() =>
                      stopAgent.mutate(undefined, {
                        onError: (error) =>
                          notifications.show({
                            color: 'red',
                            message: getApiErrorMessage(error, 'Could not stop the agent'),
                          }),
                      })
                    }
                  >
                    Stop
                  </Button>
                ) : (
                  <Tooltip
                    label="Boots the agent on its next queued card, or an interactive session if nothing is queued"
                    openDelay={400}
                  >
                    <Button
                      variant="light"
                      leftSection={<TbPlayerPlay size={16} />}
                      loading={startAgent.isPending}
                      onClick={() =>
                        // Remote control comes from the agent's persisted setting (Agent Settings).
                        startAgent.mutate(
                          {},
                          {
                            onError: (error) =>
                              notifications.show({
                                color: 'red',
                                message: getApiErrorMessage(error, 'Could not start the agent'),
                              }),
                          },
                        )
                      }
                    >
                      Start
                    </Button>
                  </Tooltip>
                )}
                <Button
                  variant="subtle"
                  leftSection={<TbHistory size={16} />}
                  onClick={() => setIncidentsOpen(true)}
                >
                  Incidents
                </Button>
                <Button variant="light" leftSection={<TbPlus size={16} />} onClick={() => setAddWorkOpen(true)}>
                  Add Card
                </Button>
              </Group>
            </Group>

            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Position</Table.Th>
                  <Table.Th>Card</Table.Th>
                  <Table.Th>Board</Table.Th>
                  <Table.Th>Workflow</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {selected.data.queue.map((card) => (
                  <Table.Tr key={card.cardId}>
                    <Table.Td>{card.queuePosition}</Table.Td>
                    <Table.Td>
                      {card.identifier} - {card.title}
                    </Table.Td>
                    <Table.Td>{card.boardName}</Table.Td>
                    <Table.Td>{card.currentStageName ?? card.workflowStatus ?? '-'}</Table.Td>
                  </Table.Tr>
                ))}
                {selected.data.queue.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={4}>
                      <Text ta="center" c="dimmed" py="md">
                        No queued cards.
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </Paper>
        )}
      </Stack>

      <AgentCreateModal opened={createOpen} onClose={() => setCreateOpen(false)} />
      {settingsAgent && (
        <AgentSettingsModal
          agent={settingsAgent}
          opened
          onClose={() => setSettingsAgent(null)}
          onDeleted={handleAgentDeleted}
        />
      )}
      {selected.data && addWorkOpen && (
        <AgentAddWorkModal agent={selected.data} opened onClose={() => setAddWorkOpen(false)} />
      )}
      {terminalAgent && (
        <AgentCliModal
          agent={terminalAgent}
          remoteControl={terminalAgent.remoteControlEnabled}
          opened
          onClose={() => setTerminalAgent(null)}
        />
      )}

      <Drawer
        opened={incidentsOpen}
        onClose={() => setIncidentsOpen(false)}
        title={`Incidents — ${selected.data?.name ?? ''}`}
        position="right"
        size="lg"
      >
        {incidents.isLoading && (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        )}
        {incidents.data?.length === 0 && (
          <Text c="dimmed" ta="center" py="xl">
            No incidents recorded.
          </Text>
        )}
        <Stack gap="xs">
          {(incidents.data ?? []).map((incident) => (
            <IncidentRow key={incident.id} incident={incident} />
          ))}
        </Stack>
      </Drawer>
    </Box>
  )
}

const SEVERITY_COLORS: Record<string, string> = {
  Info: 'gray',
  Warning: 'yellow',
  Error: 'orange',
  Critical: 'red',
}

function IncidentRow({ incident }: { incident: AgentIncidentDto }) {
  return (
    <Paper withBorder p="xs">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Stack gap={2} style={{ minWidth: 0 }}>
          <Group gap="xs">
            <Badge size="sm" color={SEVERITY_COLORS[incident.severity] ?? 'gray'} variant="light">
              {incident.severity}
            </Badge>
            <Text size="sm" fw={600}>
              {incident.kind}
            </Text>
          </Group>
          <Text size="sm" style={{ wordBreak: 'break-word' }}>
            {incident.message}
          </Text>
        </Stack>
        <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
          {new Date(incident.createdAt).toLocaleString()}
        </Text>
      </Group>
    </Paper>
  )
}

/**
 * Supervision status for always-on agents: green shield (supervised), pause shield (user-
 * suspended), red shield + live countdown while a restart is scheduled. Nothing for normal agents.
 */
function SupervisionBadge({ agent, compact = false }: { agent: AgentSummaryDto; compact?: boolean }) {
  const nextRestartAt = agent.supervision?.nextRestartAt ?? null
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (!nextRestartAt) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [nextRestartAt])

  if (!agent.alwaysOn) return null

  if (agent.supervision?.suspended) {
    return (
      <Tooltip label="Always-on suspended (stopped by user) — start to resume supervision" withArrow>
        <Badge size="sm" color="yellow" variant="light" leftSection={<TbShieldPause size={12} />}>
          {compact ? '' : 'suspended'}
        </Badge>
      </Tooltip>
    )
  }

  if (nextRestartAt) {
    const seconds = Math.max(0, Math.round((new Date(nextRestartAt).getTime() - now) / 1000))
    const display =
      seconds >= 86400
        ? `${Math.round(seconds / 86400)}d`
        : seconds >= 3600
          ? `${Math.round(seconds / 3600)}h`
          : seconds >= 60
            ? `${Math.round(seconds / 60)}m`
            : `${seconds}s`
    const attempt = (agent.supervision?.consecutiveFailures ?? 0) + 1
    return (
      <Tooltip label={`Restart attempt ${attempt} in ${display}`} withArrow>
        <Badge size="sm" color="red" variant="light" leftSection={<TbShieldX size={12} />}>
          {display}
        </Badge>
      </Tooltip>
    )
  }

  return (
    <Tooltip label="Always on — supervised (auto-restarts on crash)" withArrow>
      <Badge size="sm" color="green" variant="light" leftSection={<TbShieldCheck size={12} />}>
        {compact ? '' : 'always on'}
      </Badge>
    </Tooltip>
  )
}
