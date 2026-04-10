import { useState } from 'react'
import { Modal, Text, Box, Group, Button, Checkbox, HoverCard, Anchor } from '@mantine/core'
import { useDeleteWorkflow, useWorkflowDeleteInfo } from '../../api/workflows'
import { useBranchDiff } from '../../api/projects'

interface DeleteWorkflowModalProps {
  workflowId: string
  opened: boolean
  onClose: () => void
  /** Called after successful deletion (e.g. navigate away) */
  onDeleted?: () => void
}

export function DeleteWorkflowModal({ workflowId, opened, onClose, onDeleted }: DeleteWorkflowModalProps) {
  const [deleteBranch, setDeleteBranch] = useState(false)

  const { data: deleteInfo, isLoading: deleteInfoLoading } = useWorkflowDeleteInfo(workflowId, opened)
  const { data: branchDiff } = useBranchDiff(opened ? workflowId : undefined)
  const deleteWorkflow = useDeleteWorkflow()

  // Prefer the branch-diff resolved branch (actual agent branch) over the DB field
  const resolvedBranch = branchDiff?.headBranch?.replace(/^origin\//, '') ?? deleteInfo?.branchName
  const hasPeers = (deleteInfo?.peerWorkflows.length ?? 0) > 0
  // Keep checkbox disabled while delete-info is still loading to avoid a race where
  // hasPeers appears false before the data arrives.
  const checkboxDisabled = deleteInfoLoading || hasPeers

  function handleClose() {
    setDeleteBranch(false)
    onClose()
  }

  function handleDelete() {
    deleteWorkflow.mutate(
      { id: workflowId, deleteBranch, branchName: resolvedBranch },
      {
        onSuccess: () => {
          setDeleteBranch(false)
          onDeleted?.()
          onClose()
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={handleClose} title="Delete Workflow" centered size="sm">
      <Text size="sm" mb="md">
        Are you sure you want to delete this workflow? This cannot be undone.
      </Text>

      {resolvedBranch && (
        <Box mb="lg">
          {hasPeers ? (
            <HoverCard
              withArrow
              shadow="md"
              width={220}
              openDelay={100}
              closeDelay={300}
            >
              <HoverCard.Target>
                <Box style={{ display: 'inline-block' }}>
                  <Checkbox
                    label={`Also delete branch: ${resolvedBranch}`}
                    checked={false}
                    onChange={() => {}}
                    disabled
                    size="sm"
                  />
                </Box>
              </HoverCard.Target>
              <HoverCard.Dropdown>
                <Text size="xs" c="dimmed" mb={6}>Branch is also used by:</Text>
                {deleteInfo!.peerWorkflows.map((p) => (
                  <Anchor
                    key={p.id}
                    href={`/workflow/${p.id}`}
                    size="xs"
                    style={{ display: 'block' }}
                  >
                    {p.name}
                  </Anchor>
                ))}
              </HoverCard.Dropdown>
            </HoverCard>
          ) : (
            <Checkbox
              label={`Also delete branch: ${resolvedBranch}`}
              checked={checkboxDisabled ? false : deleteBranch}
              onChange={(e) => setDeleteBranch(e.currentTarget.checked)}
              disabled={checkboxDisabled}
              size="sm"
            />
          )}
        </Box>
      )}

      <Group justify="flex-end" gap="sm">
        <Button variant="default" onClick={handleClose}>
          Cancel
        </Button>
        <Button color="red" loading={deleteWorkflow.isPending} onClick={handleDelete}>
          Delete
        </Button>
      </Group>
    </Modal>
  )
}
