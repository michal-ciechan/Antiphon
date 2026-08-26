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
  Menu,
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
  TbDotsVertical,
  TbFiles,
  TbHistory,
  TbLayoutKanban,
  TbPlayerPlay,
  TbPlayerStop,
  TbPlus,
  TbRefreshAlert,
  TbSettings,
  TbShieldCheck,
  TbShieldPause,
  TbShieldX,
  TbTerminal2,
} from 'react-icons/tb'
import { Link, useSearchParams } from 'react-router'
import type { AgentIncidentDto, AgentSummaryDto } from '../../api/agents'
import {
  AGENT_REPLY_STYLE_OPTIONS,
  useAgent,
  useAgentIncidents,
  useAgentList,
  useStartAgent,
  useStopAgent,
} from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { useBoards, type BoardSummaryDto } from '../../api/boards'
import { useProjectReadinessList, type ProjectReadinessDto } from '../../api/projectSetup'
import { AgentActivityBadge } from './AgentActivityBadge'
import { HerdrStatusBadge } from './HerdrStatusBadge'
import { SessionContextBadge } from './SessionContextBadge'
import { AgentAddWorkModal } from './AgentAddWorkModal'
import { FilesReviewPanel } from './FilesReviewPanel'
import { AgentCliModal } from './AgentCliModal'
import { AgentCreateModal } from './AgentCreateModal'
import { AgentSettingsModal } from './AgentSettingsModal'
import { ProjectSetupModal } from '../settings/ProjectSetupModal'

