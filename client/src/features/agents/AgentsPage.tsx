import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  Paper,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { useEffect, useState } from 'react'
import { TbAlertCircle, TbLayoutKanban, TbPlus, TbSettings } from 'react-icons/tb'
import { Link } from 'react-router'
import type { AgentSummaryDto } from '../../api/agents'
import { useAgent, useAgentList } from '../../api/agents'
import { AgentCreateModal } from './AgentCreateModal'
import { AgentQueueAssignModal } from './AgentQueueAssignModal'
import { AgentSettingsModal } from './AgentSettingsModal'

export function AgentsPage() {
  const agents = useAgentList()
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null)
  const selected = useAgent(selectedAgentId)
  const [createOpen, setCreateOpen] = useState(false)
  const [assignOpen, setAssignOpen] = useState(false)
  const [settingsAgent, setSettingsAgent] = useState<AgentSummaryDto | null>(null)

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
                    <Group justify="space-between" align="flex-start" wrap="nowrap" pr={28}>
                      <Text fw={700} lineClamp={1} style={{ flex: 1, minWidth: 0 }}>
                        {agent.name}
                      </Text>
                      <Badge variant="light" style={{ flexShrink: 0 }}>
                        {agent.status}
                      </Badge>
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
              <Button variant="light" leftSection={<TbPlus size={16} />} onClick={() => setAssignOpen(true)}>
                Add Card
              </Button>
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
      {selectedAgentId && assignOpen && (
        <AgentQueueAssignModal
          agentId={selectedAgentId}
          opened={assignOpen}
          onClose={() => setAssignOpen(false)}
        />
      )}
    </Box>
  )
}
