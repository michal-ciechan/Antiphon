import {
  Alert,
  Anchor,
  Button,
  Chip,
  Code,
  Group,
  Modal,
  MultiSelect,
  Paper,
  Stack,
  Stepper,
  Switch,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import type { AgentModelLevel, AgentReplyStyle } from '../../api/agents'
import { getApiErrorMessage, getApiFieldErrors } from '../../api/client'
import { useWorkspaceGitInfos } from '../../api/filesystem'
import {
  readinessHeader,
  useSetupCatalog,
  useSetupProject,
  type AgentPresetDto,
  type ProjectSetupResultDto,
} from '../../api/projectSetup'
import { useProjects } from '../../api/projects'
import { CardModal } from '../board/CardModal'
import { DelegateModal } from '../delegations/DelegateModal'
import { normalizeDir } from '../home/projectGrouping'
import { AgentTuiSelection } from '../agents/AgentTuiSelection'
import { useRemoteControlSupport } from '../agents/useRemoteControlSupport'
import { useAgentTuiProfiles } from '../../api/agentTui'
import { DirectoryAutocomplete } from '../agents/DirectoryAutocomplete'
import { ModelLevelSelect } from '../agents/ModelLevelSelect'
import { ReplyStyleControl } from '../agents/ReplyStyleControl'
import { ProjectReadinessPanel } from './ProjectReadinessPanel'

const DEFAULT_COLUMNS = ['Backlog', 'In Progress', 'Review', 'Done']

function pathLeaf(directory: string): string {
  const trimmed = directory.trim().replace(/[\\/]+$/, '')
  const separator = Math.max(trimmed.lastIndexOf('\\'), trimmed.lastIndexOf('/'))
  return separator >= 0 ? trimmed.slice(separator + 1) : trimmed
}

function renderTemplate(template: string | null, values: Record<string, string>): string {
  if (!template) return ''
  return template
    .replaceAll('{project}', values.project)
    .replaceAll('{board}', values.board)
    .replaceAll('{repoUrl}', values.repoUrl || '(none)')
    .replaceAll('{directory}', values.directory)
}

function isUnderRoot(directory: string, root: string): boolean {
  const dir = normalizeDir(directory)
  const normalizedRoot = normalizeDir(root)
  return !!dir && !!normalizedRoot && (dir === normalizedRoot || dir.startsWith(`${normalizedRoot}\\`))
}

export function ProjectSetupModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const catalog = useSetupCatalog(opened)
  const { data: tuiProfiles } = useAgentTuiProfiles()
  const projects = useProjects(opened)
  const setup = useSetupProject()
  const [active, setActive] = useState(0)
  const [directory, setDirectory] = useState('')
  const [createDirectory, setCreateDirectory] = useState(false)
  const [pathMissing, setPathMissing] = useState(false)
  const [name, setName] = useState('')
  const [nameEdited, setNameEdited] = useState(false)
  const [gitRepositoryUrl, setGitRepositoryUrl] = useState('')
  const [baseBranch, setBaseBranch] = useState('master')
  const [boardName, setBoardName] = useState('')
  const [boardNameEdited, setBoardNameEdited] = useState(false)
  const [presetKey, setPresetKey] = useState<string | null>('orchestrator')
  const [agentName, setAgentName] = useState('')
  const [systemPromptAppend, setSystemPromptAppend] = useState('')
  const [tuiProfileId, setTuiProfileId] = useState<string | null>(null)
  const [modelId, setModelId] = useState<string | null>(null)
  const [modelLevel, setModelLevel] = useState<AgentModelLevel>('High')
  const [replyStyle, setReplyStyle] = useState<AgentReplyStyle>('Normal')
  const [alwaysOn, setAlwaysOn] = useState(false)
  const [remoteControlEnabled, setRemoteControlEnabled] = useState(false)
  const defaultTuiProfileId = tuiProfiles?.find((profile) => profile.isDefault)?.id ?? null
  const rc = useRemoteControlSupport({ tuiProfileId: tuiProfileId ?? defaultTuiProfileId })
  const [bundleKeys, setBundleKeys] = useState<string[]>([])
  const [skipAgent, setSkipAgent] = useState(false)
  const [startAgent, setStartAgent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [result, setResult] = useState<ProjectSetupResultDto | null>(null)
  const [cardOpen, setCardOpen] = useState(false)
  const [delegateOpen, setDelegateOpen] = useState(false)

  const gitInfos = useWorkspaceGitInfos(directory.trim() ? [directory.trim()] : [])
  const values = {
    project: name.trim() || pathLeaf(directory) || 'Project',
    board: boardName.trim() || name.trim() || pathLeaf(directory) || 'Board',
    repoUrl: gitRepositoryUrl.trim(),
    directory: directory.trim(),
  }
  const selectedPreset = catalog.data?.presets.find((preset) => preset.key === presetKey) ?? null
  const suggestedName = renderTemplate(selectedPreset?.namePattern ?? '{project} Agent', values)
  const suggestedPrompt = renderTemplate(selectedPreset?.systemPromptTemplate ?? null, values)
  const existingProject = useMemo(
    () => (projects.data ?? []).find((project) =>
      !!project.localRepositoryPath && normalizeDir(project.localRepositoryPath) === normalizeDir(directory)),
    [projects.data, directory],
  )

  const [filledKey, setFilledKey] = useState<string | null>(null)
  const selectPreset = (preset: AgentPresetDto) => {
    setPresetKey(preset.key)
    setAlwaysOn(preset.alwaysOn)
    setModelLevel(preset.modelLevel)
    setReplyStyle(preset.replyStyle)
    setBundleKeys([...preset.bundleKeys])
    setRemoteControlEnabled(preset.remoteControlEnabled)
    setFilledKey(preset.key)
  }

  if (selectedPreset && filledKey !== selectedPreset.key) {
    selectPreset(selectedPreset)
  }

  const next = () => {
    if (active === 0 && !directory.trim()) {
      setFieldErrors({ directory: 'Directory is required.' })
      return
    }
    setFieldErrors({})
    setError(null)
    setActive((current) => Math.min(current + 1, 4))
  }

  const submit = () => {
    if (skipAgent) setStartAgent(false)
    setError(null)
    setFieldErrors({})
    setup.mutate(
      {
        directory: directory.trim(),
        createDirectory,
        name: name.trim() || null,
        gitRepositoryUrl: gitRepositoryUrl.trim() || null,
        baseBranch: baseBranch.trim() || 'master',
        boardName: boardName.trim() || null,
        agent: skipAgent
          ? null
          : {
              preset: presetKey,
              tuiProfileId,
              modelId,
              modelLevel,
              replyStyle,
              alwaysOn,
              remoteControlEnabled: rc.supported && remoteControlEnabled,
              bundleKeys,
              name: agentName.trim() || null,
              systemPromptAppend: systemPromptAppend.trim() || null,
            },
        startAgent: skipAgent ? false : startAgent,
      },
      {
        onSuccess: (nextResult) => {
          setResult(nextResult)
          setActive(4)
        },
        onError: (submitError) => {
          const nextErrors = getApiFieldErrors(submitError)
          setFieldErrors(nextErrors)
          const fieldStep: Record<string, number> = {
            directory: 0,
            name: 1,
            gitRepositoryUrl: 1,
            boardName: 1,
            'agent.preset': 2,
            bundleKeys: 2,
          }
          const target = Object.keys(nextErrors).map((key) => fieldStep[key]).find((step) => step !== undefined)
          if (target !== undefined) setActive(target)
          setError(getApiErrorMessage(submitError, 'Could not set up the project.'))
        },
      },
    )
  }

  const gitInfo = gitInfos.data?.[0]
  const delegation = catalog.data?.delegation
  const allowed = delegation && !delegation.allowedRootsIsEmpty
    ? delegation.allowedRoots.some((root) => isUnderRoot(directory, root))
    : null

  return (
    <>
      <Modal opened={opened} onClose={onClose} title="Set up a project" size="xl">
        <Stack>
          {error && <Alert color="red">{error}</Alert>}
          <Stepper active={active} onStepClick={setActive} allowNextStepsSelect={false}>
            <Stepper.Step label="Directory" description="Choose the checkout">
              <Stack mt="md">
                <DirectoryAutocomplete
                  value={directory}
                  onChange={(nextDirectory) => {
                    setDirectory(nextDirectory)
                    if (!nameEdited) setName(pathLeaf(nextDirectory))
                    if (!boardNameEdited) setBoardName(pathLeaf(nextDirectory))
                  }}
                  createIfMissing={createDirectory}
                  onCreateIfMissingChange={setCreateDirectory}
                  onPathMissingChange={setPathMissing}
                  label="Project directory"
                />
                {fieldErrors.directory && <Text c="red" size="sm">{fieldErrors.directory}</Text>}
                {pathMissing && !createDirectory && (
                  <Text c="orange" size="sm">Select “Create this directory” to continue with this path.</Text>
                )}
                {gitInfo && (
                  <Text size="sm" c={gitInfo.isGitRepository ? 'dimmed' : 'orange'}>
                    {gitInfo.isGitRepository
                      ? gitInfo.isWorktree
                        ? `Git worktree inside the repository at ${gitInfo.repoRoot ?? gitInfo.path}`
                        : `Git repository at ${gitInfo.repoRoot ?? gitInfo.path}`
                      : 'Not a git repository — worktree tasks will not be available.'}
                  </Text>
                )}
                {existingProject && (
                  <Alert color="yellow">
                    This directory already belongs to <strong>{existingProject.name}</strong>.{' '}
                    <Anchor component={Link} to={`/settings?tab=projects&project=${existingProject.id}`}>
                      Open its readiness
                    </Anchor>
                  </Alert>
                )}
              </Stack>
            </Stepper.Step>

            <Stepper.Step label="Project & board" description="Name the defaults">
              <Stack mt="md">
                <TextInput label="Project name" value={name} onChange={(event) => { setNameEdited(true); setName(event.currentTarget.value) }} error={fieldErrors.name} />
                <TextInput
                  label="Git repository URL"
                  description="Read from the checkout if blank."
                  placeholder="https://github.com/org/repo.git"
                  value={gitRepositoryUrl}
                  onChange={(event) => setGitRepositoryUrl(event.currentTarget.value)}
                  error={fieldErrors.gitRepositoryUrl}
                />
                <TextInput label="Base branch" value={baseBranch} onChange={(event) => setBaseBranch(event.currentTarget.value)} />
                <TextInput label="Board name" value={boardName} onChange={(event) => { setBoardNameEdited(true); setBoardName(event.currentTarget.value) }} error={fieldErrors.boardName} />
                <Paper withBorder p="sm">
                  <Text size="sm" fw={500} mb={4}>Default columns</Text>
                  <Group gap="xs">{DEFAULT_COLUMNS.map((column) => <Chip key={column} checked readOnly>{column}</Chip>)}</Group>
                </Paper>
              </Stack>
            </Stepper.Step>

            <Stepper.Step label="First agent" description="Optional starter">
              <Stack mt="md">
                <Switch
                  label="Skip — no agent yet"
                  checked={skipAgent}
                  onChange={(event) => {
                    setSkipAgent(event.currentTarget.checked)
                    if (event.currentTarget.checked) setStartAgent(false)
                  }}
                />
                {!skipAgent && (
                  <>
                    <Text size="sm" fw={500}>Preset</Text>
                    <Chip.Group value={presetKey} onChange={(key) => {
                      const preset = catalog.data?.presets.find((candidate) => candidate.key === key)
                      if (preset) selectPreset(preset)
                    }}>
                      <Group>{(catalog.data?.presets ?? []).map((preset) => <Chip key={preset.key} value={preset.key}>{preset.label}</Chip>)}</Group>
                    </Chip.Group>
                    {fieldErrors['agent.preset'] && <Text c="red" size="sm">{fieldErrors['agent.preset']}</Text>}
                    {selectedPreset?.defaultWorkflowTemplateId && (
                      <Text size="sm" c="dimmed">Default workflow is set from this preset and stays editable after create.</Text>
                    )}
                    {catalog.data?.profiles.length === 0 ? (
                      <Alert color="yellow">
                        No enabled AI Agent TUI profiles. <Anchor component={Link} to="/settings?tab=agent-tui">Set one up in AI Agent TUI settings</Anchor>.
                      </Alert>
                    ) : (
                      <AgentTuiSelection tuiProfileId={tuiProfileId} modelId={modelId} onProfileChange={setTuiProfileId} onModelChange={setModelId} />
                    )}
                    <ModelLevelSelect value={modelLevel} onChange={setModelLevel} />
                    <ReplyStyleControl value={replyStyle} onChange={setReplyStyle} />
                    <MultiSelect
                      label="Attached bundles"
                      data={(catalog.data?.bundles ?? []).map((bundle) => ({ value: bundle.key, label: bundle.key }))}
                      value={bundleKeys}
                      onChange={setBundleKeys}
                      error={fieldErrors.bundleKeys}
                      searchable
                      clearable
                    />
                    <TextInput label="Agent name" placeholder={suggestedName} value={agentName} onChange={(event) => setAgentName(event.currentTarget.value)} />
                    <Textarea label="System prompt append" placeholder={suggestedPrompt || 'No preset prompt'} value={systemPromptAppend} onChange={(event) => setSystemPromptAppend(event.currentTarget.value)} minRows={3} />
                    <Paper withBorder p="sm">
                      <Text size="sm" fw={500}>Preview</Text>
                      <Text size="sm">{agentName.trim() || suggestedName}</Text>
                      {selectedPreset?.systemPromptTemplate && <Code block>{systemPromptAppend.trim() || suggestedPrompt}</Code>}
                    </Paper>
                    <Switch label="Always on" checked={alwaysOn} onChange={(event) => setAlwaysOn(event.currentTarget.checked)} />
                    <Switch
                      label="Remote control"
                      description={
                        rc.supported
                          ? 'Every start arms /remote-control so the session can be driven from claude.ai.'
                          : (rc.reason ?? 'Not available for this runner.')
                      }
                      checked={rc.supported && remoteControlEnabled}
                      disabled={!rc.supported}
                      onChange={(event) => setRemoteControlEnabled(event.currentTarget.checked)}
                    />
                  </>
                )}
                <Switch
                  label="Start agent now"
                  checked={startAgent}
                  disabled={skipAgent}
                  onChange={(event) => setStartAgent(event.currentTarget.checked)}
                />
              </Stack>
            </Stepper.Step>

            <Stepper.Step label="Delegation" description="Know the boundary">
              <Stack mt="md">
                {delegation?.allowedRootsIsEmpty ? (
                  <Text>Each caller’s own tree only — the safe default.</Text>
                ) : (
                  <>
                    <Text>{allowed ? 'This directory is under an allowed delegation root.' : 'This directory is not under an allowed delegation root.'}</Text>
                    <Text size="sm" c="dimmed">Up to {delegation?.maxConcurrentTasks ?? 0} concurrent task{delegation?.maxConcurrentTasks === 1 ? '' : 's'}, depth {delegation?.maxDepth ?? 0}, default {delegation?.defaultLevel ?? 'High'} tier.</Text>
                  </>
                )}
              </Stack>
            </Stepper.Step>

            <Stepper.Completed>
              <Stack mt="md">
                {result ? (
                  <>
                    <Text fw={600}>{readinessHeader(result.readiness)}</Text>
                    {result.notes.map((note) => <Alert key={note} color="yellow">{note}</Alert>)}
                    <ProjectReadinessPanel readiness={result.readiness} />
                    <Group>
                      <Button onClick={() => setCardOpen(true)}>Create the first card</Button>
                      <Button variant="light" onClick={() => setDelegateOpen(true)}>Delegate a task</Button>
                    </Group>
                  </>
                ) : (
                  <Text c="dimmed">Review the setup, then create the project.</Text>
                )}
              </Stack>
            </Stepper.Completed>
          </Stepper>
          {!result && (
            <Group justify="space-between">
              <Button variant="subtle" onClick={() => setActive((current) => Math.max(current - 1, 0))} disabled={active === 0}>Back</Button>
              {active < 4 ? <Button onClick={next}>Next</Button> : <Button onClick={submit} loading={setup.isPending}>Create project</Button>}
            </Group>
          )}
        </Stack>
      </Modal>
      {result && <CardModal boardId={result.board.id} card={null} opened={cardOpen} onClose={() => setCardOpen(false)} />}
      {result && <DelegateModal opened={delegateOpen} onClose={() => setDelegateOpen(false)} prefill={{ workingDirectory: result.project.localRepositoryPath }} />}
    </>
  )
}
