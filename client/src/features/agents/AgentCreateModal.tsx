import { Button, Group, Modal, Select, Stack, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import type { AgentAssignmentPolicy } from '../../api/agents'
import { useCreateAgent } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'

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
  const [name, setName] = useState('')
  const [workingDirectory, setWorkingDirectory] = useState('')
  const [details, setDetails] = useState('')
  const [assignmentPolicy, setAssignmentPolicy] = useState<AgentAssignmentPolicy>('AutoPick')

  const reset = () => {
    setName('')
    setWorkingDirectory('')
    setDetails('')
    setAssignmentPolicy('AutoPick')
  }

  const handleClose = () => {
    reset()
    onClose()
  }

  const handleSubmit = () => {
    if (!name.trim() || !workingDirectory.trim()) return

    createAgent.mutate(
      {
        name: name.trim(),
        workingDirectory: workingDirectory.trim(),
        details: details.trim() || null,
        assignmentPolicy,
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

  return (
    <Modal opened={opened} onClose={handleClose} title="New Agent">
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
        <Group justify="flex-end">
          <Button variant="subtle" onClick={handleClose}>
            Cancel
          </Button>
          <Button
            onClick={handleSubmit}
            loading={createAgent.isPending}
            disabled={!name.trim() || !workingDirectory.trim()}
          >
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