export function AgentsPage() {
  const agents = useAgentList()
  const boards = useBoards()
  const projectIds = [
    ...new Set(
      (agents.data ?? [])
        .map((agent) => boards.data?.find((board) => board.id === agent.boardId)?.projectId)
        .filter((id): id is string => !!id),
    ),
  ]
  const readinessQueries = useProjectReadinessList(projectIds)
  const readinessByProject = new Map<string, ProjectReadinessDto>()
  projectIds.forEach((id, index) => {
    const data = readinessQueries[index]?.data
    if (data) readinessByProject.set(id, data)
  })
  // ?agent=<id> deep-links a specific agent — how the delegations board points at the delegate
  // that ran a task.
  const [searchParams] = useSearchParams()
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(searchParams.get('agent'))
  const selected = useAgent(selectedAgentId)
  const [createOpen, setCreateOpen] = useState(false)
  const [setupProjectOpen, setSetupProjectOpen] = useState(false)
  const [addWorkOpen, setAddWorkOpen] = useState(false)
  const [settingsAgent, setSettingsAgent] = useState<AgentSummaryDto | null>(null)
  const [terminalAgent, setTerminalAgent] = useState<AgentSummaryDto | null>(null)
  const [incidentsOpen, setIncidentsOpen] = useState(false)
  const startAgent = useStartAgent(selectedAgentId ?? '')
  const stopAgent = useStopAgent(selectedAgentId ?? '')
  const incidents = useAgentIncidents(selectedAgentId, incidentsOpen)

  // Default to the first agent once the list arrives — adjusted during render, not in an effect,
  // so the page never paints a frame with nothing selected.
  if (!selectedAgentId && agents.data?.[0]) {
    setSelectedAgentId(agents.data[0].id)
  }

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
          <Group>
            <Button variant="light" leftSection={<TbPlus size={16} />} onClick={() => setSetupProjectOpen(true)}>
              Set up a project
            </Button>
            <Button leftSection={<TbPlus size={16} />} onClick={() => setCreateOpen(true)}>
              New Agent
            </Button>
          </Group>
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
                        <ReplyStyleBadge agent={agent} />
                        <BundleDriftBadge agent={agent} />
                        <SupervisionBadge agent={agent} compact />
                        <AgentReadinessChip
                          agent={agent}
                          boards={boards.data}
                          readinessByProject={readinessByProject}
                        />
                        {agent.liveSession?.agentKind === 'ClaudeCode' && (
                          <SessionContextBadge
                            fullness={agent.liveSession.contextFullness}
                            state={agent.liveSession.contextFullnessState}
                            size="xs"
                          />
                        )}
                        <AgentActivityBadge agent={agent} />
                        {agent.liveSession && <HerdrStatusBadge session={agent.liveSession} working={agent.working} size="xs" />}
                      </Group>
                    </Group>
                    <Text size="xs" c="dimmed" lineClamp={1}>
                      {agent.workingDirectory}
                    </Text>
                    <Text size="sm">{agent.queueLength} queued</Text>
                  </Stack>
                </Paper>
              </UnstyledButton>
              {/* Liveness lives in the terminal icon colour: green = running, yellow = starting/
                  stopping, gray = no session. The status badge is reserved for real activity. */}
              <Tooltip
                label={
                  agent.liveSession?.status === 'Running'
                    ? 'Terminal — live now'
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
              <Menu shadow="md" position="bottom-end" withinPortal>
                <Menu.Target>
                  <ActionIcon
                    variant="subtle"
                    color="gray"
                    aria-label={`Agent menu ${agent.name}`}
                    pos="absolute"
                    top={8}
                    right={8}
                  >
                    <TbDotsVertical size={18} />
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item
                    leftSection={<TbFiles size={14} />}
                    component="a"
                    href={`/agents/${agent.id}/files`}
                    target="_blank"
                  >
                    Open files view
                  </Menu.Item>
                  {agent.boardId && (
                    <Menu.Item
                      leftSection={<TbLayoutKanban size={14} />}
                      component={Link}
                      to={`/boards/${agent.boardId}`}
                    >
                      Open board
                    </Menu.Item>
                  )}
                  <Menu.Divider />
                  <Menu.Item leftSection={<TbSettings size={14} />} onClick={() => setSettingsAgent(agent)}>
                    Edit settings
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
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
                  <AgentActivityBadge agent={selected.data} />
                  {selected.data.liveSession && <HerdrStatusBadge session={selected.data.liveSession} working={selected.data.working} />}
                  {selected.data.liveSession?.agentKind === 'ClaudeCode' && (
                    <SessionContextBadge
                      fullness={selected.data.liveSession.contextFullness}
                      state={selected.data.liveSession.contextFullnessState}
                    />
                  )}
                  <SupervisionBadge agent={selected.data} />
                  <ReplyStyleBadge agent={selected.data} />
                  <BundleDriftBadge agent={selected.data} />
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
                {selected.data.liveSession || selected.data.status === 'Running' ? (
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

            <FilesReviewPanel agentId={selected.data.id} showExpand />
          </Paper>
        )}
      </Stack>

      <AgentCreateModal opened={createOpen} onClose={() => setCreateOpen(false)} />
      <ProjectSetupModal opened={setupProjectOpen} onClose={() => setSetupProjectOpen(false)} />
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
 * The agent's reply style (CARD-0060), when it has one. Nothing at all for `Normal` — Normal
 * composes no instruction, so a chip announcing it would be a badge for the absence of a setting on
 * every agent in the list.
 */
function AgentReadinessChip({
  agent,
  boards,
  readinessByProject,
}: {
  agent: AgentSummaryDto
  boards: BoardSummaryDto[] | undefined
  readinessByProject: Map<string, ProjectReadinessDto>
}) {
  const projectId = boards?.find((board) => board.id === agent.boardId)?.projectId
  if (!projectId) return null
  const readiness = readinessByProject.get(projectId)
  if (!readiness) return null
  const directory = readiness.checks.find((c) => c.key === 'agent-directory')
  const runner = readiness.checks.find((c) => c.key === 'agent-runner')
  const label =
    directory?.status === 'Missing'
      ? 'directory missing'
      : runner?.status === 'Missing'
        ? 'runner profile disabled'
        : null
  if (!label) return null
  return (
    <Badge
      size="sm"
      color="red"
      variant="light"
      data-testid={`agent-readiness-${agent.id}`}
      title={label}
    >
      {label}
    </Badge>
  )
}

function ReplyStyleBadge({ agent }: { agent: AgentSummaryDto }) {
  const style = agent.replyStyle ?? 'Normal'
  if (style === 'Normal') return null

  return (
    <Tooltip
      label={AGENT_REPLY_STYLE_OPTIONS.find((option) => option.value === style)?.description ?? style}
      withArrow
    >
      <Badge size="sm" color="grape" variant="light">
        {style.toLowerCase()}
      </Badge>
    </Tooltip>
  )
}

/**
 * The running session was launched with instruction bundles the repo has since moved on from
 * (CARD-0058) — an edited bundle file, an attachment added or removed, a changed reply style.
 *
 * Deliberately quiet and deliberately inert: the agent picks the new instructions up at its NEXT
 * launch, and nothing here offers to make that happen now. Typing bundles into a live session is
 * exactly what this design does not do — the staleness is bounded and the badge is here to make it
 * visible, not to trigger a fix.
 */
function BundleDriftBadge({ agent }: { agent: AgentSummaryDto }) {
  if (!agent.bundlesOutOfDate) return null

  return (
    <Tooltip
      label="Running with older instruction bundles. It restarts with the current ones at its next launch."
      withArrow
    >
      <Badge size="sm" color="yellow" variant="light" leftSection={<TbRefreshAlert size={12} />}>
        bundles
      </Badge>
    </Tooltip>
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
