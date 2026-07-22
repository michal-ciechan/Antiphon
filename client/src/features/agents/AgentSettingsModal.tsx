import { Button, Divider, Group, Modal, Select, Stack, Switch, Text, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState } from 'react'
import { TbTrash } from 'react-icons/tb'
import type { AgentAssignmentPolicy, AgentSummaryDto } from '../../api/agents'
import { fetchPreamblePreset, useDeleteAgent, useUpdateAgent } from '../../api/agents'
import { useBoards } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'

const ASSIGNMENT_POLICIES: Array<{ value: AgentAssignmentPolicy; label: string }> = [
  { value: 'AutoPick', label: 'Auto pick' },
  { value: 'ManualConfirm', label: 'Manual confirm' },
  { value: 'Paused', label: 'Paused' },
]

interface AgentSettingsModalProps {
  agent: AgentSummaryDto | null
  opened: boolean
  onClose: () => void
  onDeleted: (agentId: string) => void
}

export function AgentSettingsModal({ agent, opened, onClose, onDeleted }: AgentSettingsModalProps) {
  const boards = useBoards()
  // Hooks need a stable agent id; fall back to an empty string when closed (modal is gated on agent).
  const updateAgent = useUpdateAgent(agent?.id ?? '')
  const deleteAgent = useDeleteAgent()

  const [name, setName] = useState('')
  const [workingDirectory, setWorkingDirectory] = useState('')
  const [details, setDetails] = useState('')
  const [assignmentPolicy, setAssignmentPolicy] = useState<AgentAssignmentPolicy>('AutoPick')
  const [boardId, setBoardId] = useState<string | null>(null)
  const [alwaysOn, setAlwaysOn] = useState(false)
  const [remoteControlEnabled, setRemoteControlEnabled] = useState(false)
  const [systemPromptAppend, setSystemPromptAppend] = useState('')
  const [loadingPreset, setLoadingPreset] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  // Reload the form whenever a different agent is opened.
  useEffect(() => {
    if (!opened || !agent) return
    setName(agent.name)
    setWorkingDirectory(agent.workingDirectory)
    setDetails(agent.details)
    setAssignmentPolicy(agent.assignmentPolicy)
    setBoardId(agent.boardId)
    setAlwaysOn(agent.alwaysOn)
    setRemoteControlEnabled(agent.remoteControlEnabled)
    setSystemPromptAppend(agent.systemPromptAppend ?? '')
    setConfirmingDelete(false)
  }, [agent, opened])

  const handleUsePreset = async () => {
    setLoadingPreset(true)
    try {
      const preset = await fetchPreamblePreset('telegram')
      setSystemPromptAppend(preset.template)
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Failed to load preset') })
    } finally {
      setLoadingPreset(false)
    }
  }

  const boardOptions = useMemo(
    () => (boards.data ?? []).map((board) => ({ value: board.id, label: `${board.projectName} / ${board.name}` })),
    [boards.data],
  )

  const handleSave = () => {
    if (!agent || !name.trim() || !workingDirectory.trim()) return

    updateAgent.mutate(
      {
        name: name.trim(),
        workingDirectory: workingDirectory.trim(),
        details: details.trim() || null,
        defaultWorkflowTemplateId: agent.defaultWorkflowTemplateId,
        assignmentPolicy,
        boardId,
        alwaysOn,
        remoteControlEnabled,
        // Empty string clears the preamble server-side; null would mean "leave unchanged".
        systemPromptAppend: systemPromptAppend.trim(),
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: 'Agent updated' })
          onClose()
        },
        onError: (error) => {
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Agent update failed') })
        },
      },
    )
  }

  const handleDelete = () => {
    if (!agent) return

    deleteAgent.mutate(agent.id, {
      onSuccess: () => {
        notifications.show({ color: 'green', message: 'Agent deleted' })
        onDeleted(agent.id)
        onClose()
      },
      onError: (error) => {
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Agent deletion failed') })
      },
    })
  }

  return (
    <Modal opened={opened} onClose={onClose} title="Agent Settings" size="lg">
      <Stack>
        <TextInput
          label="Name"
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
        />
        <TextInput
          label="Working directory"
          value={workingDirectory}
          onChange={(event) => setWorkingDirectory(event.currentTarget.value)}
        />
        <Textarea
          label="Details"
          autosize
          minRows={3}
          value={details}
          onChange={(event) => setDetails(event.currentTarget.value)}
        />
        <Select
          label="Default board"
          placeholder="No board"
          data={boardOptions}
          value={boardId}
          onChange={setBoardId}
          disabled={boards.isLoading}
          clearable
          searchable
        />
        <Select
          label="Assignment policy"
          data={ASSIGNMENT_POLICIES}
          value={assignmentPolicy}
          onChange={(value) => setAssignmentPolicy((value as AgentAssignmentPolicy | null) ?? 'AutoPick')}
          allowDeselect={false}
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

        <Textarea
          label="System prompt (appended)"
          description="Channel preamble appended to the system prompt on every launch (--append-system-prompt). {agentName} and {channels} render at launch time. Empty = none; also disables bootstrap/restart/compaction notes."
          autosize
          minRows={3}
          maxRows={12}
          value={systemPromptAppend}
          onChange={(event) => setSystemPromptAppend(event.currentTarget.value)}
        />
        <Group justify="flex-start">
          <Button variant="light" size="xs" onClick={handleUsePreset} loading={loadingPreset}>
            Use Telegram preset
          </Button>
        </Group>

        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={handleSave}
            loading={updateAgent.isPending}
            disabled={!name.trim() || !workingDirectory.trim()}
          >
            Save
          </Button>
        </Group>

        <Divider label="Danger zone" labelPosition="center" />

        {confirmingDelete ? (
          <Group justify="space-between">
            <Text size="sm" c="red">
              Delete this agent? Its cards will be unassigned.
            </Text>
            <Group gap="xs">
              <Button variant="subtle" onClick={() => setConfirmingDelete(false)}>
                Cancel
              </Button>
              <Button color="red" onClick={handleDelete} loading={deleteAgent.isPending}>
                Delete agent
              </Button>
            </Group>
          </Group>
        ) : (
          <Group justify="flex-end">
            <Button
              variant="light"
              color="red"
              leftSection={<TbTrash size={16} />}
              onClick={() => setConfirmingDelete(true)}
            >
              Delete agent
            </Button>
          </Group>
        )}
      </Stack>
    </Modal>
  )
}
