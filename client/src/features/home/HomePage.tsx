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
import { useDisclosure, useLocalStorage, useMediaQuery } from '@mantine/hooks'
import { useMemo } from 'react'
import {
  TbAlertHexagon,
  TbChevronDown,
  TbFolder,
  TbGitBranch,
  TbLayoutList,
  TbMessage,
  TbUserShare,
} from 'react-icons/tb'
import { Link } from 'react-router'
import { useAgentList } from '../../api/agents'
import { useAgentTasks } from '../../api/agentTasks'
import { useAttention } from '../../api/attention'
import { useWorkspaceGitInfos, useWorkspaceWorktrees } from '../../api/filesystem'
import { DelegateModal } from '../delegations/DelegateModal'
import { FilesReviewPanel, type FilesPanelHeights } from '../agents/FilesReviewPanel'
import { SessionTranscriptPanel } from '../agents/SessionTranscriptPanel'
import { AgentRail } from './AgentRail'
import { MobileHomePage } from './MobileHomePage'
import { ReportBugButton } from '../diagnostics/ReportBugButton'
import { ProjectTasksPanel } from './ProjectTasksPanel'
import {
  buildProjects,
  isActiveTask,
  mergeWorktrees,
  pickAgent,
  pickWorkspace,
  taskRunDir,
  type WorkspaceEntry,
  type WorkspaceKind,
} from './projectGrouping'

// Fill the viewport under the 56px app header and the AppShell.Main md padding (16px top+bottom).
const PAGE_HEIGHT = 'calc(100dvh - 56px - 2 * var(--mantine-spacing-md))'

// Everything stacked above the Monaco/rendered viewer: app header + page header row + the viewer's
// own filename/mode row + paddings. The max() keeps small windows usable.
const FILES_HEIGHTS: FilesPanelHeights = { viewer: 'max(240px, calc(100vh - 275px))' }

/**
 * Below 48em (the app-wide mobile threshold — BoardPage, SessionTerminal) the home surface is the
 * three bands, not the desktop rail squeezed narrow (spec 2026-08-17 §D3). The branch lives in a
 * wrapper rather than an early return so neither page's hooks are ever conditionally skipped.
 */
export function HomePage() {
  const isMobile = useMediaQuery('(max-width: 48em)') ?? false
  return isMobile ? <MobileHomePage /> : <DesktopHomePage />
}

/**
 * The home workspace (feature 008): one screen shaped like the actual working loop — pick a
 * project (a working directory) now and again, watch its agents, live in the files view (rendered
 * docs first), talk to the selected agent, and queue work for the pool without leaving the page.
 */
