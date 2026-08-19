import { useState } from 'react'
import { Alert, Button, Checkbox, Group, List, Loader, Modal, Stack, Text } from '@mantine/core'
import { TbAlertTriangle } from 'react-icons/tb'
import { getApiErrorMessage } from '../../api/client'
import {
  useDeleteProject,
  useProjectDeletionImpact,
  type ProjectDto,
} from '../../api/projects'

import { describeImpact } from './describeImpact'

export interface ProjectDeleteDialogProps {
  project: ProjectDto | null
  onClose: () => void
  onDeleted?: () => void
}

/**
 * Confirms deleting a project, having first said what that destroys. A project that owns nothing
 * deletes on one click; anything else needs the acknowledgement ticked, which is what sends
 * `force=true`. Blockers (orchestrator workflows) disable the button outright.
 */
export function ProjectDeleteDialog({ project, onClose, onDeleted }: ProjectDeleteDialogProps) {
  const impact = useProjectDeletionImpact(project?.id ?? null)
  const deleteMutation = useDeleteProject()
  const [acknowledged, setAcknowledged] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // A fresh dialog must never open with the previous project's tick still set — adjusted during
  // render when the project changes, not in an effect, so the stale tick can never paint.
  const [prevProjectId, setPrevProjectId] = useState(project?.id)
  if (project?.id !== prevProjectId) {
    setPrevProjectId(project?.id)
    setAcknowledged(false)
    setError(null)
  }

  const data = impact.data
  const lines = data ? describeImpact(data) : []
  const needsAcknowledgement = data?.requiresConfirmation ?? false
  const blocked = data ? !data.canDelete : false
  const canConfirm = !!data && !blocked && (!needsAcknowledgement || acknowledged)

  const handleDelete = async () => {
    if (!project || !canConfirm) return
    setError(null)
    try {
      await deleteMutation.mutateAsync({ id: project.id, force: needsAcknowledgement })
      onDeleted?.()
      onClose()
    } catch (e) {
      // The server's 409 detail says exactly what is still attached — far more use than
      // "API Error 409: Conflict".
      setError(getApiErrorMessage(e, 'Delete failed.'))
    }
  }

  return (
    <Modal opened={!!project} onClose={onClose} title="Delete Project" size="md">
      <Stack>
        <Text>
          Delete the project "{project?.name}"? This cannot be undone.
        </Text>

        {impact.isLoading && (
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="sm" c="dimmed">
              Checking what this would delete...
            </Text>
          </Group>
        )}

        {impact.isError && (
          <Alert color="red" icon={<TbAlertTriangle />}>
            Could not check what this project owns. Deleting now could fail or destroy more than
            you expect.
          </Alert>
        )}

        {blocked && (
          <Alert color="red" icon={<TbAlertTriangle />} title="Cannot delete this project">
            <List size="sm">
              {data!.blockers.map((blocker) => (
                <List.Item key={blocker}>{blocker}</List.Item>
              ))}
            </List>
          </Alert>
        )}

        {!blocked && lines.length > 0 && (
          <Alert
            color="orange"
            icon={<TbAlertTriangle />}
            title="This will also delete"
            data-testid="project-deletion-impact"
          >
            <List size="sm">
              {lines.map((line) => (
                <List.Item key={line}>{line}</List.Item>
              ))}
            </List>
          </Alert>
        )}

        {!blocked && data && !data.requiresConfirmation && (
          <Text size="sm" c="dimmed" data-testid="project-deletion-impact">
            This project has no boards or cards.
          </Text>
        )}

        {needsAcknowledgement && !blocked && (
          <Checkbox
            checked={acknowledged}
            onChange={(event) => setAcknowledged(event.currentTarget.checked)}
            label="I understand this deletes everything listed above"
          />
        )}

        {error && (
          <Alert color="red" icon={<TbAlertTriangle />}>
            {error}
          </Alert>
        )}

        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>
            Cancel
          </Button>
          <Button
            color="red"
            onClick={handleDelete}
            disabled={!canConfirm}
            loading={deleteMutation.isPending}
          >
            Delete
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
