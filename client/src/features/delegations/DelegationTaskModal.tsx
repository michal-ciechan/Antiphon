import { Modal } from '@mantine/core'
import { useAgentTask } from '../../api/agentTasks'
import { TaskDetailBody, TaskDetailTitle } from './TaskDetailBody'

/**
 * The same task body as the drawer, in a modal — the home-rail Tasks section opens this instead of
 * sliding a drawer over the workspace.
 */
export function DelegationTaskModal({ taskId, onClose }: { taskId: string; onClose: () => void }) {
  const detail = useAgentTask(taskId)

  return (
    <Modal
      opened
      onClose={onClose}
      size="xl"
      title={detail.data ? <TaskDetailTitle detail={detail.data} /> : 'Task'}
    >
      <TaskDetailBody taskId={taskId} onClose={onClose} />
    </Modal>
  )
}