function DesktopHomePage() {
  const agents = useAgentList()
  const tasks = useAgentTasks()
  const [delegateOpen, delegate] = useDisclosure(false)

  // Git identity (repo root + branch) for every directory on screen — what lets a worktree- or
  // subdirectory-scoped agent nest under the repo it belongs to instead of standing alone.
  const gitDirs = useMemo(() => {
    const dirs = new Set<string>()
    for (const a of agents.data ?? []) {
      if (a.workingDirectory.trim()) dirs.add(a.workingDirectory.trim())
    }
    for (const t of tasks.data ?? []) {
      if (!isActiveTask(t)) continue
      dirs.add(taskRunDir(t))
      if (t.repoPath) dirs.add(t.repoPath)
    }
    return [...dirs].sort()
  }, [agents.data, tasks.data])
  const gitInfos = useWorkspaceGitInfos(gitDirs)

  const projects = useMemo(
    () => buildProjects(agents.data ?? [], tasks.data ?? [], gitInfos.data ?? []),
    [agents.data, tasks.data, gitInfos.data],
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

  // The selected project's live worktree list fills in checkouts nobody has an agent in yet
  // (and refreshes branch names from git truth).
  const worktreeListing = useWorkspaceWorktrees(project?.path ?? null)
  const projectView = useMemo(
    () => (project ? mergeWorktrees(project, worktreeListing.data) : null),
    [project, worktreeListing.data],
  )

  const [workspaceByProject, setWorkspaceByProject] = useLocalStorage<Record<string, string>>({
    key: 'antiphon-home-workspace-by-project',
    defaultValue: {},
  })
  const workspace = pickWorkspace(
    projectView,
    project ? (workspaceByProject[project.key] ?? null) : null,
  )

  // Remembered agent is keyed by WORKSPACE directory; a main workspace's key equals the old
  // project key, so pre-switcher selections carry over.
  const agent = pickAgent(workspace, workspace ? (agentByProject[workspace.key] ?? null) : null)
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
          {projectView && workspace && (
            <WorkspaceSwitcher
              workspaces={projectView.workspaces}
              selected={workspace}
              onSelect={(key) => {
                if (project) setWorkspaceByProject({ ...workspaceByProject, [project.key]: key })
              }}
            />
          )}
        </Group>
        <Group gap="xs" wrap="nowrap" style={{ flexShrink: 0 }}>
          <NeedsAttentionBadge />
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
        prefill={
          workspace
            ? { workingDirectory: workspace.path }
            : project
              ? { workingDirectory: project.path }
              : undefined
        }
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
            agents={workspace?.agents ?? []}
            selectedId={agent?.id ?? null}
            onSelect={(id) => {
              if (workspace) setAgentByProject({ ...agentByProject, [workspace.key]: id })
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
                    : workspace && workspace.kind !== 'main'
                      ? `No agent is scoped to this ${workspace.kind === 'worktree' ? 'worktree' : 'subdirectory'} yet.`
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
              <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', paddingRight: 8 }}>
                <ReportBugButton agentId={agent?.id} sessionId={sessionId ?? undefined} />
              </div>
            </Tabs.List>
            <Tabs.Panel
              value="chat"
              p="xs"
              style={{ display: 'flex', flexDirection: 'column', minHeight: 0, flexGrow: 1 }}
            >
              {sessionId ? (
                <SessionTranscriptPanel
                  sessionId={sessionId}
                  withComposer
                  composerCollapsed
                  fitHeight
                  transcriptBinding={agent?.liveSession?.transcriptBinding}
                  liveStatus={agent?.liveSession?.status}
                  startedAt={agent?.liveSession?.startedAt}
                  agentId={agent?.id}
                />
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
              {projectView ? (
                <ProjectTasksPanel
                  dirKeys={[projectView.key, ...projectView.workspaces.map((w) => w.key)]}
                />
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
 * The one home-screen change CARD-0035 makes (slice 6): a count of what is stuck, linking to the
 * diagnostic tab.
 *
 * <p><b>It renders nothing at zero, and that is the feature.</b> A permanent "Needs attention: 0"
 * chip is a control an operator stops seeing within a week, and the badge only works if its presence
 * alone means something. So a quiet fleet gets a header with nothing extra in it — the reassurance
 * lives on the tab itself, where there is room to say why it is empty.</p>
 *
 * <p>Settled failures are excluded, matching the tab badge and the panel header: they are context
 * carried for 24h, and counting them would make every ordinary day look like a bad one.</p>
 */
function NeedsAttentionBadge() {
  const attention = useAttention()
  const openCount = (attention.data?.items ?? []).filter(
    (item) => item.kind !== 'RecentFailure',
  ).length

  if (openCount === 0) return null

  return (
    <Anchor component={Link} to="/orchestrator?tab=attention" underline="never">
      <Badge
        size="sm"
        variant="light"
        color="danger"
        leftSection={<TbAlertHexagon size={12} />}
        style={{ textTransform: 'none' }}
      >
        Needs attention ({openCount})
      </Badge>
    </Anchor>
  )
}

/**
 * The directory scope WITHIN the selected project: its main checkout, agent-scoped
 * subdirectories, and git worktrees — branch names included. Selecting one narrows the agent
 * rail, files pane, and chat to the agents living there. Always rendered as a dropdown — even
 * with just the main directory — so the control stays discoverable; the badge counts the dirs.
 */
function WorkspaceSwitcher({
  workspaces,
  selected,
  onSelect,
}: {
  workspaces: WorkspaceEntry[]
  selected: WorkspaceEntry
  onSelect: (key: string) => void
}) {
  const kindLabel: Record<WorkspaceKind, string> = {
    main: 'main',
    subdir: 'subdirectory',
    worktree: 'worktree',
  }
  return (
    <Menu shadow="md" position="bottom-start" width={440}>
      <Menu.Target>
        <UnstyledButton aria-label="Switch workspace" data-testid="workspace-switcher" visibleFrom="md">
          <Group gap={6} wrap="nowrap">
            <Text size="xs" c="dimmed" truncate style={{ maxWidth: 340 }}>
              {selected.path}
            </Text>
            {selected.branch && <BranchBadge branch={selected.branch} />}
            <Badge size="xs" variant="default" style={{ flexShrink: 0, textTransform: 'none' }}>
              {workspaces.length} {workspaces.length === 1 ? 'dir' : 'dirs'}
            </Badge>
            <TbChevronDown size={12} style={{ flexShrink: 0 }} />
          </Group>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Workspaces — main directory, subdirectories, worktrees</Menu.Label>
        {workspaces.map((w) => (
          <Menu.Item key={w.key} onClick={() => onSelect(w.key)}>
            <Group gap="xs" wrap="nowrap" justify="space-between">
              <Box style={{ minWidth: 0 }}>
                <Group gap={6} wrap="nowrap">
                  <Text size="sm" fw={w.key === selected.key ? 700 : 400} truncate>
                    {w.label}
                  </Text>
                  {w.kind !== 'main' && (
                    <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
                      {kindLabel[w.kind]}
                    </Text>
                  )}
                </Group>
                <Text size="xs" c="dimmed" truncate>
                  {w.path}
                </Text>
              </Box>
              <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
                {w.branch && <BranchBadge branch={w.branch} />}
                {w.agents.length > 0 && (
                  <Tooltip label={`${w.agents.length} agent${w.agents.length === 1 ? '' : 's'}`}>
                    <Badge size="xs" variant="default">
                      {w.agents.length}
                    </Badge>
                  </Tooltip>
                )}
                {w.activeTaskCount > 0 && (
                  <Tooltip
                    label={`${w.activeTaskCount} task${w.activeTaskCount === 1 ? '' : 's'} in flight`}
                  >
                    <Badge size="xs" variant="light" color="active">
                      {w.activeTaskCount}
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

/** Branch chip — casing preserved, git icon, capped width so feat/very-long-names behave. */
function BranchBadge({ branch }: { branch: string }) {
  return (
    <Badge
      size="xs"
      variant="light"
      color="teal"
      leftSection={<TbGitBranch size={10} />}
      style={{ textTransform: 'none', maxWidth: 180, flexShrink: 0 }}
    >
      {branch}
    </Badge>
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
        <Menu.Label>Projects — one per repo (worktrees fold in)</Menu.Label>
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
                {p.workspaces.length > 1 && (
                  <Tooltip label={`${p.workspaces.length} directories (worktrees/subdirs)`}>
                    <Badge size="xs" variant="light" color="teal">
                      {p.workspaces.length}
                    </Badge>
                  </Tooltip>
                )}
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
