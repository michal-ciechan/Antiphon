import { useState, useMemo } from 'react'
import {
  Button,
  Group,
  Modal,
  Paper,
  Stack,
  Table,
  Text,
  TextInput,
  Switch,
  Badge,
  ActionIcon,
  Tooltip,
  Alert,
  Divider,
  Loader,
  Combobox,
  InputBase,
  useCombobox,
  ScrollArea,
} from '@mantine/core'
import {
  TbPlus,
  TbEdit,
  TbTrash,
  TbAlertCircle,
  TbPlugConnected,
  TbCheck,
  TbX,
  TbRefresh,
} from 'react-icons/tb'
import {
  useProjects,
  useCreateProject,
  useUpdateProject,
  useTestGitConnectivity,
  useGitHubRepos,
  useRefreshGitHubRepos,
  type ProjectDto,
} from '../../api/projects'
import { useBoards, useDeleteBoard, type BoardSummaryDto } from '../../api/boards'
import { ProjectDeleteDialog } from './ProjectDeleteDialog'
import { ApiKeysSection } from './ApiKeysSection'

export function ProjectConfig() {
  const { data: projects, isLoading, error } = useProjects()

  if (isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading projects">
        {error.message}
      </Alert>
    )
  }

  return <ProjectList projects={projects ?? []} />
}

