import {
  Button,
  Divider,
  Group,
  Input,
  Modal,
  MultiSelect,
  SegmentedControl,
  Select,
  Stack,
  Switch,
  TextInput,
  Textarea,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbSparkles } from 'react-icons/tb'
import type { AgentAssignmentPolicy, AgentModelLevel, AgentReplyStyle } from '../../api/agents'
import { AGENT_REPLY_STYLE_OPTIONS, useCreateAgent, useDraftAgent, useInstructionBundles } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { DirectoryAutocomplete } from './DirectoryAutocomplete'
import { AgentTuiSelection } from './AgentTuiSelection'
import { useAgentTuiProfiles } from '../../api/agentTui'
import { useBoards } from '../../api/boards'
import { ModelLevelSelect } from './ModelLevelSelect'

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
  const [creationError, setCreationError] = useState<string | null>(null)
  const { data: profiles } = useAgentTuiProfiles()
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
    setCreationError(null)
    draftAgent.reset()
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
        alwaysOn,
        remoteControlEnabled,
        boardId: boardId ?? undefined,
        bundleKeys,
        systemPromptAppend: systemPromptAppend.trim() || null,
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
        <Input.Wrapper
          label="Reply style"
          description={
            AGENT_REPLY_STYLE_OPTIONS.find((option) => option.value === replyStyle)?.description ?? ''
          }
        >
          <SegmentedControl
            fullWidth
            mt={4}
            data={AGENT_REPLY_STYLE_OPTIONS.map(({ value, label }) => ({ value, label }))}
            value={replyStyle}
            onChange={(value) => setReplyStyle(value as AgentReplyStyle)}
          />
        </Input.Wrapper>
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
          description="Every start arms /remote-control so the session can be driven from claude.ai."
          checked={remoteControlEnabled}
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
