import { Drawer } from '@mantine/core'
import { useAgentTask } from '../../api/agentTasks'
import { TaskDetailBody, TaskDetailTitle } from './TaskDetailBody'

/**
 * Everything about one task, and the four things you can do to it. The drawer exists because the
 * chip is deliberately small: the brief, the delegate's untouched report and the event timeline are
 * what you need when a task went wrong, and none of them fit on a board.
 */
export function TaskDrawer({ taskId, onClose }: { taskId: string | null; onClose: () => void }) {
  const detail = useAgentTask(taskId)

  return (
    <Drawer
      opened={!!taskId}
      onClose={onClose}
      position="right"
      size="xl"
      title={detail.data ? <TaskDetailTitle detail={detail.data} /> : 'Task'}
    >
      <TaskDetailBody taskId={taskId} onClose={onClose} />
    </Drawer>
  )
}