function ProjectList({ projects }: { projects: ProjectDto[] }) {
  const createMutation = useCreateProject()
  const updateMutation = useUpdateProject()
  const testMutation = useTestGitConnectivity()
  const { data: boards } = useBoards()
  const { data: githubRepos } = useGitHubRepos()
  const refreshReposMutation = useRefreshGitHubRepos()

  const [editModalOpen, setEditModalOpen] = useState(false)
  const [editingProject, setEditingProject] = useState<ProjectDto | null>(null)
  const [deletingProject, setDeletingProject] = useState<ProjectDto | null>(null)
  const [testResult, setTestResult] = useState<{
    success: boolean
    message: string
  } | null>(null)

  const [formName, setFormName] = useState('')
  const [formGitUrl, setFormGitUrl] = useState('')
  const [formLocalRepoPath, setFormLocalRepoPath] = useState('')
  const [formBaseBranch, setFormBaseBranch] = useState('master')
  const [repoSearch, setRepoSearch] = useState('')
  const [formConstitutionPath, setFormConstitutionPath] = useState('AGENTS.md;CLAUDE.md;README.md')
  const [formGitHubEnabled, setFormGitHubEnabled] = useState(false)
  const [formNotificationsEnabled, setFormNotificationsEnabled] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
  })

  const filteredRepos = useMemo(() => {
    if (!githubRepos) return []
    const q = repoSearch.toLowerCase()
    return githubRepos.filter((r) => r.fullName.toLowerCase().includes(q)).slice(0, 50)
  }, [githubRepos, repoSearch])

  const openCreateModal = () => {
    setEditingProject(null)
    setFormName('')
    setFormGitUrl('')
    setFormLocalRepoPath('')
    setFormBaseBranch('master')
    setRepoSearch('')
    setFormConstitutionPath('AGENTS.md;CLAUDE.md;README.md')
    setFormGitHubEnabled(false)
    setFormNotificationsEnabled(false)
    setFormError(null)
    setTestResult(null)
    setEditModalOpen(true)
  }

  const openEditModal = (project: ProjectDto) => {
    setEditingProject(project)
    setFormName(project.name)
    setFormGitUrl(project.gitRepositoryUrl)
    setFormLocalRepoPath(project.localRepositoryPath ?? '')
    setFormBaseBranch(project.baseBranch ?? 'master')
    setRepoSearch('')
    setFormConstitutionPath(project.constitutionPath)
    setFormGitHubEnabled(project.gitHubIntegrationEnabled)
    setFormNotificationsEnabled(project.notificationsEnabled)
    setFormError(null)
    setTestResult(null)
    setEditModalOpen(true)
  }

  const boardsByProject = useMemo(() => {
    const grouped = new Map<string, BoardSummaryDto[]>()
    for (const board of boards ?? []) {
      const existing = grouped.get(board.projectId)
      if (existing) existing.push(board)
      else grouped.set(board.projectId, [board])
    }
    return grouped
  }, [boards])

  const handleTestConnectivity = async () => {
    setTestResult(null)
    try {
      const result = await testMutation.mutateAsync(formGitUrl)
      setTestResult({ success: result.success, message: result.message })
    } catch {
      setTestResult({ success: false, message: 'Failed to test connectivity.' })
    }
  }

  const handleSave = async () => {
    setFormError(null)
    try {
      if (editingProject) {
        await updateMutation.mutateAsync({
          id: editingProject.id,
          data: {
            name: formName,
            gitRepositoryUrl: formGitUrl,
            localRepositoryPath: formLocalRepoPath || undefined,
            baseBranch: formBaseBranch || 'master',
            constitutionPath: formConstitutionPath || undefined,
            gitHubIntegrationEnabled: formGitHubEnabled,
            notificationsEnabled: formNotificationsEnabled,
          },
        })
      } else {
        await createMutation.mutateAsync({
          name: formName,
          gitRepositoryUrl: formGitUrl,
          localRepositoryPath: formLocalRepoPath || undefined,
          baseBranch: formBaseBranch || 'master',
          constitutionPath: formConstitutionPath || undefined,
          gitHubIntegrationEnabled: formGitHubEnabled,
          notificationsEnabled: formNotificationsEnabled,
        })
      }
      setEditModalOpen(false)
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'body' in err) {
        const apiErr = err as { body: unknown }
        if (apiErr.body && typeof apiErr.body === 'object' && 'errors' in apiErr.body) {
          const errors = (apiErr.body as { errors: Record<string, string[]> }).errors
          const messages = Object.values(errors).flat().join('. ')
          setFormError(messages)
          return
        }
        if (apiErr.body && typeof apiErr.body === 'object' && 'detail' in apiErr.body) {
          setFormError((apiErr.body as { detail: string }).detail)
          return
        }
      }
      setFormError('An unexpected error occurred.')
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending

  return (
    <Stack>
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          Configure projects pointing at git repositories. Each project loads context files from
          the repository and supports per-project feature flags.
        </Text>
        <Button leftSection={<TbPlus />} onClick={openCreateModal}>
          Add Project
        </Button>
      </Group>

      {projects.length > 0 ? (
        <Paper withBorder>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Repository</Table.Th>
                <Table.Th>Boards</Table.Th>
                <Table.Th>Default Context</Table.Th>
                <Table.Th>Features</Table.Th>
                <Table.Th w={100}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {projects.map((project) => (
                <Table.Tr key={project.id}>
                  <Table.Td fw={500}>{project.name}</Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed" lineClamp={1} maw={250}>
                      {project.gitRepositoryUrl}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <ProjectBoardsCell boards={boardsByProject.get(project.id) ?? []} />
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {project.constitutionPath}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {project.gitHubIntegrationEnabled && (
                        <Badge variant="light" color="blue" size="sm">
                          GitHub
                        </Badge>
                      )}
                      {project.notificationsEnabled && (
                        <Badge variant="light" color="violet" size="sm">
                          Notifications
                        </Badge>
                      )}
                      {!project.gitHubIntegrationEnabled && !project.notificationsEnabled && (
                        <Text size="sm" c="dimmed">
                          --
                        </Text>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label="Edit">
                        <ActionIcon
                          variant="subtle"
                          aria-label="Edit project"
                          onClick={() => openEditModal(project)}
                        >
                          <TbEdit />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Delete">
                        <ActionIcon
                          variant="subtle"
                          color="red"
                          aria-label="Delete project"
                          onClick={() => setDeletingProject(project)}
                        >
                          <TbTrash />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      ) : (
        <Paper withBorder p="xl">
          <Text ta="center" c="dimmed">
            No projects configured. Click "Add Project" to get started.
          </Text>
        </Paper>
      )}

      {/* Create / Edit Project Modal */}
      <Modal
        opened={editModalOpen}
        onClose={() => setEditModalOpen(false)}
        title={editingProject ? 'Edit Project' : 'Add Project'}
        size="md"
      >
        <Stack>
          {formError && (
            <Alert color="red" icon={<TbAlertCircle />}>
              {formError}
            </Alert>
          )}
          <TextInput
            label="Name"
            placeholder="My Project"
            required
            value={formName}
            onChange={(e) => setFormName(e.currentTarget.value)}
          />
          {githubRepos && githubRepos.length > 0 && (
            <Combobox
              store={combobox}
              onOptionSubmit={(val) => {
                const repo = githubRepos.find((r) => r.cloneUrl === val)
                if (repo) {
                  setFormGitUrl(repo.cloneUrl)
                  setRepoSearch(repo.fullName)
                  if (!formName) setFormName(repo.fullName.split('/')[1] ?? repo.fullName)
                }
                combobox.closeDropdown()
              }}
            >
              <Combobox.Target>
                <InputBase
                  label="Repository"
                  placeholder="Search repos..."
                  value={repoSearch}
                  onChange={(e) => {
                    setRepoSearch(e.currentTarget.value)
                    combobox.openDropdown()
                    combobox.updateSelectedOptionIndex()
                  }}
                  onClick={() => combobox.openDropdown()}
                  onFocus={() => combobox.openDropdown()}
                  onBlur={() => combobox.closeDropdown()}
                  rightSection={
                    <Tooltip label="Refresh repos">
                      <ActionIcon
                        variant="subtle"
                        loading={refreshReposMutation.isPending}
                        onClick={(e) => {
                          e.stopPropagation()
                          refreshReposMutation.mutate()
                        }}
                      >
                        <TbRefresh />
                      </ActionIcon>
                    </Tooltip>
                  }
                />
              </Combobox.Target>
              <Combobox.Dropdown>
                <Combobox.Options>
                  <ScrollArea.Autosize mah={200}>
                    {filteredRepos.length > 0 ? (
                      filteredRepos.map((repo) => (
                        <Combobox.Option key={repo.cloneUrl} value={repo.cloneUrl}>
                          {repo.fullName}
                          {repo.isPrivate && (
                            <Text span size="xs" c="dimmed" ml={4}>
                              (private)
                            </Text>
                          )}
                        </Combobox.Option>
                      ))
                    ) : (
                      <Combobox.Empty>No repos found</Combobox.Empty>
                    )}
                  </ScrollArea.Autosize>
                </Combobox.Options>
              </Combobox.Dropdown>
            </Combobox>
          )}
          <TextInput
            label="Git Repository URL"
            placeholder="https://github.com/org/repo.git"
            required
            value={formGitUrl}
            onChange={(e) => setFormGitUrl(e.currentTarget.value)}
            rightSection={
              <Tooltip label="Test Connectivity">
                <ActionIcon
                  variant="subtle"
                  color="blue"
                  loading={testMutation.isPending}
                  onClick={handleTestConnectivity}
                  disabled={!formGitUrl}
                >
                  <TbPlugConnected />
                </ActionIcon>
              </Tooltip>
            }
          />
          {testResult && (
            <Alert
              color={testResult.success ? 'green' : 'red'}
              icon={testResult.success ? <TbCheck /> : <TbX />}
              withCloseButton
              onClose={() => setTestResult(null)}
              py="xs"
            >
              {testResult.message}
            </Alert>
          )}
          <TextInput
            label="Local Repository Path"
            placeholder="D:\src\MyProject"
            description="Absolute path to the local git clone. Leave empty to auto-clone under the workspace folder."
            value={formLocalRepoPath}
            onChange={(e) => setFormLocalRepoPath(e.currentTarget.value)}
          />
          <TextInput
            label="Base Branch"
            placeholder="master"
            description="The branch to compare workflow changes against in the Diff tab."
            value={formBaseBranch}
            onChange={(e) => setFormBaseBranch(e.currentTarget.value)}
          />
          <TextInput
            label="Default Context"
            placeholder="AGENTS.md;CLAUDE.md;README.md"
            description="Semicolon-separated list of files loaded as context for AI agents."
            value={formConstitutionPath}
            onChange={(e) => setFormConstitutionPath(e.currentTarget.value)}
          />
          <Switch
            label="GitHub Integration"
            description="Enable GitHub PR creation and monitoring for this project."
            checked={formGitHubEnabled}
            onChange={(e) => setFormGitHubEnabled(e.currentTarget.checked)}
          />
          <Switch
            label="Notifications"
            description="Enable notifications for workflow events in this project."
            checked={formNotificationsEnabled}
            onChange={(e) => setFormNotificationsEnabled(e.currentTarget.checked)}
          />
          {editingProject && (
            <>
              <Divider label="Project API keys" labelPosition="center" />
              <ApiKeysSection projectId={editingProject.id} />
            </>
          )}
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setEditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} loading={isSaving}>
              {editingProject ? 'Save Changes' : 'Create Project'}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <ProjectDeleteDialog project={deletingProject} onClose={() => setDeletingProject(null)} />
    </Stack>
  )
}

/**
 * The boards attached to a project, each removable from here — the issue's "currently cannot
 * delete items from project screen". Deleting the last board also deletes this project, so the
 * confirmation says so rather than surprising anyone.
 */
function ProjectBoardsCell({ boards }: { boards: BoardSummaryDto[] }) {
  const deleteBoard = useDeleteBoard()
  const [confirming, setConfirming] = useState<BoardSummaryDto | null>(null)
  const isLastBoard = boards.length === 1

  if (boards.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        --
      </Text>
    )
  }

  return (
    <>
      <Stack gap={4}>
        {boards.map((board) => (
          <Group key={board.id} gap={4} wrap="nowrap">
            <Text size="sm" lineClamp={1} maw={160}>
              {board.name}
            </Text>
            <Badge variant="light" size="xs" color="gray">
              {board.cardCount}
            </Badge>
            <Tooltip label="Delete board">
              <ActionIcon
                variant="subtle"
                color="red"
                size="sm"
                aria-label={`Delete board ${board.name}`}
                onClick={() => setConfirming(board)}
              >
                <TbTrash size={14} />
              </ActionIcon>
            </Tooltip>
          </Group>
        ))}
      </Stack>

      <Modal
        opened={!!confirming}
        onClose={() => setConfirming(null)}
        title="Delete Board"
        size="md"
      >
        <Stack>
          <Text>
            Delete the board "{confirming?.name}"? Its columns and{' '}
            {confirming?.cardCount ?? 0} card{confirming?.cardCount === 1 ? '' : 's'} go with it.
            This cannot be undone.
          </Text>
          {isLastBoard && (
            <Alert color="orange" icon={<TbAlertCircle />}>
              This is the project's last board, so the project will be deleted too.
            </Alert>
          )}
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setConfirming(null)}>
              Cancel
            </Button>
            <Button
              color="red"
              loading={deleteBoard.isPending}
              onClick={async () => {
                if (!confirming) return
                try {
                  await deleteBoard.mutateAsync(confirming.id)
                } finally {
                  setConfirming(null)
                }
              }}
            >
              Delete
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}
