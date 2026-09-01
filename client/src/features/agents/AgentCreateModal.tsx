import {
  Button,
  Chip,
  Divider,
  Group,
  Input,
  Modal,
  MultiSelect,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbSparkles } from 'react-icons/tb'
import type { AgentAssignmentPolicy, AgentModelLevel, AgentReplyStyle } from '../../api/agents'
import { useCreateAgent, useDraftAgent, useInstructionBundles } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { useSetupCatalog, type AgentPresetDto } from '../../api/projectSetup'
import { DirectoryAutocomplete } from './DirectoryAutocomplete'
import { AgentTuiSelection } from './AgentTuiSelection'
import { useRemoteControlSupport } from './useRemoteControlSupport'
import { useAgentTuiProfiles } from '../../api/agentTui'
import { useBoards } from '../../api/boards'
import { ModelLevelSelect } from './ModelLevelSelect'
import { ReplyStyleControl } from './ReplyStyleControl'

const ASSIGNMENT_POLICIES: Array<{ value: AgentAssignmentPolicy; label: string }> = [
  { value: 'AutoPick', label: 'Auto pick' },
  { value: 'ManualConfirm', label: 'Manual confirm' },
  { value: 'Paused', label: 'Paused' },
]

interface AgentCreateModalProps {
  opened: boolean
  onClose: () => void
}

