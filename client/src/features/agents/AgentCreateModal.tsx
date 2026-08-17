import {
  Button,
  Divider,
  Group,
  Input,
  Modal,
  SegmentedControl,
  Select,
  Stack,
  TextInput,
  Textarea,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbSparkles } from 'react-icons/tb'
import type { AgentAssignmentPolicy, AgentReplyStyle } from '../../api/agents'
import { AGENT_REPLY_STYLE_OPTIONS, useCreateAgent, useDraftAgent } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'
import { DirectoryAutocomplete } from './DirectoryAutocomplete'
import { AgentTuiSelection } from './AgentTuiSelection'
import { useAgentTuiProfiles } from '../../api/agentTui'

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
  const [replyStyle, setReplyStyle] = useState<AgentReplyStyle>('Normal')
  const { data: profiles } = useAgentTuiProfiles()

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
    setReplyStyle('Normal')
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
        replyStyle,
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: 'Agent created' })
          handleClose()
        },
        onError: (error) => {
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Agent creation failed'),
          })
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
