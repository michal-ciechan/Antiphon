import {
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  Menu,
  Paper,
  Stack,
  Tabs,
  Text,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { useDisclosure, useLocalStorage } from '@mantine/hooks'
import { useMemo } from 'react'
import { TbChevronDown, TbFolder, TbLayoutList, TbMessage, TbUserShare } from 'react-icons/tb'
import { Link } from 'react-router'
import { useAgentList } from '../../api/agents'
import { useAgentTasks } from '../../api/agentTasks'
import { DelegateModal } from '../delegations/DelegateModal'
import { FilesReviewPanel, type FilesPanelHeights } from '../agents/FilesReviewPanel'
import { SessionTranscriptPanel } from '../agents/SessionTranscriptPanel'
import { AgentRail } from './AgentRail'
import { ProjectTasksPanel } from './ProjectTasksPanel'
import { buildProjects, pickAgent } from './projectGrouping'

// Fill the viewport under the 56px app header and the AppShell.Main md padding (16px top+bottom).
const PAGE_HEIGHT = 'calc(100dvh - 56px - 2 * var(--mantine-spacing-md))'

// Everything stacked above the Monaco/rendered viewer: app header + page header row + the viewer's
// own filename/mode row + paddings. The max() keeps small windows usable.
const FILES_HEIGHTS: FilesPanelHeights = { viewer: 'max(240px, calc(100vh - 275px))' }

/**
 * The home workspace (feature 008): one screen shaped like the actual working loop — pick a
 * project (a working directory) now and again, watch its agents, live in the files view (rendered
 * docs first), talk to the selected agent, and queue work for the pool without leaving the page.
 */
export function HomePage() {
  const agents = useAgentList()
  const tasks = useAgentTasks()
  const [delegateOpen, delegate] = useDisclosure(false)

  const projects = useMemo(
    () => buildProjects(agents.data ?? [], tasks.data ?? []),
    [agents.data, tasks.data],
  )

  const [storedProject, setStoredProject] = useLocalStorage<string | null>({
    key: 'antiphon-home-project',
    defaultValue: null,
  })
  const [agentByProject, setAgentByProject] = useLocalStorage<Record<string, string>>({
    key: 'antiphon-home-agent-by-project',
    defaultValue: {},
  })

  // Default to a project that has agents — the files pane needs one; a delegations-only
  // directory is still switchable to, just not the landing view.
  const project = useMemo(
    () =>
      projects.find((p) => p.key === storedProject) ??
      projects.find((p) => p.agents.length > 0) ??
      projects[0] ??
      null,
    [projects, storedProject],
  )
  const agent = pickAgent(project, project ? (agentByProject[project.key] ?? null) : null)
  const sessionId = agent ? (agent.liveSession?.id ?? agent.persistentSessionId) : null

  if (agents.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  return (
    <Box style={{ height: PAGE_HEIGHT, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <Group justify="space-between" pb="sm" wrap="nowrap" style={{ flexShrink: 0 }}>
        <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
          <ProjectSwitcher
            projects={projects}
            selectedKey={project?.key ?? null}
            onSelect={setStoredProject}
          />
          {project && (
            <Text size="xs" c="dimmed" truncate style={{ maxWidth: 380 }} visibleFrom="md">
              {project.path}
            </Text>
          )}
        </Group>
        <Group gap="xs" wrap="nowrap" style={{ flexShrink: 0 }}>
          <Anchor component={Link} to="/orchestrator?tab=delegations" size="sm" c="dimmed">
            Delegations board
          </Anchor>
          <Button
            size="xs"
            color="violet"
            leftSection={<TbUserShare size={15} />}
            onClick={delegate.open}
          >
            Delegate work
          </Button>
        </Group>
      </Group>

      <DelegateModal
        opened={delegateOpen}
        onClose={delegate.close}
        prefill={project ? { workingDirectory: project.path } : undefined}
      />

      <Group align="stretch" gap="sm" wrap="nowrap" style={{ flexGrow: 1, minHeight: 0 }}>
        {/* Agent rail */}
        <Paper
          withBorder
          p="xs"
          w={240}
          style={{ flexShrink: 0, display: 'flex', flexDirection: 'column', minHeight: 0 }}
        >
          <Group justify="space-between" pb={6} style={{ flexShrink: 0 }}>
            <Text size="xs" tt="uppercase" fw={700} c="dimmed">
              Agents
            </Text>
            <Anchor component={Link} to="/agents" size="xs" c="dimmed">
              manage
            </Anchor>
          </Group>
          <AgentRail
            agents={project?.agents ?? []}
            selectedId={agent?.id ?? null}
            onSelect={(id) => {
              if (project) setAgentByProject({ ...agentByProject, [project.key]: id })
            }}
          />
        </Paper>

        {/* Files — the page's dominant surface */}
        <Box style={{ flexGrow: 1, minWidth: 0, minHeight: 0, overflow: 'auto' }}>
          {agent ? (
            <FilesReviewPanel agentId={agent.id} heights={FILES_HEIGHTS} layout="sidebar" />
          ) : (
            <Paper withBorder p="xl" style={{ height: '100%' }}>
              <Stack align="center" justify="center" gap="xs" style={{ height: '100%' }}>
                <Text c="dimmed">
                  {projects.length === 0
                    ? 'No agents and no work yet.'
                    : 'No agents in this project yet.'}
                </Text>
                <Text size="sm" c="dimmed">
                  <Anchor onClick={delegate.open}>Delegate work</Anchor> to queue something for the
                  agent pool, or{' '}
                  <Anchor component={Link} to="/agents">
                    create an agent
                  </Anchor>
                  .
                </Text>
              </Stack>
            </Paper>
          )}
        </Box>

        {/* Chat / Tasks dock */}
        <Paper
          withBorder
          w={400}
          style={{ flexShrink: 0, display: 'flex', flexDirection: 'column', minHeight: 0 }}
          data-testid="home-dock"
        >
          <Tabs
            defaultValue="chat"
            style={{ display: 'flex', flexDirection: 'column', minHeight: 0, flexGrow: 1 }}
          >
            <Tabs.List style={{ flexShrink: 0 }}>
              <Tabs.Tab value="chat" leftSection={<TbMessage size={14} />}>
                Chat
              </Tabs.Tab>
              <Tabs.Tab value="tasks" leftSection={<TbLayoutList size={14} />}>
                Tasks
              </Tabs.Tab>
            </Tabs.List>
            <Tabs.Panel
              value="chat"
              p="xs"
              style={{ display: 'flex', flexDirection: 'column', minHeight: 0, flexGrow: 1 }}
            >
              {sessionId ? (
                <SessionTranscriptPanel sessionId={sessionId} withComposer composerCollapsed fitHeight />
              ) : (
                <Text size="sm" c="dimmed" ta="center" py="xl">
                  {agent
                    ? 'No session for this agent yet — start it from the Agents page to talk to it here.'
                    : 'Select an agent to talk to it here.'}
                </Text>
              )}
            </Tabs.Panel>
            <Tabs.Panel
              value="tasks"
              p="xs"
              style={{ display: 'flex', flexDirection: 'column', minHeight: 0, flexGrow: 1 }}
            >
              {project ? (
                <ProjectTasksPanel projectKey={project.key} />
              ) : (
                <Text size="sm" c="dimmed" ta="center" py="xl">
                  No project selected.
                </Text>
              )}
            </Tabs.Panel>
          </Tabs>
        </Paper>
      </Group>
    </Box>
  )
}

/**
 * Compact on purpose — projects are switched now and again, not all the time, so this is a small
 * dropdown rather than a persistent rail.
 */
function ProjectSwitcher({
  projects,
  selectedKey,
  onSelect,
}: {
  projects: ReturnType<typeof buildProjects>
  selectedKey: string | null
  onSelect: (key: string) => void
}) {
  const selected = projects.find((p) => p.key === selectedKey) ?? null
  return (
    <Menu shadow="md" position="bottom-start" width={360}>
      <Menu.Target>
        <UnstyledButton aria-label="Switch project" data-testid="project-switcher">
          <Group gap={6} wrap="nowrap">
            <TbFolder size={18} />
            <Text fw={700} size="lg" truncate style={{ maxWidth: 280 }}>
              {selected?.label ?? 'No project'}
            </Text>
            <TbChevronDown size={14} />
          </Group>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Projects — one per working directory</Menu.Label>
        {projects.length === 0 && (
          <Menu.Item disabled>No agent directories yet</Menu.Item>
        )}
        {projects.map((p) => (
          <Menu.Item key={p.key} onClick={() => onSelect(p.key)}>
            <Group gap="xs" wrap="nowrap" justify="space-between">
              <Box style={{ minWidth: 0 }}>
                <Text size="sm" fw={p.key === selectedKey ? 700 : 400} truncate>
                  {p.label}
                </Text>
                <Text size="xs" c="dimmed" truncate>
                  {p.path}
                </Text>
              </Box>
              <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
                <Tooltip label={`${p.agents.length} agent${p.agents.length === 1 ? '' : 's'}`}>
                  <Badge size="xs" variant="default">
                    {p.agents.length}
                  </Badge>
                </Tooltip>
                {p.activeTaskCount > 0 && (
                  <Tooltip label={`${p.activeTaskCount} task${p.activeTaskCount === 1 ? '' : 's'} in flight`}>
                    <Badge size="xs" variant="light" color="active">
                      {p.activeTaskCount}
                    </Badge>
                  </Tooltip>
                )}
              </Group>
            </Group>
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  )
}