export function AgentCreateModal({ opened, onClose }: AgentCreateModalProps) {
  const createAgent = useCreateAgent()
  const draftAgent = useDraftAgent()
  const [draftDescription, setDraftDescription] = useState('')
  const [name, setName] = useState('')
  const [workingDirectory, setWorkingDirectory] = useState('')
  const [createDir, setCreateDir] = useState(false)
  const [pathMissing, setPathMissing] = useState(false)
  const [details, setDetails] = useState('')
  const [assignmentPolicy, setAssignmentPolicy] = useState<AgentAssignmentPolicy>('AutoPick')
  const [tuiProfileId, setTuiProfileId] = useState<string | null>(null)
  const [modelId, setModelId] = useState<string | null>(null)
  const [modelLevel, setModelLevel] = useState<AgentModelLevel>('High')
  const [replyStyle, setReplyStyle] = useState<AgentReplyStyle>('Normal')
  const [alwaysOn, setAlwaysOn] = useState(false)
  const [remoteControlEnabled, setRemoteControlEnabled] = useState(false)
  const [boardId, setBoardId] = useState<string | null>(null)
  const [bundleKeys, setBundleKeys] = useState<string[]>([])
  const [systemPromptAppend, setSystemPromptAppend] = useState('')
  const [presetKey, setPresetKey] = useState<string | null>('orchestrator')
  const [creationError, setCreationError] = useState<string | null>(null)
  const catalog = useSetupCatalog(opened)
  const [filledKey, setFilledKey] = useState<string | null>(null)
  const { data: profiles } = useAgentTuiProfiles()
  const defaultProfileId = profiles?.find((profile) => profile.isDefault)?.id ?? null
  const rc = useRemoteControlSupport({ tuiProfileId: tuiProfileId ?? defaultProfileId })
  const boards = useBoards()
  const bundles = useInstructionBundles(opened)
  const boardOptions = useMemo(
    () => (boards.data ?? []).map((board) => ({ value: board.id, label: `${board.projectName} / ${board.name}` })),
    [boards.data],
  )
  const bundleOptions = useMemo(
    () => (bundles.data ?? []).map((bundle) => ({ value: bundle.key, label: bundle.key })),
    [bundles.data],
  )

  const reset = () => {
    setDraftDescription('')
    setName('')
    setWorkingDirectory('')
    setCreateDir(false)
    setPathMissing(false)
    setDetails('')
    setAssignmentPolicy('AutoPick')
    setTuiProfileId(null)
    setModelId(null)
    setModelLevel('High')
    setReplyStyle('Normal')
    setAlwaysOn(false)
    setRemoteControlEnabled(false)
    setBoardId(null)
    setBundleKeys([])
    setSystemPromptAppend('')
    setPresetKey('orchestrator')
    setFilledKey(null)
    setCreationError(null)
    draftAgent.reset()
  }

  const selectPreset = (preset: AgentPresetDto) => {
    setPresetKey(preset.key)
    setAlwaysOn(preset.alwaysOn)
    setModelLevel(preset.modelLevel)
    setReplyStyle(preset.replyStyle)
    setBundleKeys([...preset.bundleKeys])
    setRemoteControlEnabled(preset.remoteControlEnabled)
    setFilledKey(preset.key)
  }

  const selectedPreset = catalog.data?.presets.find((preset) => preset.key === presetKey) ?? null
  if (opened && selectedPreset && filledKey !== selectedPreset.key) {
    selectPreset(selectedPreset)
  }

  // A missing directory may only be submitted when the user opts to create it.
  const blockedByMissingDir = pathMissing && !createDir

  const handleClose = () => {
    reset()
    onClose()
  }

  const handleSubmit = () => {
    if (!name.trim() || !workingDirectory.trim() || blockedByMissingDir) return

    setCreationError(null)

    const profileId =
      tuiProfileId ?? profiles?.find((profile) => profile.isDefault)?.id ?? null
    if (!profileId) return

    createAgent.mutate(
      {
        name: name.trim(),
        workingDirectory: workingDirectory.trim(),
        details: details.trim() || null,
        assignmentPolicy,
        createWorkingDirectory: createDir,
        tuiProfileId: profileId,
        modelId,
        modelLevel,
        replyStyle,
        remoteControlEnabled: rc.supported && remoteControlEnabled,
        boardId: boardId ?? undefined,
        systemPromptAppend: systemPromptAppend.trim() || null,
        preset: presetKey,
        ...(filledKey
          ? { alwaysOn, bundleKeys }
          : {}),
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: 'Agent created' })
          handleClose()
        },
        onError: (error) => {
          setCreationError(getApiErrorMessage(error, 'Agent creation failed'))
        },
      },
    )
  }

  const handleDraft = () => {
    if (!draftDescription.trim()) return

    draftAgent.mutate(
      { description: draftDescription.trim() },
      {
        onSuccess: (draft) => {
          setName(draft.name)
          setWorkingDirectory(draft.workingDirectory)
          setDetails(draft.details)
          setAssignmentPolicy(draft.assignmentPolicy)
          notifications.show({
            color: draft.usedAi ? 'green' : 'yellow',
            message: draft.usedAi ? 'Agent details drafted' : 'Agent details filled from description',
          })
        },
        onError: (error) => {
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Agent draft failed'),
          })
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={handleClose} title="New Agent" size="lg">
      <Stack>
        <Textarea
          label="Describe what you want"
          minRows={3}
          autosize
          value={draftDescription}
          onChange={(event) => setDraftDescription(event.currentTarget.value)}
        />
        <Group justify="flex-end">
          <Button
            variant="light"
            leftSection={<TbSparkles size={16} />}
            onClick={handleDraft}
            loading={draftAgent.isPending}
            disabled={!draftDescription.trim()}
          >
            Draft details
          </Button>
        </Group>
        <Divider label="or enter details manually" labelPosition="center" />
        <Text size="sm" fw={500}>Preset</Text>
        <Chip.Group
          value={presetKey}
          onChange={(key) => {
            const preset = catalog.data?.presets.find((candidate) => candidate.key === key)
            if (preset) selectPreset(preset)
          }}
        >
          <Group>
            {(catalog.data?.presets ?? []).map((preset) => (
              <Chip key={preset.key} value={preset.key}>{preset.label}</Chip>
            ))}
          </Group>
        </Chip.Group>
        {catalog.data?.presets.find((preset) => preset.key === presetKey)?.defaultWorkflowTemplateId && (
          <Text size="sm" c="dimmed">Default workflow is set from this preset and stays editable after create.</Text>
        )}
        <TextInput
          label="Name"
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
        />
        <DirectoryAutocomplete
          value={workingDirectory}
          onChange={setWorkingDirectory}
          createIfMissing={createDir}
          onCreateIfMissingChange={setCreateDir}
          onPathMissingChange={setPathMissing}
        />
        <Textarea
          label="Details"
          description="Standing job for this agent, written into CLAUDE.md. Not sent as a first prompt on Start — use Add work, the session composer, or StartAgentRequest.prompt."
          value={details}
          onChange={(event) => setDetails(event.currentTarget.value)}
        />
        <Select
          label="Board"
          description="Optional. Leave empty to use the project's board."
          placeholder="Use the project's board"
          data={boardOptions}
          value={boardId}
          onChange={setBoardId}
          disabled={boards.isLoading}
          searchable
          clearable
        />
        {creationError && <Input.Error>{creationError}</Input.Error>}
        <Select
          label="Assignment policy"
          data={ASSIGNMENT_POLICIES}
          value={assignmentPolicy}
          onChange={(value) => setAssignmentPolicy((value as AgentAssignmentPolicy | null) ?? 'AutoPick')}
          allowDeselect={false}
        />
        <AgentTuiSelection
          tuiProfileId={
            tuiProfileId ?? profiles?.find((profile) => profile.isDefault)?.id ?? null
          }
          modelId={modelId}
          onProfileChange={setTuiProfileId}
          onModelChange={setModelId}
        />
        <ModelLevelSelect value={modelLevel} onChange={setModelLevel} />
        <ReplyStyleControl value={replyStyle} onChange={setReplyStyle} />
        <MultiSelect
          label="Attached bundles"
          description="Instructions composed into this agent's next launch."
          data={bundleOptions}
          value={bundleKeys}
          onChange={setBundleKeys}
          searchable
          clearable
          disabled={bundles.isLoading}
        />
        <Textarea
          label="System prompt append"
          description="Optional instructions added after the attached bundles."
          value={systemPromptAppend}
          onChange={(event) => setSystemPromptAppend(event.currentTarget.value)}
          minRows={3}
        />
        <Switch
          label="Always on"
          description="Auto-start at boot and auto-restart on crash (backing off, never giving up). Stop suspends until the next manual start."
          checked={alwaysOn}
          onChange={(event) => setAlwaysOn(event.currentTarget.checked)}
        />
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
        <Group justify="flex-end">
          <Button variant="subtle" onClick={handleClose}>
            Cancel
          </Button>
          <Button
            onClick={handleSubmit}
            loading={createAgent.isPending}
            disabled={
              !name.trim() ||
              !workingDirectory.trim() ||
              draftAgent.isPending ||
              blockedByMissingDir ||
              !(tuiProfileId ?? profiles?.find((profile) => profile.isDefault)?.id)
            }
          >
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
